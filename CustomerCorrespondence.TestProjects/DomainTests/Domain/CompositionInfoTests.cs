using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CustomerCorrespondence.Domain.Entities;


namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class CompositionInfoTests
    {
        [TestMethod]
        public void Can_Create_CompositionInfo_With_Required_Fields()
        {
            var info = new CompositionInfo
            {
                VersionCode = "V1",
                CompositionSchemaId = "SCHEMA1",
                 };
            Assert.AreEqual("V1", info.VersionCode);
            Assert.AreEqual("SCHEMA1", info.CompositionSchemaId);
        }

        [TestMethod]
        public void CompositionInfo_Required_Fields_Validation()
        {
            var info = new CompositionInfo
            {
                VersionCode = string.Empty,
            };

            var context = new ValidationContext(info);
            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(info, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(CompositionInfo.VersionCode))));  
        }

        [TestMethod]
        public void CompositionSchemaId_Should_Not_Exceed_MaxLength()
        {
            var info = new CompositionInfo
            {
                VersionCode = "V1",
                CompositionSchemaId = new string('a', 251)
            };

            var context = new ValidationContext(info);
            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(info, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(CompositionInfo.CompositionSchemaId))));
        }
    }
}