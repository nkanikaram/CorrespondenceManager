using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class CorrespondenceStatusCode
    {
        public Guid StatusId { get; set; }
        public required string StatusCode { get; set; }
        public required string StatusDescription { get; set; }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(StatusCode))
            {
                throw new InvalidOperationException("StatusCode is required and cannot be null or empty.");
            }

            if (string.IsNullOrWhiteSpace(StatusDescription))
            {
                throw new InvalidOperationException("StatusDescription is required and cannot be null or empty.");
            }
        }
    }
}
