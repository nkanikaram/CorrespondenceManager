namespace ProcessRequestEndPoint.Pdf
{
    /// <summary>"PdfGeneration" configuration section.</summary>
    public sealed class PdfGenerationOptions
    {
        public const string SectionName = "PdfGeneration";

        /// <summary>Runs the background worker that renders PDFs for new requests. On-demand generation works either way.</summary>
        public bool Enabled { get; set; }

        public int PollIntervalSeconds { get; set; } = 30;
        public int BatchSize { get; set; } = 10;
        public int MaxAttempts { get; set; } = 5;
        public int LeaseSeconds { get; set; } = 300;

        /// <summary>Requests younger than this are left alone, so the pipeline can finish (DocId, first status).</summary>
        public int MinAgeSeconds { get; set; } = 60;

        /// <summary>
        /// Only requests created within this many days get a PDF automatically. Older ones can be generated
        /// on demand from the portal. Raise it once to backfill history.
        /// </summary>
        public int BackfillDays { get; set; } = 1;

        /// <summary>Show only the last 4 characters of policy numbers in the PDF.</summary>
        public bool MaskPolicyNumber { get; set; } = true;

        public BlobOptions Blob { get; set; } = new();

        public sealed class BlobOptions
        {
            /// <summary>Development / Azurite: "UseDevelopmentStorage=true". Leave empty in Azure and set ServiceUri.</summary>
            public string? ConnectionString { get; set; }

            /// <summary>e.g. https://stecco.blob.core.windows.net — authenticated with managed identity (DefaultAzureCredential).</summary>
            public string? ServiceUri { get; set; }

            /// <summary>User-assigned managed identity client id, when the app uses one.</summary>
            public string? ManagedIdentityClientId { get; set; }

            public string ContainerName { get; set; } = "correspondence-pdfs";
        }

        public bool StorageConfigured =>
            !string.IsNullOrWhiteSpace(Blob.ConnectionString) || !string.IsNullOrWhiteSpace(Blob.ServiceUri);
    }
}
