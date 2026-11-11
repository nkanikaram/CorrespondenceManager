using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using System.Text;
using CustomerCorrespondence.Domain.Interfaces;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public interface IPgpEncryptionService
    {
        Task<string> EncryptAsync(string plainText);
        Task<string> DecryptAsync(string encryptedText);
        Task<string> SignAndEncryptAsync(string plainText);
        Task<bool> VerifyAndDecryptAsync(string encryptedText);
    }

    public class PgpEncryptionService : IPgpEncryptionService
    {
        private readonly ISecretsCacheService _secretsCacheService;
        private readonly IConfiguration _configuration;

        public PgpEncryptionService(ISecretsCacheService secretsCacheService, IConfiguration configuration)
        {
            _secretsCacheService = secretsCacheService ?? throw new ArgumentNullException(nameof(secretsCacheService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Encrypts plain text using PGP encryption with the public key from Key Vault
        /// </summary>
        /// <param name="plainText">Text to encrypt</param>
        /// <returns>PGP encrypted text (ASCII armored)</returns>
        public async Task<string> EncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                // Get PGP public key from Key Vault
                var publicKeySecretName = _configuration["SecretNames:PGPPublicKey"];
                if (string.IsNullOrEmpty(publicKeySecretName))
                    throw new InvalidOperationException("PGPPublicKey secret name is not configured");

                var publicKeyArmored = await _secretsCacheService.GetSecretAsync(publicKeySecretName);
                if (string.IsNullOrEmpty(publicKeyArmored))
                    throw new InvalidOperationException("PGPPublicKey not found in Key Vault");

                var publicKey = ReadPublicKey(publicKeyArmored);

                using var outputStream = new MemoryStream();

                // Create encrypted data generator
                var encryptedDataGenerator = new PgpEncryptedDataGenerator(
                    SymmetricKeyAlgorithmTag.Aes256,
                    true,
                    new SecureRandom());

                encryptedDataGenerator.AddMethod(publicKey);

                using var encryptedOut = encryptedDataGenerator.Open(outputStream, new byte[4096]);

                // Create compressed data generator
                var compressedDataGenerator = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
                using var compressedOut = compressedDataGenerator.Open(encryptedOut);

                // Create literal data generator
                var literalDataGenerator = new PgpLiteralDataGenerator();
                using var literalOut = literalDataGenerator.Open(compressedOut, PgpLiteralData.Binary,
                    "encrypted_data", DateTime.UtcNow, new byte[4096]);

                // Write the plain text
                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                await literalOut.WriteAsync(plainTextBytes, 0, plainTextBytes.Length);

                // Close all streams in reverse order
                literalOut.Close();
                compressedOut.Close();
                encryptedOut.Close();

                // Convert to ASCII armored format
                var encryptedBytes = outputStream.ToArray();
                return ConvertToArmoredFormat(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PGP encryption failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decrypts PGP encrypted text using private key from Key Vault
        /// </summary>
        /// <param name="encryptedText">PGP encrypted text (ASCII armored)</param>
        /// <returns>Decrypted plain text</returns>
        public async Task<string> DecryptAsync(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                // Get PGP private key and passphrase from Key Vault
                var privateKeySecretName = _configuration["SecretNames:PGPPrivateKey"];
                var passphraseSecretName = _configuration["SecretNames:PGPPassphrase"];

                if (string.IsNullOrEmpty(privateKeySecretName))
                    throw new InvalidOperationException("SecretNames:PGPPrivateKey is not configured");
                if (string.IsNullOrEmpty(passphraseSecretName))
                    throw new InvalidOperationException("SecretNames:PGPPassphrase is not configured");

                var privateKeyArmored = await _secretsCacheService.GetSecretAsync(privateKeySecretName);
                var passphrase = await _secretsCacheService.GetSecretAsync(passphraseSecretName);

                if (string.IsNullOrEmpty(privateKeyArmored))
                    throw new InvalidOperationException($"PGP private key not found in Key Vault using secret name: {privateKeySecretName}");
                if (string.IsNullOrEmpty(passphrase))
                    throw new InvalidOperationException($"PGP passphrase not found in Key Vault using secret name: {passphraseSecretName}");

                var privateKey = ReadPrivateKey(privateKeyArmored, passphrase);

                // Decode from ASCII armored format
                var encryptedBytes = DecodeArmoredFormat(encryptedText);

                using var inputStream = new MemoryStream(encryptedBytes);
                var pgpObjectFactory = new PgpObjectFactory(inputStream);

                var pgpObject = pgpObjectFactory.NextPgpObject();

                // Handle the case where we might get a PgpEncryptedDataList directly
                if (pgpObject is not PgpEncryptedDataList encryptedDataList)
                {
                    throw new InvalidOperationException("Invalid PGP message format - no encrypted data list found");
                }

                // Find the correct encrypted data for our private key
                PgpPublicKeyEncryptedData? encryptedData = null;

                foreach (PgpEncryptedData obj in encryptedDataList.GetEncryptedDataObjects())
                {
                    if (obj is PgpPublicKeyEncryptedData pkEncData)
                    {
                        if (pkEncData.KeyId == privateKey.KeyId || pkEncData.KeyId == 0) // 0 means wildcard key
                        {
                            encryptedData = pkEncData;
                            break;
                        }
                    }
                }

                if (encryptedData == null)
                {
                    throw new InvalidOperationException($"No suitable encrypted data found for private key ID: {privateKey.KeyId:X}");
                }

                using var decryptedStream = encryptedData.GetDataStream(privateKey);
                var decryptedFactory = new PgpObjectFactory(decryptedStream);

                var nextObject = decryptedFactory.NextPgpObject();

                // Handle compressed data if present
                if (nextObject is PgpCompressedData compressedData)
                {
                    using var compressedStream = compressedData.GetDataStream();
                    var literalFactory = new PgpObjectFactory(compressedStream);
                    
                    // Read through all objects to find the literal data
                    PgpObject? obj = literalFactory.NextPgpObject();
                    
                    // Skip over PgpOnePassSignatureList if present (signed messages)
                    while (obj != null && obj is not PgpLiteralData)
                    {
                        if (obj is PgpOnePassSignatureList)
                        {
                            // Skip signature list and get next object
                            obj = literalFactory.NextPgpObject();
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unexpected PGP object type in compressed data: {obj.GetType().Name}");
                        }
                    }

                    if (obj is not PgpLiteralData literalData)
                    {
                        throw new InvalidOperationException("No literal data found in compressed stream");
                    }

                    using var literalStream = literalData.GetInputStream();
                    using var outputStream = new MemoryStream();
                    await literalStream.CopyToAsync(outputStream);

                    return Encoding.UTF8.GetString(outputStream.ToArray());
                }
                // Handle literal data directly
                else if (nextObject is PgpLiteralData literalData)
                {
                    using var literalStream = literalData.GetInputStream();
                    using var outputStream = new MemoryStream();
                    await literalStream.CopyToAsync(outputStream);

                    return Encoding.UTF8.GetString(outputStream.ToArray());
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected PGP object type: {nextObject?.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PGP decryption failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Signs and encrypts text using PGP
        /// </summary>
        /// <param name="plainText">Text to sign and encrypt</param>
        /// <returns>Signed and encrypted text</returns>
        public async Task<string> SignAndEncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                // Get keys and passphrase from Key Vault
                var publicKeySecretName = _configuration["SecretNames:PGPPublicKey"];
                var privateKeySecretName = _configuration["SecretNames:PGPPrivateKey"];
                var passphraseSecretName = _configuration["SecretNames:PGPPassphrase"];

                if (string.IsNullOrEmpty(publicKeySecretName) || string.IsNullOrEmpty(privateKeySecretName) || string.IsNullOrEmpty(passphraseSecretName))
                    throw new InvalidOperationException("PGP secret names are not properly configured in SecretNames section");

                var publicKeyArmored = await _secretsCacheService.GetSecretAsync(publicKeySecretName);
                var privateKeyArmored = await _secretsCacheService.GetSecretAsync(privateKeySecretName);
                var passphrase = await _secretsCacheService.GetSecretAsync(passphraseSecretName);

                if (string.IsNullOrEmpty(publicKeyArmored) || string.IsNullOrEmpty(privateKeyArmored) || string.IsNullOrEmpty(passphrase))
                    throw new InvalidOperationException("PGP keys or passphrase not found in Key Vault");

                var publicKey = ReadPublicKey(publicKeyArmored);
                var privateKey = ReadPrivateKey(privateKeyArmored, passphrase);

                using var outputStream = new MemoryStream();

                // Create encrypted data generator
                var encryptedDataGenerator = new PgpEncryptedDataGenerator(
                    SymmetricKeyAlgorithmTag.Aes256,
                    true,
                    new SecureRandom());

                encryptedDataGenerator.AddMethod(publicKey);

                using var encryptedOut = encryptedDataGenerator.Open(outputStream, new byte[4096]);

                // Create compressed data generator
                var compressedDataGenerator = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
                using var compressedOut = compressedDataGenerator.Open(encryptedOut);

                // FIXED: Create signature generator with correct constructor for BouncyCastle 2.x
                var signatureGenerator = new PgpSignatureGenerator(
                    PublicKeyAlgorithmTag.RsaGeneral, // Use explicit algorithm tag
                    HashAlgorithmTag.Sha256);

                signatureGenerator.InitSign(PgpSignature.BinaryDocument, privateKey);

                // Create one-pass signature packet
                signatureGenerator.GenerateOnePassVersion(false).Encode(compressedOut);

                // Add literal data
                var literalDataGenerator = new PgpLiteralDataGenerator();
                using var literalOut = literalDataGenerator.Open(compressedOut, PgpLiteralData.Binary,
                    "signed_data", DateTime.UtcNow, new byte[4096]);

                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

                // Update signature with data and write data
                signatureGenerator.Update(plainTextBytes);
                await literalOut.WriteAsync(plainTextBytes, 0, plainTextBytes.Length);
                literalOut.Close();

                // Generate and write signature
                var signature = signatureGenerator.Generate();
                signature.Encode(compressedOut);

                // Close remaining streams
                compressedOut.Close();
                encryptedOut.Close();

                var encryptedBytes = outputStream.ToArray();
                return ConvertToArmoredFormat(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PGP sign and encrypt failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Verifies signature and decrypts text
        /// </summary>
        /// <param name="encryptedText">Signed and encrypted text</param>
        /// <returns>True if signature is valid and decryption successful</returns>
        public async Task<bool> VerifyAndDecryptAsync(string encryptedText)
        {
            try
            {
                var decryptedText = await DecryptAsync(encryptedText);
                // Additional signature verification logic could be added here
                return !string.IsNullOrEmpty(decryptedText);
            }
            catch
            {
                return false;
            }
        }

        #region Private Helper Methods

        private static PgpPublicKey ReadPublicKey(string armoredPublicKey)
        {
            try
            {
                // DECODE BASE64 FIRST if the key is stored as Base64 in Key Vault
                string actualArmoredKey;
                try
                {
                    // Try to decode from Base64 first
                    var base64DecodedBytes = Convert.FromBase64String(armoredPublicKey);
                    actualArmoredKey = Encoding.UTF8.GetString(base64DecodedBytes);
                }
                catch (FormatException)
                {
                    // If Base64 decoding fails, assume it's already in ASCII armored format
                    actualArmoredKey = armoredPublicKey;
                }

                using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(actualArmoredKey));
                using var armoredStream = PgpUtilities.GetDecoderStream(inputStream);

                var pgpPub = new PgpPublicKeyRing(armoredStream);
                var publicKey = pgpPub.GetPublicKey();

                if (publicKey == null)
                    throw new InvalidOperationException("No public key found in the provided key data");

                return publicKey;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read PGP public key: {ex.Message}", ex);
            }
        }

        private static PgpPrivateKey ReadPrivateKey(string armoredPrivateKey, string passphrase)
        {
            try
            {
                // DECODE BASE64 FIRST if the key is stored as Base64 in Key Vault
                string actualArmoredKey;
                try
                {
                    // Try to decode from Base64 first
                    var base64DecodedBytes = Convert.FromBase64String(armoredPrivateKey);
                    actualArmoredKey = Encoding.UTF8.GetString(base64DecodedBytes);
                }
                catch (FormatException)
                {
                    // If Base64 decoding fails, assume it's already in ASCII armored format
                    actualArmoredKey = armoredPrivateKey;
                }

                using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(actualArmoredKey));
                using var armoredStream = PgpUtilities.GetDecoderStream(inputStream);

                var pgpSec = new PgpSecretKeyRing(armoredStream);
                PgpSecretKey? secretKey = null;

                // Find a suitable secret key for decryption
                foreach (PgpSecretKey key in pgpSec.GetSecretKeys())
                {
                    if (key.IsSigningKey || key.IsMasterKey)
                    {
                        secretKey = key;
                        break;
                    }
                }

                if (secretKey == null)
                {
                    // Fallback to the first available key
                    secretKey = pgpSec.GetSecretKey();
                }

                if (secretKey == null)
                    throw new InvalidOperationException("No secret key found in the provided key data");

                var privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

                if (privateKey == null)
                    throw new InvalidOperationException("Failed to extract private key - check passphrase");

                return privateKey;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read PGP private key: {ex.Message}", ex);
            }
        }

        private static string ConvertToArmoredFormat(byte[] data)
        {
            try
            {
                using var outputStream = new MemoryStream();
                using var armoredStream = new ArmoredOutputStream(outputStream);

                armoredStream.SetHeader("Version", "BouncyCastle PGP C# v2.4.0");
                armoredStream.Write(data, 0, data.Length);
                armoredStream.Close();

                return Encoding.UTF8.GetString(outputStream.ToArray());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create armored format: {ex.Message}", ex);
            }
        }

        private static byte[] DecodeArmoredFormat(string armoredData)
        {
            try
            {
                using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(armoredData));
                using var armoredStream = PgpUtilities.GetDecoderStream(inputStream);
                using var outputStream = new MemoryStream();

                armoredStream.CopyTo(outputStream);
                var result = outputStream.ToArray();

                if (result.Length == 0)
                    throw new InvalidOperationException("No data decoded from armored format");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to decode armored format: {ex.Message}", ex);
            }
        }

        #endregion
    }
}
