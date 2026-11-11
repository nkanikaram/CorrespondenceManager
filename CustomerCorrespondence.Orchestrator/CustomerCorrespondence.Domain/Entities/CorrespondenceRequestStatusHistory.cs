using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class CorrespondenceRequestStatusHistory
    {
        public Guid CorrespondenceRequestStatusHistoryId { get; set; }
        public Guid CorrespondenceId { get; set; }
        public int HistorySequenceNum { get; set; }
        [Required]
        public string? StatusCode { get; set; }
        public string? StatusUpdatedBy { get; set; }
        public DateTime StatusUpdateTS { get; set; }
        public string? SFMCEventInstanceId { get; set; }
    }
}
