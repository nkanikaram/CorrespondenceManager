using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class CompositionInfo
    {
        [Required]
        public string? SolutionsId { get; set; }

        [Required]
        public string? VersionCode { get; set; }

        [StringLength(250)]
        public string? CompositionSchemaId { get; set; }
        

        /// <summary>
        /// Validates that no forbidden special characters exist in composition fields.
        /// </summary>
        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(SolutionsId, nameof(SolutionsId));
            ProcessRequestHelper.CheckField(VersionCode, nameof(VersionCode));
            ProcessRequestHelper.CheckField(CompositionSchemaId, nameof(CompositionSchemaId));
        }

        /// <summary>
        /// Trims whitespace from all string fields in this object.
        /// </summary>
        public void TrimAllStringFields()
        {
            SolutionsId = SolutionsId?.Trim();
            VersionCode = VersionCode?.Trim();
            CompositionSchemaId = CompositionSchemaId?.Trim();
        }
    }
}
