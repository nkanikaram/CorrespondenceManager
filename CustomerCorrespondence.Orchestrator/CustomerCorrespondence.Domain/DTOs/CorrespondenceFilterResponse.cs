namespace CustomerCorrespondence.Domain.DTOs
{
    /// <summary>
    /// Response DTO for filtered correspondence data.
    /// Contains correspondence details including current status information.
    /// </summary>
    public class CorrespondenceFilterResponse
    {
        /// <summary>
        /// The document ID for the correspondence.
        /// </summary>
        public string? DocId { get; set; }

        /// <summary>
        /// The unique identifier for the correspondence.
        /// </summary>
        public Guid CorrespondenceId { get; set; }

        /// <summary>
        /// The timestamp when the correspondence request was created.
        /// </summary>
        public DateTimeOffset RequestCreationTS { get; set; }

        /// <summary>
        /// The name of the delivery method 
        /// </summary>
        public string? DeliveryMethodName { get; set; }

        /// <summary>
        /// The current status code for the correspondence.
        /// </summary>
        public string? CurrentStatusCode { get; set; }

        /// <summary>
        /// Human-readable description of the current status.
        /// </summary>
        public string? CurrentStatusDescription { get; set; }

        /// <summary>
        /// The description of the document type 
        /// </summary>
        public string? DocumentDescription { get; set; }

        /// <summary>
        /// The name of the requesting system.
        /// </summary>
        public string? RequestingSystemName { get; set; }
    }
}
