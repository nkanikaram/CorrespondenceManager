using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ProcessRequestEndPoint.Pdf
{
    /// <summary>
    /// Private Blob Storage container for correspondence PDFs. Uses a connection string when one is set
    /// (Azurite in Development), otherwise the account URI with managed identity (DefaultAzureCredential),
    /// matching how the API reaches Key Vault and SQL. The container is created private if missing.
    /// </summary>
    public sealed class PdfBlobStorage
    {
        private readonly BlobContainerClient _container;
        private readonly SemaphoreSlim _ensureLock = new(1, 1);
        private bool _ensured;

        public PdfBlobStorage(IOptions<PdfGenerationOptions> options)
        {
            var blob = options.Value.Blob;
            BlobServiceClient service;
            if (!string.IsNullOrWhiteSpace(blob.ConnectionString))
            {
                service = new BlobServiceClient(blob.ConnectionString);
            }
            else if (!string.IsNullOrWhiteSpace(blob.ServiceUri))
            {
                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = string.IsNullOrWhiteSpace(blob.ManagedIdentityClientId) ? null : blob.ManagedIdentityClientId
                });
                service = new BlobServiceClient(new Uri(blob.ServiceUri), credential);
            }
            else
            {
                throw new InvalidOperationException("PdfGeneration:Blob needs a ConnectionString or a ServiceUri.");
            }
            _container = service.GetBlobContainerClient(blob.ContainerName);
        }

        public string ContainerName => _container.Name;

        private async Task EnsureContainerAsync(CancellationToken ct)
        {
            if (_ensured) return;
            await _ensureLock.WaitAsync(ct);
            try
            {
                if (_ensured) return;
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                _ensured = true;
            }
            finally
            {
                _ensureLock.Release();
            }
        }

        /// <summary>Uploads (overwriting) a PDF. Metadata carries identifiers only — never personal data.</summary>
        public async Task UploadAsync(string blobName, byte[] content, IDictionary<string, string> metadata, CancellationToken ct)
        {
            await EnsureContainerAsync(ct);
            var blob = _container.GetBlobClient(blobName);
            using var stream = new MemoryStream(content, writable: false);
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
                Metadata = metadata
            }, ct);
        }

        public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct)
        {
            var blob = _container.GetBlobClient(blobName);
            try
            {
                var download = await blob.DownloadStreamingAsync(cancellationToken: ct);
                return download.Value.Content;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteIfExistsAsync(string blobName, CancellationToken ct)
        {
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
    }
}
