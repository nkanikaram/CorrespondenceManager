using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class VentureFusionServiceCredentials
    {
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? ApimSubscriptionKey { get; set; }
        public Uri? Uri { get; set; }
        public string? Scope { get; set; }
        public string? TokenEndpoint { get; set; }
        public string? grant_type { get; set; }
        public string GetEncodedAuthenticationString()
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        }
    }
}
