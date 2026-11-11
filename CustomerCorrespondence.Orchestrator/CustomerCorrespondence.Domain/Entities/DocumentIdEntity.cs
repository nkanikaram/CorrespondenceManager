using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class DocumentIdEntity
    {
        public int SeqId { get; set; }
        public string DocIds { get; set; } = default!;
        public string DocIdStatus { get; set; } = default!;
        public DateTime? RequestCreationTS { get; set; } = DateTime.UtcNow;

    }
}
