using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class MailDeliveryInfo
    {
        [StringLength(60)]
        public string? RecipientFullName { get; set; }

        [StringLength(30)]
        public string? RecipientFirstName { get; set; }

        [StringLength(30)]
        public string? RecipientLastName { get; set; }

        [Required]
        public RecipientAddress? RecipientAddress { get; set; }

        public bool? NonUSAddressFlag { get; set; }
        public bool? BadAddressFlag { get; set; }

        /// <summary>
        /// Validates that no forbidden special characters exist in name and address fields.
        /// </summary>
        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(RecipientFullName, nameof(RecipientFullName));
            ProcessRequestHelper.CheckField(RecipientFirstName, nameof(RecipientFirstName));
            ProcessRequestHelper.CheckField(RecipientLastName, nameof(RecipientLastName));
            RecipientAddress?.ValidateNoSpecialCharacters();
        }

        /// <summary>
        /// Trims whitespace from all string fields in this object and nested objects.
        /// </summary>
        public void TrimAllStringFields()
        {
            RecipientFullName = RecipientFullName?.Trim();
            RecipientFirstName = RecipientFirstName?.Trim();
            RecipientLastName = RecipientLastName?.Trim();
            RecipientAddress?.TrimAllStringFields();
        }
    }
}
