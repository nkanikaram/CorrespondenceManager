using System.Security.Cryptography;
using System.Text;
using CustomerCorrespondence.Application.Interfaces;

namespace ProcessRequestEndPoint.Pdf
{
    /// <summary>
    /// Turns a stored CorrespondenceRequest.RequestPayload back into JSON text. Handles the same formats as
    /// CorrespondenceSearchController: AES-GCM (nonce | tag | ciphertext, PAYLOAD_ENCRYPTION_KEY), ASCII-armored
    /// PGP written by AddCorrespondenceDBHandler (decrypted with IEncryptionService), or plain UTF-8.
    /// </summary>
    public sealed class CorrespondencePayloadDecoder
    {
        private const string PgpHeader = "-----BEGIN PGP MESSAGE-----";
        private readonly IEncryptionService _encryption;
        private readonly byte[]? _aesKey;

        public CorrespondencePayloadDecoder(IEncryptionService encryption, IConfiguration configuration)
        {
            _encryption = encryption;
            var key = configuration["PAYLOAD_ENCRYPTION_KEY"]?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    var bytes = Convert.FromBase64String(key);
                    if (bytes.Length == 32) _aesKey = bytes;
                }
                catch (FormatException)
                {
                    // Hex keys and bad values: CorrespondenceSearchController validates and reports those.
                }
            }
        }

        public async Task<string> DecodeAsync(byte[] stored)
        {
            if (_aesKey != null && TryDecryptAesGcm(stored, _aesKey, out var aesPlain)) return aesPlain;

            var text = Encoding.UTF8.GetString(stored).Trim();
            if (text.StartsWith(PgpHeader, StringComparison.Ordinal))
                return await _encryption.DecryptAsync(text); // throws when the key doesn't match
            return text;
        }

        private static bool TryDecryptAesGcm(byte[] stored, byte[] key, out string plaintext)
        {
            plaintext = string.Empty;
            if (stored.Length < 12 + 16) return false;
            try
            {
                var nonce = stored.AsSpan(0, 12);
                var tag = stored.AsSpan(12, 16);
                var cipher = stored.AsSpan(28);
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                plaintext = Encoding.UTF8.GetString(plain);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
