using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class VentureFusionTokenOptions
    {

        public string? grant_type { get; set; }
        public string? scope { get; set; }
        private string? client_id { get; set; }
        private string? client_secret { get; set; }
        private string? tenant_id { get; set; }
        public string? credentials { get; set; }
        public string? encodedAuthenticationString { get; set; }
        public string? apimSubscriptionKey { get; set; }
        public string? tokenEndpoint { get; set; }

        public VentureFusionTokenOptions()
        {
            tenant_id = Environment.GetEnvironmentVariable("VentureFusionService:TenantId");
            client_id = Environment.GetEnvironmentVariable("VentureFusionService:ClientId");
            client_secret = Environment.GetEnvironmentVariable("VentureFusionService:ClientSecret");
            apimSubscriptionKey = Environment.GetEnvironmentVariable("VentureFusionService:ApimSubscriptionKey");
            grant_type = "client_credentials";
            scope = $"https://venturestageb2c.onmicrosoft.com/4422d0f6-7702-40d2-9883-eb308ebd7693/.default";
            tokenEndpoint = $"https://login.microsoftonline.com/{tenant_id}/oauth2/v2.0/token";

            var authenticationString = $"{client_id}:{client_secret}";
            encodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));
        }
    }
}
