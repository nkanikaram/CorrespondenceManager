using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class SolutionsTests
    {
        [TestMethod]
        public void Solutions_Properties_SetAndGetValues()
        {
            // Arrange
            var solutions = new Solutions();

            // Act
            solutions.solutionsId = "sol-001";
            solutions.solutionName = "Test Solution";
            solutions.description = "A test description";
            solutions.dataRetention = 365;
            solutions.customerId = "cust-123";

            // Assert
            Assert.AreEqual("sol-001", solutions.solutionsId);
            Assert.AreEqual("Test Solution", solutions.solutionName);
            Assert.AreEqual("A test description", solutions.description);
            Assert.AreEqual(365, solutions.dataRetention);
            Assert.AreEqual("cust-123", solutions.customerId);
        }

        [TestMethod]
        public void Solutions_DefaultValues_AreNullOrZero()
        {
            // Arrange
            var solutions = new Solutions();

            // Assert
            Assert.IsNull(solutions.solutionsId);
            Assert.IsNull(solutions.solutionName);
            Assert.IsNull(solutions.description);
            Assert.AreEqual(0, solutions.dataRetention);
            Assert.IsNull(solutions.customerId);
        }
    }
}