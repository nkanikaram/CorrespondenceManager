using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CustomerCorrespondence.Domain.Entities
{
    [Table("CorrespondenceDocumentType", Schema = "dbo")]
    public class CorrespondenceDocumentType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CorrespondenceDocumentTypeId { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? DocumentSubType { get; set; }

        [StringLength(100)]
        public string? CabinetName { get; set; }
    }
}
