using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using System.Text;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class PgpEncryptionTests
    {
        private Mock<ISecretsCacheService> _mockSecretsCacheService = null!;
        private Mock<IConfiguration> _mockConfiguration = null!;
        private PgpEncryptionService _service = null!;
        private string _testPublicKey = null!;
        private string _testPrivateKey = null!;
        private string _testPassphrase = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockSecretsCacheService = new Mock<ISecretsCacheService>();
            _mockConfiguration = new Mock<IConfiguration>();

            // Generate test PGP key pair
            _testPassphrase = "TestPassphrase123!";
            (_testPublicKey, _testPrivateKey) = GenerateTestKeyPair(_testPassphrase);

            // Setup configuration mock
            SetupConfiguration();

            // Setup secrets cache service mock
            SetupSecretsCacheService();

            _service = new PgpEncryptionService(_mockSecretsCacheService.Object, _mockConfiguration.Object);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Cleanup if needed
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithNullSecretsCacheService_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            var exception = Assert.ThrowsException<ArgumentNullException>(() =>
                new PgpEncryptionService(null!, _mockConfiguration.Object));

            Assert.AreEqual("secretsCacheService", exception.ParamName);
        }

        [TestMethod]
        public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            var exception = Assert.ThrowsException<ArgumentNullException>(() =>
                new PgpEncryptionService(_mockSecretsCacheService.Object, null!));

            Assert.AreEqual("configuration", exception.ParamName);
        }

        [TestMethod]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Arrange & Act
            var service = new PgpEncryptionService(_mockSecretsCacheService.Object, _mockConfiguration.Object);

            // Assert
            Assert.IsNotNull(service);
        }

        #endregion

        #region EncryptAsync Tests

        [TestMethod]
        public async Task EncryptAsync_WithNullPlainText_ReturnsNull()
        {
            // Arrange
            string? plainText = null;

            // Act
            var result = await _service.EncryptAsync(plainText!);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task EncryptAsync_WithEmptyPlainText_ReturnsEmpty()
        {
            // Arrange
            var plainText = string.Empty;

            // Act
            var result = await _service.EncryptAsync(plainText);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public async Task EncryptAsync_WithValidPlainText_ReturnsEncryptedText()
        {
            // Arrange
            var plainText = "This is a test message to encrypt.";

            // Act
            var result = await _service.EncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result));
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
            StringAssert.Contains(result, "-----END PGP MESSAGE-----");
            Assert.IsFalse(result.Contains(plainText)); // Should not contain plain text
        }

        [TestMethod]
        public async Task EncryptAsync_WithMissingPublicKeySecretName_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockConfiguration.Setup(c => c["SecretNames:PGPPublicKey"]).Returns((string?)null);
            var plainText = "Test message";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.EncryptAsync(plainText));

            StringAssert.Contains(exception.Message, "PGPPublicKey secret name is not configured");
        }

        [TestMethod]
        public async Task EncryptAsync_WithMissingPublicKeyInVault_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockSecretsCacheService.Setup(s => s.GetSecretAsync("PGPPublicKey"))
                .ReturnsAsync((string?)null);
            var plainText = "Test message";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.EncryptAsync(plainText));

            StringAssert.Contains(exception.Message, "PGPPublicKey not found in Key Vault");
        }

        [TestMethod]
        public async Task EncryptAsync_WithSpecialCharacters_EncryptsSuccessfully()
        {
            // Arrange
            var plainText = "Special chars: !@#$%^&*()_+-=[]{}|;:',.<>?/~`";

            // Act
            var result = await _service.EncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
        }

        [TestMethod]
        public async Task EncryptAsync_WithUnicodeCharacters_EncryptsSuccessfully()
        {
            // Arrange
            var plainText = "Unicode: 你好世界 🌍 Émilie Çağlar";

            // Act
            var result = await _service.EncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
        }

        [TestMethod]
        public async Task EncryptAsync_WithLargeText_EncryptsSuccessfully()
        {
            // Arrange
            var plainText = new string('A', 10000); // 10KB of text

            // Act
            var result = await _service.EncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
        }

        #endregion

        #region DecryptAsync Tests

        [TestMethod]
        public async Task DecryptAsync_WithNullEncryptedText_ReturnsNull()
        {
            // Arrange
            string? encryptedText = null;

            // Act
            var result = await _service.DecryptAsync(encryptedText!);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task DecryptAsync_WithEmptyEncryptedText_ReturnsEmpty()
        {
            // Arrange
            var encryptedText = string.Empty;

            // Act
            var result = await _service.DecryptAsync(encryptedText);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public async Task DecryptAsync_WithValidEncryptedText_ReturnsDecryptedText()
        {
            // Arrange
            var originalText = "This is a test message.";
            var encryptedText = await _service.EncryptAsync(originalText);

            // Act
            var result = await _service.DecryptAsync(encryptedText);

            // Assert
            Assert.AreEqual(originalText, result);
        }

        [TestMethod]
        public async Task DecryptAsync_WithMissingPrivateKeySecretName_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockConfiguration.Setup(c => c["SecretNames:PGPPrivateKey"]).Returns((string?)null);
            var encryptedText = "-----BEGIN PGP MESSAGE-----\ntest\n-----END PGP MESSAGE-----";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.DecryptAsync(encryptedText));

            StringAssert.Contains(exception.Message, "SecretNames:PGPPrivateKey is not configured");
        }

        [TestMethod]
        public async Task DecryptAsync_WithMissingPassphraseSecretName_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockConfiguration.Setup(c => c["SecretNames:PGPPassphrase"]).Returns((string?)null);
            var encryptedText = "-----BEGIN PGP MESSAGE-----\ntest\n-----END PGP MESSAGE-----";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.DecryptAsync(encryptedText));

            StringAssert.Contains(exception.Message, "SecretNames:PGPPassphrase is not configured");
        }

        [TestMethod]
        public async Task DecryptAsync_WithInvalidEncryptedText_ThrowsInvalidOperationException()
        {
            // Arrange
            var invalidEncryptedText = "This is not encrypted text";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.DecryptAsync(invalidEncryptedText));

            StringAssert.Contains(exception.Message, "PGP decryption failed");
        }

        [TestMethod]
        public async Task DecryptAsync_WithSpecialCharacters_DecryptsCorrectly()
        {
            // Arrange
            var originalText = "Special: !@#$%^&*()_+-=[]{}|;:',.<>?/~`";
            var encryptedText = await _service.EncryptAsync(originalText);

            // Act
            var result = await _service.DecryptAsync(encryptedText);

            // Assert
            Assert.AreEqual(originalText, result);
        }

        [TestMethod]
        public async Task DecryptAsync_WithUnicodeCharacters_DecryptsCorrectly()
        {
            // Arrange
            var originalText = "Unicode: 你好世界 🌍 Émilie Çağlar";
            var encryptedText = await _service.EncryptAsync(originalText);

            // Act
            var result = await _service.DecryptAsync(encryptedText);

            // Assert
            Assert.AreEqual(originalText, result);
        }

        #endregion

        #region SignAndEncryptAsync Tests

        [TestMethod]
        public async Task SignAndEncryptAsync_WithNullPlainText_ReturnsNull()
        {
            // Arrange
            string? plainText = null;

            // Act
            var result = await _service.SignAndEncryptAsync(plainText!);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task SignAndEncryptAsync_WithEmptyPlainText_ReturnsEmpty()
        {
            // Arrange
            var plainText = string.Empty;

            // Act
            var result = await _service.SignAndEncryptAsync(plainText);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public async Task SignAndEncryptAsync_WithValidPlainText_ReturnsSignedAndEncryptedText()
        {
            // Arrange
            var plainText = "This is a test message to sign and encrypt.";

            // Act
            var result = await _service.SignAndEncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result));
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
            StringAssert.Contains(result, "-----END PGP MESSAGE-----");
        }

        [TestMethod]
        public async Task SignAndEncryptAsync_WithMissingSecretNames_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockConfiguration.Setup(c => c["SecretNames:PGPPublicKey"]).Returns((string?)null);
            var plainText = "Test message";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.SignAndEncryptAsync(plainText));

            StringAssert.Contains(exception.Message, "PGP secret names are not properly configured");
        }

        [TestMethod]
        public async Task SignAndEncryptAsync_WithMissingKeys_ThrowsInvalidOperationException()
        {
            // Arrange
            _mockSecretsCacheService.Setup(s => s.GetSecretAsync("PGPPublicKey"))
                .ReturnsAsync((string?)null);
            var plainText = "Test message";

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.SignAndEncryptAsync(plainText));

            StringAssert.Contains(exception.Message, "PGP keys or passphrase not found in Key Vault");
        }

        [TestMethod]
        public async Task SignAndEncryptAsync_WithLargeText_SuccessfullySignsAndEncrypts()
        {
            // Arrange
            var plainText = new string('B', 5000);

            // Act
            var result = await _service.SignAndEncryptAsync(plainText);

            // Assert
            Assert.IsNotNull(result);
            StringAssert.Contains(result, "-----BEGIN PGP MESSAGE-----");
        }

        #endregion

        #region VerifyAndDecryptAsync Tests

        [TestMethod]
        public async Task VerifyAndDecryptAsync_WithValidSignedAndEncryptedText_ReturnsTrue()
        {
            // Arrange
            var plainText = "Test message";
            var signedAndEncrypted = await _service.SignAndEncryptAsync(plainText);

            // Act
            var result = await _service.VerifyAndDecryptAsync(signedAndEncrypted);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task VerifyAndDecryptAsync_WithInvalidText_ReturnsFalse()
        {
            // Arrange
            var invalidText = "This is not encrypted or signed";

            // Act
            var result = await _service.VerifyAndDecryptAsync(invalidText);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task VerifyAndDecryptAsync_WithNullText_ReturnsFalse()
        {
            // Arrange
            string? nullText = null;

            // Act
            var result = await _service.VerifyAndDecryptAsync(nullText!);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task VerifyAndDecryptAsync_WithEmptyText_ReturnsFalse()
        {
            // Arrange
            var emptyText = string.Empty;

            // Act
            var result = await _service.VerifyAndDecryptAsync(emptyText);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region Integration Tests

        [TestMethod]
        public async Task EncryptThenDecrypt_PreservesOriginalText()
        {
            // Arrange
            var originalText = "The quick brown fox jumps over the lazy dog.";

            // Act
            var encrypted = await _service.EncryptAsync(originalText);
            var decrypted = await _service.DecryptAsync(encrypted);

            // Assert
            Assert.AreEqual(originalText, decrypted);
        }

        [TestMethod]
        public async Task SignAndEncryptThenDecrypt_PreservesOriginalText()
        {
            // Arrange
            var originalText = "Signed message content.";

            // Act
            var signedAndEncrypted = await _service.SignAndEncryptAsync(originalText);
            var decrypted = await _service.DecryptAsync(signedAndEncrypted);

            // Assert
            Assert.AreEqual(originalText, decrypted);
        }

        [TestMethod]
        public async Task MultipleEncryptOperations_ProduceDifferentOutputs()
        {
            // Arrange
            var plainText = "Same text";

            // Act
            var encrypted1 = await _service.EncryptAsync(plainText);
            // Small delay to ensure different timestamps
            await Task.Delay(10);
            var encrypted2 = await _service.EncryptAsync(plainText);

            // Assert
            Assert.AreNotEqual(encrypted1, encrypted2); // Should be different due to random padding
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("a")]
        [DataRow("Short text")]
        [DataRow("This is a longer text that spans multiple lines.\nIt includes line breaks.\nAnd various characters: 123, !@#")]
        public async Task EncryptDecryptCycle_WithVariousTextLengths_WorksCorrectly(string text)
        {
            // Arrange & Act
            if (string.IsNullOrEmpty(text))
            {
                var result = await _service.EncryptAsync(text);
                Assert.AreEqual(text, result);
                return;
            }

            var encrypted = await _service.EncryptAsync(text);
            var decrypted = await _service.DecryptAsync(encrypted);

            // Assert
            Assert.AreEqual(text, decrypted);
        }

        #endregion

        #region Helper Methods

        private void SetupConfiguration()
        {
            _mockConfiguration.Setup(c => c["SecretNames:PGPPublicKey"]).Returns("PGPPublicKey");
            _mockConfiguration.Setup(c => c["SecretNames:PGPPrivateKey"]).Returns("PGPPrivateKey");
            _mockConfiguration.Setup(c => c["SecretNames:PGPPassphrase"]).Returns("PGPPassphrase");
        }

        private void SetupSecretsCacheService()
        {
            _mockSecretsCacheService.Setup(s => s.GetSecretAsync("PGPPublicKey"))
                .ReturnsAsync(_testPublicKey);

            _mockSecretsCacheService.Setup(s => s.GetSecretAsync("PGPPrivateKey"))
                .ReturnsAsync(_testPrivateKey);

            _mockSecretsCacheService.Setup(s => s.GetSecretAsync("PGPPassphrase"))
                .ReturnsAsync(_testPassphrase);
        }

        private static (string publicKey, string privateKey) GenerateTestKeyPair(string passphrase)
        {
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(new RsaKeyGenerationParameters(
                BigInteger.ValueOf(0x10001),
                new SecureRandom(),
                2048,
                12));
            var keyPair = keyPairGenerator.GenerateKeyPair();

            // FIX: Wrap AsymmetricCipherKeyPair in PgpKeyPair
            var pgpKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, keyPair, DateTime.UtcNow);

            var keyRingGenerator = new PgpKeyRingGenerator(
                PgpSignature.DefaultCertification,
                pgpKeyPair,
                "test@example.com",
                SymmetricKeyAlgorithmTag.Aes256,
                passphrase.ToCharArray(),
                true,
                null,
                null,
                new SecureRandom());

            var publicKeyRing = keyRingGenerator.GeneratePublicKeyRing();
            var secretKeyRing = keyRingGenerator.GenerateSecretKeyRing();

            // Export public key
            using var publicKeyStream = new MemoryStream();
            using (var armoredOutputStream = new ArmoredOutputStream(publicKeyStream))
            {
                publicKeyRing.Encode(armoredOutputStream);
            }
            var publicKey = Encoding.UTF8.GetString(publicKeyStream.ToArray());

            // Export private key
            using var privateKeyStream = new MemoryStream();
            using (var armoredOutputStream = new ArmoredOutputStream(privateKeyStream))
            {
                secretKeyRing.Encode(armoredOutputStream);
            }
            var privateKey = Encoding.UTF8.GetString(privateKeyStream.ToArray());

            return (publicKey, privateKey);
        }

        #endregion
    }
}
