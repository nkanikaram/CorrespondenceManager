using System.Security.Cryptography;

namespace ProcessRequestEndPoint.Auth
{
    public sealed record PasswordHash(byte[] Hash, byte[] Salt, int Iterations);

    /// <summary>PBKDF2-HMAC-SHA256 password hashing with a per-user random salt.</summary>
    public static class PasswordHasher
    {
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        public static PasswordHash Hash(string password, int iterations)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
            return new PasswordHash(hash, salt, iterations);
        }

        public static bool Verify(string password, byte[] expectedHash, byte[] salt, int iterations)
        {
            if (string.IsNullOrEmpty(password) || expectedHash.Length == 0) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
        }

        /// <summary>
        /// Burns the same work as a real verification so unknown user names take as long as wrong
        /// passwords (no user-enumeration timing signal).
        /// </summary>
        public static void VerifyDummy(string password, int iterations)
        {
            Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, new byte[SaltBytes], iterations, HashAlgorithmName.SHA256, HashBytes);
        }

        /// <summary>Returns a list of problems; empty when the password is acceptable.</summary>
        public static IReadOnlyList<string> Validate(string? password, string userName, int minLength)
        {
            var problems = new List<string>();
            if (string.IsNullOrEmpty(password))
            {
                problems.Add("Password is required.");
                return problems;
            }
            if (password.Length < minLength) problems.Add($"Use at least {minLength} characters.");
            if (password.Length > 128) problems.Add("Use at most 128 characters.");
            var classes = new[]
            {
                password.Any(char.IsUpper), password.Any(char.IsLower), password.Any(char.IsDigit),
                password.Any(c => !char.IsLetterOrDigit(c))
            }.Count(x => x);
            if (classes < 3) problems.Add("Mix at least three of: upper case, lower case, digits, symbols.");
            if (!string.IsNullOrEmpty(userName) && password.Contains(userName, StringComparison.OrdinalIgnoreCase))
                problems.Add("Don't include your user name.");
            return problems;
        }
    }
}
