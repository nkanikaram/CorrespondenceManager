using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ProcessRequestEndPoint.Handlers
{
    /// <summary>
    /// Class to Create JWT Events
    /// </summary>
    [ExcludeFromCodeCoverageAttribute]
    public static class JwtEventHandler
    {
        /// <summary>
        /// Creates one or more JWT event handlers.
        /// </summary>
        /// <returns>
        /// A JwtBearerEvents object instance.
        /// </returns>
        public static JwtBearerEvents CreateJwtEvents()
        {
            var events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Debug.WriteLine("OnAuthenticationFailed: " + context.Exception.Message);
                    return Task.FromResult(0);
                },
                OnTokenValidated = context =>
                {
                    Debug.WriteLine("OnTokenValidated: " + context.SecurityToken);
                    // re-adding as claims which .net core understands for some standard role-based authorization
                    var claims = new List<Claim>();

                    //populating custom claims based on the information in the JWT. Using Microsoft standard role namespaces so that they can be used in the controllers with the Authorize tags.
                    int claimUserIDVal = 0;
                    if (context.Principal != null)
                    {
                        claimUserIDVal = context.Principal.Claims.Where(c => c.Type == "username" && c.Value.Length > 0).Count();
                    }

                    if (claimUserIDVal > 0)
                    {
                        claims.Add(new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "Everyone"));
                    }

                    int claimMemberOfVal = 0;
                    if (context.Principal != null)
                    {
                        claimMemberOfVal = context.Principal.Claims.Where(c => c.Type == "memberof" && c.Value.Length > 0).Count();
                    }

                    if (claimMemberOfVal > 0 && context.Principal != null)
                    {
                        var memberofClaim = context.Principal.Claims.Where(c => c.Type == "memberof").FirstOrDefault();
                        if (memberofClaim != null && !string.IsNullOrEmpty(memberofClaim.Value))
                        {
                            string[] membersofList = memberofClaim.Value.Split(":");

                            foreach (var group in membersofList)
                            {
                                var cleanRole = group.Trim();
                                if (cleanRole.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                                {
                                    cleanRole = cleanRole.Substring(3);
                                }
                                claims.Add(new Claim(ClaimTypes.Role, cleanRole));
                            }
                        }
                    }

                    if (claims.Count > 0)
                    {
                        var appIdentity = new ClaimsIdentity(claims);
                        context.Principal?.AddIdentity(appIdentity);
                    }

                    //Add claim if yes
                    return Task.FromResult(0);
                }
            };
            return events;
        }
    }
}