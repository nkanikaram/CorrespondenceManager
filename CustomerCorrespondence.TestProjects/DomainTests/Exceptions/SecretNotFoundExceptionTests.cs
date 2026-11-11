using CustomerCorrespondence.Exceptions;

namespace CustomerCorrespondenceTests.Exceptions
{
    [TestClass]
    public class SecretNotFoundExceptionTests
    {
        [TestMethod]
        public void Constructor_WithSecretName_CreatesExceptionWithFormattedMessage()
        {
            // Arrange
            var secretName = "MySecret";

            // Act
            var exception = new SecretNotFoundException(secretName);

            // Assert
            Assert.AreEqual($"Secret '{secretName}' was not found in Key Vault.", exception.Message);
            Assert.AreEqual("SECRET_NOT_FOUND", exception.ErrorCode);
            Assert.AreEqual(secretName, exception.SecretName);
        }

        [TestMethod]
        public void Constructor_WithSecretNameAndInnerException_CreatesExceptionWithInner()
        {
            // Arrange
            var secretName = "MySecret";
            var innerException = new InvalidOperationException("Key Vault error");

            // Act
            var exception = new SecretNotFoundException(secretName, innerException);

            // Assert
            Assert.AreEqual($"Secret '{secretName}' was not found in Key Vault.", exception.Message);
            Assert.AreEqual("SECRET_NOT_FOUND", exception.ErrorCode);
            Assert.AreEqual(secretName, exception.SecretName);
            Assert.IsNotNull(exception.InnerException);
            Assert.AreEqual("Key Vault error", exception.InnerException.Message);
        }

        [TestMethod]
        public void SecretNotFoundException_InheritsFromCorrespondenceException()
        {
            // Arrange & Act
            var exception = new SecretNotFoundException("Test");

            // Assert
            Assert.IsInstanceOfType(exception, typeof(CorrespondenceException));
        }

        [TestMethod]
        public void SecretNotFoundException_CanBeCaughtAsCorrespondenceException()
        {
            // Arrange
            var wasCaught = false;

            // Act
            try
            {
                throw new SecretNotFoundException("MySecret");
            }
            catch (CorrespondenceException ex)
            {
                wasCaught = true;
                Assert.AreEqual("SECRET_NOT_FOUND", ex.ErrorCode);
            }

            // Assert
            Assert.IsTrue(wasCaught);
        }

        [TestMethod]
        public void SecretNotFoundException_PreservesSecretNameWhenCaughtAsBase()
        {
            // Arrange
            var secretName = "DatabaseConnectionString";

            // Act
            try
            {
                throw new SecretNotFoundException(secretName);
            }
            catch (CorrespondenceException ex)
            {
                // Assert
                Assert.IsInstanceOfType(ex, typeof(SecretNotFoundException));
                var secretEx = (SecretNotFoundException)ex;
                Assert.AreEqual(secretName, secretEx.SecretName);
            }
        }

        [TestMethod]
        public void SecretName_IsReadOnly()
        {
            // Arrange & Act
            var exception = new SecretNotFoundException("MySecret");

            // Assert - Verify SecretName property exists and has a value
            Assert.AreEqual("MySecret", exception.SecretName);
            // Property is read-only (no setter), confirming proper encapsulation
        }

        [TestMethod]
        public void Constructor_WithEmptySecretName_CreatesExceptionWithEmptyName()
        {
            // Arrange & Act
            var exception = new SecretNotFoundException(string.Empty);

            // Assert
            Assert.AreEqual("Secret '' was not found in Key Vault.", exception.Message);
            Assert.AreEqual(string.Empty, exception.SecretName);
        }

        [TestMethod]
        public void Constructor_WithNullSecretName_CreatesExceptionWithNullName()
        {
            // Arrange & Act
            var exception = new SecretNotFoundException(null!);

            // Assert
            Assert.IsTrue(exception.Message.Contains("was not found in Key Vault"));
            Assert.IsNull(exception.SecretName);
        }

        [TestMethod]
        public void InnerException_IsPreserved()
        {
            // Arrange
            var innerException = new ArgumentException("Inner error");
            var secretName = "TestSecret";

            // Act
            var exception = new SecretNotFoundException(secretName, innerException);

            // Assert
            Assert.IsNotNull(exception.InnerException);
            Assert.AreSame(innerException, exception.InnerException);
            Assert.IsInstanceOfType(exception.InnerException, typeof(ArgumentException));
        }

        [TestMethod]
        public void ErrorCode_IsAlwaysSecretNotFound()
        {
            // Arrange
            var exception1 = new SecretNotFoundException("Secret1");
            var exception2 = new SecretNotFoundException("Secret2", new Exception());

            // Assert
            Assert.AreEqual("SECRET_NOT_FOUND", exception1.ErrorCode);
            Assert.AreEqual("SECRET_NOT_FOUND", exception2.ErrorCode);
        }
    }
}
