# Correspondence request PDFs

Each correspondence request gets a PDF rendered from its stored payload. The PDF is saved to a private Blob Storage
container and tracked in `dbo.CorrespondencePdfDocument`. The portal shows a PDF icon per record to download it, or
to generate it when it's missing.

## How it works

1. **Find new requests.** `CorrespondencePdfWorker` is a hosted service in this API. Every `PollIntervalSeconds` it
   inserts `Pending` rows for requests that meet all of these:
   - created within `BackfillDays`
   - at least `MinAgeSeconds` old, so the pipeline has assigned a DocId and first status
   - not tracked yet
2. **Claim a batch.** The worker claims up to `BatchSize` due rows: pending ones, failures whose backoff has passed,
   and rows whose lease expired. It uses `UPDATE … WITH (READPAST)` plus a lease, so several API instances never
   generate the same PDF.
3. **Generate each one.** For each claimed row, `CorrespondencePdfService`:
   1. **Loads** the record.
   2. **Decodes** the payload with `CorrespondencePayloadDecoder`. It handles AES-GCM (`PAYLOAD_ENCRYPTION_KEY`), PGP
      armour (`IEncryptionService`, the same key the pipeline encrypts with) and plain JSON.
   3. **Renders** the PDF with `CorrespondencePdfRenderer`.
   4. **Uploads** it to `{container}/{yyyy}/{MM}/{correspondenceId}.pdf`, with content type `application/pdf` and
      metadata holding ids only.
   5. **Records** the result:
      - **Success:** status `Generated`, with the size and SHA-256.
      - **Failure:** status `Failed` with a readable reason. It retries after 2^attempt minutes, up to `MaxAttempts`.
4. **On demand:** `POST /api/portal/correspondence/{id}/pdf` runs step 3 immediately. Use it for old records or to
   regenerate.

**PDF contents**
- **Sections:** a title band with the document name, then:
  - **Request:** ids, claim, policy (masked to the last 4 characters), customer, delivery method, created date, and the
    status when generated.
  - **Recipient:** name, address and flags.
  - **Journey delivery** and **Composition**, when present.
  - **All payload fields:** every field as an appendix. Values over 600 characters are truncated.
- **Footer:** "contains personal data" with the page number on every page.

**How it's built**
- `SimplePdfDocument` writes PDF 1.4 using only the standard Helvetica and Courier fonts. There's no third-party
  library, no font files and no licence to manage, and it works on Windows or Linux App Service.
- Text is WinAnsi: Latin-1 accents work, but other scripts render as `?`. Swap in a library such as PDFsharp/MigraDoc
  if you need logos, other scripts or richer layout.

## Setup

1. Run `Build/DBScripts/Pdf/001_CorrespondencePdf_Schema.sql`.
2. Re-run `Build/DBScripts/Auth/002_PortalAuth_ReferenceData.sql`. It adds `correspondence.pdf.generate` for
   Administrator and Operator.
3. Configure the `PdfGeneration` section:

   | Setting | Default | Notes |
   |---|---|---|
   | `Enabled` | false (true in Development) | Runs the background worker. On-demand generation works either way once storage is set. |
   | `Blob:ConnectionString` | — | Development: `UseDevelopmentStorage=true` (Azurite). |
   | `Blob:ServiceUri` | — | Azure: `https://<account>.blob.core.windows.net`, with managed identity. Grant the app's identity **Storage Blob Data Contributor** on the account or container. |
   | `Blob:ManagedIdentityClientId` | — | Only for a user-assigned identity. |
   | `Blob:ContainerName` | `correspondence-pdfs` | Created private if missing. |
   | `PollIntervalSeconds` / `BatchSize` | 30 / 10 | |
   | `MaxAttempts` / `LeaseSeconds` | 5 / 300 | |
   | `MinAgeSeconds` | 60 | |
   | `BackfillDays` | 1 (2 in Development) | Raise it temporarily to backfill history, e.g. 365. |
   | `MaskPolicyNumber` | true | |

   Without any Blob setting, the PDF endpoints return 503 and the portal hides the PDF icon.
4. Locally, start Azurite (`azurite --silent --location %TEMP%\azurite`) before the API.

## Access (RBAC + ABAC)

| Endpoint | Access |
|---|---|
| `POST /api/portal/correspondence/pdf/status` `{ correspondenceIds }` | `correspondence.read`; the failure reason is only shown to users with `correspondence.pdf.generate` |
| `GET /api/portal/correspondence/{id}/pdf` | `correspondence.payload.read`, the PII-clearance policy and the user's data scope. The PDF holds the same personal data as the payload. Logged as `PdfDownloaded`. |
| `POST /api/portal/correspondence/{id}/pdf` | `correspondence.pdf.generate` and data scope. Logged as `PdfGenerated`. |

Deleting records through the portal also deletes their PDF rows (cascade) and blobs.

## Tests

`CustomerCorrespondence.TestProjects/PortalAuthTests`:

- **`CorrespondencePdfTests`:** opens the output with PdfPig to check the content, masking, wrapping, multi-page
  footers and escaping.
- **`CorrespondencePdfWorkerIntegrationTests`:** runs the real worker against the throwaway database and Azurite. It
  needs `ECCO_AUTH_TEST_CONNECTION` and `ECCO_PDF_TEST_BLOB=UseDevelopmentStorage=true`. It checks:
  - plain and PGP payloads are generated and stored
  - a corrupt payload is backed off
  - the backfill window is respected
  - on-demand generation works
