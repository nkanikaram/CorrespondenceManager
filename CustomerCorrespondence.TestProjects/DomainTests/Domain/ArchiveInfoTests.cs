using CustomerCorrespondence.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;


namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class ArchiveInfoTests
    {
        //[TestMethod]
        //public void ArchiveInfo_ValidProperties_PassesValidation()
        //{
        //    var archiveInfo = new ArchiveInfo
        //    {
        //        ArchivePDFDocumentFlag = true,
        //        DocumentTypeCode = "TYPE1",
        //        DocumentSubTypeCode = "SUBTYPE1"
        //    };

        //    var context = new ValidationContext(archiveInfo);
        //    var results = new List<ValidationResult>();

        //    bool isValid = Validator.TryValidateObject(archiveInfo, context, results, true);

        //    Assert.IsTrue(isValid);
        //    Assert.AreEqual(0, results.Count);
        //}

        //[TestMethod]
        //public void ArchiveInfo_MissingDocumentTypeCode_FailsValidation()
        //{
        //    var archiveInfo = new ArchiveInfo
        //    {
        //        ArchivePDFDocumentFlag = false,
        //        DocumentTypeCode = string.Empty,
        //        DocumentSubTypeCode = "SUBTYPE1"
        //    };

        //    var context = new ValidationContext(archiveInfo);
        //    var results = new List<ValidationResult>();

        //    bool isValid = Validator.TryValidateObject(archiveInfo, context, results, true);

        //    Assert.IsFalse(isValid);
        //    Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(ArchiveInfo.DocumentTypeCode))));
        //}

        //[TestMethod]
        //public void ArchiveInfo_MissingDocumentSubTypeCode_FailsValidation()
        //{
        //    var archiveInfo = new ArchiveInfo
        //    {
        //        ArchivePDFDocumentFlag = false,
        //        DocumentTypeCode = "TYPE1",
        //        DocumentSubTypeCode = string.Empty
        //    };

        //    var context = new ValidationContext(archiveInfo);
        //    var results = new List<ValidationResult>();

        //    bool isValid = Validator.TryValidateObject(archiveInfo, context, results, true);

        //    Assert.IsFalse(isValid);
        //    Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(ArchiveInfo.DocumentSubTypeCode))));
        //}
    }
}