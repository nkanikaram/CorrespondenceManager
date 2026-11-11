using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class RecipientAddress
    {
        [StringLength(35)]
        public string? RecipientAddressLine1 { get; set; }

        [StringLength(35)]
        public string? RecipientAddressLine2 { get; set; }

        [StringLength(35)]
        public string? RecipientAddressLine3 { get; set; }

        //[StringLength(35)]
        //public string? RecipientAddressLine4 { get; set; }

        //[StringLength(35)]
        //public string? RecipientAddressLine5 { get; set; }

        [StringLength(100)]
        public string? RecipientAddressCity { get; set; }

        [StringLength(40)]
        public string? RecipientAddressST { get; set; }

        [StringLength(20)]
        public string? RecipientAddressZIP { get; set; }

        [StringLength(60)]
        public string? RecipientCountry { get; set; }

        /// <summary>
        /// Validates that no forbidden special characters exist in address fields.
        /// </summary>
        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(RecipientAddressLine1, nameof(RecipientAddressLine1));
            ProcessRequestHelper.CheckField(RecipientAddressLine2, nameof(RecipientAddressLine2));
            ProcessRequestHelper.CheckField(RecipientAddressLine3, nameof(RecipientAddressLine3));
            ProcessRequestHelper.CheckField(RecipientAddressCity, nameof(RecipientAddressCity));
            ProcessRequestHelper.CheckField(RecipientAddressST, nameof(RecipientAddressST));
            ProcessRequestHelper.CheckField(RecipientAddressZIP, nameof(RecipientAddressZIP));
            ProcessRequestHelper.CheckField(RecipientCountry, nameof(RecipientCountry));
        }

        /// <summary>
        /// Trims whitespace from all string fields in this object.
        /// </summary>
        public void TrimAllStringFields()
        {
            RecipientAddressLine1 = RecipientAddressLine1?.Trim();
            RecipientAddressLine2 = RecipientAddressLine2?.Trim();
            RecipientAddressLine3 = RecipientAddressLine3?.Trim();
            RecipientAddressCity = RecipientAddressCity?.Trim();
            RecipientAddressST = RecipientAddressST?.Trim();
            RecipientAddressZIP = RecipientAddressZIP?.Trim();
            RecipientCountry = RecipientCountry?.Trim();
        }
    }
}
