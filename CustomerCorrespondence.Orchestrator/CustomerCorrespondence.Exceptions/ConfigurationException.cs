using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when configuration is missing, invalid, or cannot be loaded.
    /// Used for appsettings.json issues, environment variables, and configuration validation.
    /// </summary>
    public class ConfigurationException : CorrespondenceException
    {
        /// <summary>
        /// The configuration key that is missing or invalid.
        /// </summary>
        public string? ConfigurationKey { get; set; }

        /// <summary>
        /// The configuration section involved (e.g., "SecretNames", "KeyVault").
        /// </summary>
        public string? Section { get; set; }

        public ConfigurationException(string message, string errorCode)
            : base(message, errorCode)
        {
        }

        public ConfigurationException(string message, string errorCode, string configurationKey)
            : base(message, errorCode)
        {
            ConfigurationKey = configurationKey;
        }

        public ConfigurationException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }
    }
}