using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class CorrespondenceRequest
    {
        public Guid CorrespondenceId { get; set; }
        public string? DocId { get; set; }
        public string? RequestingDomainName { get; set; }
        public required string DeliveryMethodCode { get; set; }
        public string? VersionCode { get; set; }
        public string? ArchiveDocumentTypeCode { get; set; }
        public string? ArchiveDocumentSubTypeCode { get; set; }
        public byte[]? PolicyNumber { get; set; } // PGP encrypted
        public byte[]? PolicyNumberHash { get; set; } // SHA-256 hash for searches
        public string? ClaimNumber { get; set; }
        public string? CustomerId { get; set; }
        public byte[]? RequestPayload { get; set; }
        public DateTime RequestCreationTS { get; set; }
        public string? VFResponseId { get; set; }

        public ICollection<CorrespondenceRequestStatusHistory> StatusHistories { get; set; } = new List<CorrespondenceRequestStatusHistory>();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DeliveryMethodCode))
            {
                throw new InvalidOperationException("DeliveryMethodCode is required and cannot be null or empty.");
            }
        }
    }
}
