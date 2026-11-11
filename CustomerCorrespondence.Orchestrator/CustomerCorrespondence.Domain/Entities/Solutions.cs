using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class Solutions
    {
        public string? solutionsId { get; set; }
        public string? solutionName { get; set; }
        public string? description { get; set; }
        public int dataRetention { get; set; }
        public string? customerId { get; set; }
        public int usageId { get; set; }
        public string? welcomeKitSolutionsId { get; set; }
        public string? welcomeLetterSolutionsId { get; set; }
    }
}
