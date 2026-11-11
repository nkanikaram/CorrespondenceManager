using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CustomerCorrespondence.Domain.Entities
{
    /// <summary>
    /// Model Context Protocol (MCP) container that carries metadata about the model
    /// used to produce or interpret a given SequentialProcess payload.
    /// This object is intentionally lightweight and serializable so it can travel
    /// with messages and HTTP requests.
    /// </summary>
    public class ModelContext
    {
        /// <summary>
        /// Logical name of the model (e.g. "CorrespondenceRequestModel").
        /// </summary>
        public string? ModelName { get; set; }

        /// <summary>
        /// Model version (semantic version string is recommended).
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Optional URI to the model schema or documentation.
        /// </summary>
        public string? SchemaUri { get; set; }

        /// <summary>
        /// Origin of the model payload (system / service name).
        /// </summary>
        public string? Origin { get; set; }

        /// <summary>
        /// When the model context was created (UTC).
        /// </summary>
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Additional key/value metadata.
        /// </summary>
        public Dictionary<string, string>? AdditionalMetadata { get; set; }

        public void TrimAllStringFields()
        {
            ModelName = ModelName?.Trim();
            Version = Version?.Trim();
            SchemaUri = SchemaUri?.Trim();
            Origin = Origin?.Trim();

            if (AdditionalMetadata != null)
            {
                var keys = new List<string>(AdditionalMetadata.Keys);
                foreach (var k in keys)
                {
                    AdditionalMetadata[k] = AdditionalMetadata[k]?.Trim() ?? string.Empty;
                }
            }
        }

        public void ValidateNoSpecialCharacters()
        {
            // Reuse existing validation helper from ProcessRequestHelper for consistency.
            ProcessRequestHelper.CheckField(ModelName, nameof(ModelName));
            ProcessRequestHelper.CheckField(Version, nameof(Version));
            ProcessRequestHelper.CheckField(SchemaUri, nameof(SchemaUri));
            ProcessRequestHelper.CheckField(Origin, nameof(Origin));

            if (AdditionalMetadata != null)
            {
                foreach (var kvp in AdditionalMetadata)
                {
                    ProcessRequestHelper.CheckField(kvp.Key, $"AdditionalMetadata.Key");
                    ProcessRequestHelper.CheckField(kvp.Value, $"AdditionalMetadata[{kvp.Key}]");
                }
            }
        }
    }
}
