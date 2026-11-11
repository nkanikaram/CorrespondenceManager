using System.ComponentModel.DataAnnotations;

namespace CustomerCorrespondence.Domain.DTOs
{
    /// <summary>
    /// Request DTO for filtering correspondence data based on search criteria.
    /// </summary>
    public class CorrespondenceFilterRequest
    {
        /// <summary>
        /// The name of the requesting system 
        /// </summary>
        [Required]
        public string RequestingSystemName { get; set; } = string.Empty;

        /// <summary>
        /// The description of the document type 
        /// </summary>
        public string? DocumentDescription { get; set; }

        /// <summary>
        /// The delivery method for correspondence
        /// </summary>
        public string? DeliveryMethod { get; set; }

        /// <summary>
        /// The status code to filter by 
        /// </summary>
        public string? CorrespondenceRequestStatusCode { get; set; }

        /// <summary>
        /// The start date for filtering correspondence creation timestamp.
        /// Expected format: MM/dd/yyyy (e.g., "01/01/2026").
        /// </summary>
        [Required]
        public string CreateBeginDate { get; set; } = string.Empty;

        /// <summary>
        /// The end date for filtering correspondence creation timestamp.
        /// Expected format: MM/dd/yyyy (e.g., "02/26/2026").
        /// If not provided, defaults to today's date.
        /// </summary>
        public string? CreateEndDate { get; set; }

        /// <summary>
        /// Optional policy number to filter by. If provided, it will be hashed
        /// before querying the database for security purposes.
        /// </summary>
        public string? PolicyNumber { get; set; }
    }
}
