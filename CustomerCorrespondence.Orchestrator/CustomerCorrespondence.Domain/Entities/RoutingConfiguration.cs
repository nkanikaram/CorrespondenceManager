using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class RoutingConfiguration
    {
        public int Id { get; set; }
        public string? RouteName { get; set; }
        public string? CustomerName { get; set; }
        public string? CorrespondenceMethod { get; set; }

        // Store as semicolon-separated values instead of JSON
        public string RouteData { get; set; } = string.Empty;

        // Computed property that converts to/from simple format
        public List<string> Route
        {
            get => string.IsNullOrEmpty(RouteData)
                ? new List<string>()
                : RouteData.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            set => RouteData = string.Join(";", value);
        }
    }
}
