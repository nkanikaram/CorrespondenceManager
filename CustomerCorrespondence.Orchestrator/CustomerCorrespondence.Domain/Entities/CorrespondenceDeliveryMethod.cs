using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class CorrespondenceDeliveryMethod
    {
        public Guid DeliveryMethodId { get; set; }
        public required string DeliveryMethodCode { get; set; }
        public required string DeliveryMethodName { get; set; }

        public CorrespondenceDeliveryMethod()
        {
            //Validate();
        }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DeliveryMethodCode))
            {
                throw new InvalidOperationException("DeliveryMethodCode is required and cannot be null or empty.");
            }

            if (string.IsNullOrWhiteSpace(DeliveryMethodName))
            {
                throw new InvalidOperationException("DeliveryMethodName is required and cannot be null or empty.");
            }
        }
    }
}
