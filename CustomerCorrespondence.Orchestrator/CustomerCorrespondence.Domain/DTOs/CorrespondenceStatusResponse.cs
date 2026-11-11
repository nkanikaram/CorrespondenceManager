using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.DTOs
{
    /// <summary>
    /// Represents the response containing correspondence status information.
    /// </summary>
    public class CorrespondenceStatusResponse
    {
        /// <summary>Gets or sets the document identifier.</summary>
        public string? DocId { get; set; }
        /// <summary>Gets or sets the correspondence identifier.</summary>
        public Guid CorrespondenceId { get; set; }
        /// <summary>Gets or sets the name of the requesting system.</summary>
        public string? RequestingSystemName { get; set; }
        /// <summary>Gets or sets the policy number.</summary>
        public string? PolicyNumber { get; set; }
        /// <summary>Gets or sets the claim number.</summary>
        public string? ClaimNumber { get; set; }
        /// <summary>Gets or sets the customer identifier.</summary>
        public string? CustomerId { get; set; }
        /// <summary>Gets or sets the delivery method.</summary>
        public string? DeliveryMethod { get; set; }
        /// <summary>Gets or sets the document type.</summary>
        public string? DocumentType { get; set; }
        /// <summary>Gets or sets the document type details.</summary>
        public DocumentTypeInfo? DocumentTypeDetails { get; set; }
        /// <summary>Gets or sets the request creation date.</summary>
        public DateTime RequestCreationDate { get; set; }
        /// <summary>Gets or sets the current status information.</summary>
        public CurrentStatusInfo CurrentStatus { get; set; } = new();
        /// <summary>Gets or sets the status history.</summary>
        public List<StatusHistoryInfo> StatusHistory { get; set; } = new();
        /// <summary>Gets or sets the processing information.</summary>
        public ProcessingInfo? ProcessingInfo { get; set; }
    }

    /// <summary>
    /// Represents the current status of a correspondence request.
    /// </summary>
    public class CurrentStatusInfo
    {
        /// <summary>Gets or sets the status code.</summary>
        public string StatusCode { get; set; } = string.Empty;
        /// <summary>Gets or sets the status description.</summary>
        public string StatusDescription { get; set; } = string.Empty;
        /// <summary>Gets or sets the last updated timestamp.</summary>
        public DateTime LastUpdated { get; set; }
        /// <summary>Gets or sets the user who last updated the status.</summary>
        public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a single entry in the status history.
    /// </summary>
    public class StatusHistoryInfo
    {
        /// <summary>Gets or sets the sequence number.</summary>
        public int SequenceNumber { get; set; }
        /// <summary>Gets or sets the status code.</summary>
        public string StatusCode { get; set; } = string.Empty;
        /// <summary>Gets or sets the status description.</summary>
        public string StatusDescription { get; set; } = string.Empty;
        /// <summary>Gets or sets the date the status was updated.</summary>
        public DateTime UpdatedDate { get; set; }
        /// <summary>Gets or sets the user who updated the status.</summary>
        public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents processing information for a correspondence request.
    /// </summary>
    public class ProcessingInfo
    {
        /// <summary>Gets or sets whether VentureFusion processing completed.</summary>
        public bool? VentureFusionProcessed { get; set; }
        /// <summary>Gets or sets the VentureFusion processing status.</summary>
        public string? VentureFusionStatus { get; set; }
        /// <summary>Gets or sets the VentureFusion processing timestamp.</summary>
        public DateTime? VentureFusionTimestamp { get; set; }
        /// <summary>Gets or sets the step results dictionary.</summary>
        public Dictionary<string, object>? StepResults { get; set; }
    }

    /// <summary>
    /// Represents document type information.
    /// </summary>
    public class DocumentTypeInfo
    {
        /// <summary>Gets or sets the document type.</summary>
        public string DocumentType { get; set; } = string.Empty;
        /// <summary>Gets or sets the document sub-type.</summary>
        public string? DocumentSubType { get; set; }
        /// <summary>Gets or sets the cabinet name.</summary>
        public string? CabinetName { get; set; }
    }
}
