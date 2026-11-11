using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class SequentialProcessRequest : IMessage
    {
        public required SequentialProcess Process { get; set; }

        // Initialize Route as an empty array to avoid null issues
        public List<string> Route { get; set; } = new List<string>();

        public void Validate()
        {
            // Validate Process
            if (Process == null)
            {
                throw new InvalidOperationException("Process is required and cannot be null.");
            }

            //// Validate Route
            //if (Route == null || Route.Count == 0)
            //{
            //    throw new InvalidOperationException("Route is required and cannot be null or empty.");
            //}

            //if (Route.Any(r => string.IsNullOrWhiteSpace(r)))
            //{
            //    throw new InvalidOperationException("Route contains invalid or empty endpoint names.");
            //}
        }
    }
}
