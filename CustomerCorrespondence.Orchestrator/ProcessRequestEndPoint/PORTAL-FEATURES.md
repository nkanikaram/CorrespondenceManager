# Correspondence Manager portal: SSO, record details, recycle bin, letters, alerts

These are the five features on top of portal sign-in ([Auth/README.md](Auth/README.md)) and PDFs
([Pdf/README.md](Pdf/README.md)). They are all registered by one line in `Program.cs`: `AddPortalFeatures(builder)`,
with `MapPortalFeatures(app)` before `app.Run()`.

## Database

Run these scripts once per database, after `Auth/001–003` and `Pdf/001`. All of them are idempotent.

| Script | Adds |
|---|---|
| `Build/DBScripts/Auth/004_PortalAuth_EntraSso.sql` | `AppUser.ExternalProvider/ExternalObjectId/ExternalTenantId` (password now optional), `Source` (`Manual`/`Group`) on `UserRole` and `UserAttribute`, `auth.ExternalGroupMapping`, `AuthAuditLog.ResourceId` |
| `Build/DBScripts/Portal/001_RecycleBin.sql` | `dbo.CorrespondenceRecycleBin`, `usp_RecycleCorrespondence`, `usp_RestoreCorrespondence`, `fn_RecycleBinColumns` |
| `Build/DBScripts/Portal/002_LetterTemplates.sql` | `dbo.LetterTemplate`, plus `TemplateId` and `TemplateVersion` on the PDF table. Seeds *Welcome Kit* and *Claim Update* as **drafts** |
| `Build/DBScripts/Portal/003_Alerts.sql` | `dbo.PortalAlert`, `dbo.PortalAlertMetric` |
| `Build/DBScripts/Auth/005_PortalAuth_PasswordReset.sql` | `auth.PasswordResetToken` (hashes of reset links) |
| `Build/DBScripts/Portal/004_Sla.sql` | `dbo.SlaSetting`, `dbo.SlaTarget` (default: 5 business days), permission `sla.manage` (Administrator) |
| `Build/DBScripts/Auth/006_PortalAuth_UserPreference.sql` | `auth.UserPreference` (display preferences saved with the account) |

`002_PortalAuth_ReferenceData.sql` also adds these permissions:

- `correspondence.recycle`
- `letters.templates.manage`
- `alerts.read`

`alerts.read` is granted to Operator and Analyst. Administrator has every permission.

## 1. Microsoft Entra ID sign-in (`Auth/EntraTokenValidator.cs`, `AuthController`)

- **`GET /api/auth/config`** (anonymous) reports which sign-in methods are on, plus the public client id and authority.
- **`POST /api/auth/entra { idToken }`** (anonymous) checks the token:
  - the signature, against the tenant's OpenID keys (cached by `ConfigurationManager`)
  - the issuer and `tid`
  - the audience (client id)
  - `iat` no older than `MaxTokenAgeMinutes`
  - `amr` contains `mfa` (`RequireMfa`)

  If all pass, it returns the normal portal token.
- **Accounts** are found by `(Entra, oid)` and created on first sign-in (`AutoProvision`).
  - They are never linked by user name, because `preferred_username` can change.
  - A name clash with a local account returns 409.
- **Group grants:**
  - Group and app-role grants come from `auth.ExternalGroupMapping` and are re-synced at every sign-in, stored with
    `Source = 'Group'`.
  - A change rotates the security stamp, which ends the user's sessions.
  - Admin edits only touch `Source = 'Manual'` grants.
- **Local accounts:**
  - Local sign-in can be switched off (`LocalLogin:Enabled`) or limited to break-glass accounts
    (`LocalLogin:AllowedUserNames`).
  - Microsoft accounts can't use the local form, change a password, or have one reset.
- **Group mappings:** `GET/POST/DELETE /api/admin/group-mappings` (`admin.users.manage`).

App registration steps are in the portal's README, under "Microsoft sign-in". Configuration:

```json
"PortalAuth": {
  "Entra": { "Enabled": true, "TenantId": "<guid>", "ClientId": "<guid>", "RequireMfa": true, "MaxTokenAgeMinutes": 10, "AutoProvision": true },
  "LocalLogin": { "Enabled": true, "AllowedUserNames": [ "admin" ] }
}
```

## 2. Record details (`Records/CorrespondenceDetails.cs`, `CorrespondenceRecordsController`)

**`GET /api/portal/correspondence/{id}/details`** needs `correspondence.read` and data scope. It returns:

- the record
- the full status history, oldest first
- the payload, under the same rules as search: `correspondence.payload.read` and the PII policy, otherwise
  `payloadHiddenReason`
- the PDF state
- the audit entries for that record (`AuthAuditLog.ResourceId`)

## 3. Recycle bin and retention (`Records/RecycleBin.cs`)

**Deleting** (`POST /api/portal/correspondence/delete`) moves each record, in one transaction, into
`dbo.CorrespondenceRecycleBin`:

- the request row, its status history and its PDF row, saved as JSON snapshots
- `PurgeAfterUtc = now + RecycleBinDays`

The response adds `recycleBinDays`.

**Restoring** (`POST /api/portal/recycle-bin/{id}/restore`) re-inserts the rows exactly, including identity values. It
refuses if the record already exists again.

**Other endpoints:**

- `GET /api/portal/recycle-bin?search=` lists the bin.
- `DELETE /api/portal/recycle-bin/{id}` purges one record now and also deletes its PDF blob.

All of these need `correspondence.recycle` and the user's data scope.

**`RetentionWorker`** runs every `IntervalHours`:

- It purges bin entries past `PurgeAfterUtc`, including their blobs.
- If `RecordRetentionDays` is set, it also moves records older than that into the bin.
- Expiry is **off by default** (`null`). Set it once the business agrees a retention period.

**Blob Storage:** for the `correspondence-pdfs` container, add an Azure lifecycle rule as a safety net, e.g. move to
Cool after 90 days. Do not add a delete rule shorter than your retention period: the worker already deletes blobs of
purged records.

```json
"Retention": { "Enabled": true, "RecycleBinDays": 30, "IntervalHours": 6, "RecordRetentionDays": null }
```

## 4. Letter templates (`Letters/LetterTemplates.cs`, `LettersController`)

**How templates are used**

- A template belongs to a `DocumentType`, which matches the payload's `DocumentDescription`.
- It has a version and a status: `Draft`, `Published` or `Retired`. Only one version per type is published.
- `CorrespondencePdfService` renders the published template of the record's type as a branded letter:
  - letterhead from the `Letters` section
  - recipient address block
  - subject, body and signature
- Types without a published template get the data sheet as before.
- Each PDF row records `TemplateId` and `TemplateVersion`.

**Placeholders**

- Supported: `{{RecipientName}}`, `{{RecipientFirstName}}`, `{{RecipientLastName}}`, `{{ClaimNumber}}`,
  `{{PolicyNumber}}` (masked), `{{CustomerId}}`, `{{DocId}}`, `{{DocumentType}}`, `{{RequestingSystem}}`,
  `{{DeliveryMethod}}`, `{{Today}}`, `{{CompanyName}}`, `{{CompanyPhone}}`, `{{CompanyWebsite}}`, and
  `{{payload.Any.Path}}`.
- Missing values are blank in real letters and shown as `[Name]` in previews.

**Endpoints**

| Endpoint | Access |
|---|---|
| `GET templates`, `GET placeholders`, `POST templates`, `PUT templates/{id}` (drafts only, else 409), `POST templates/{id}/publish` (unknown placeholders → 400; retires the previous version), `POST templates/{id}/retire` | `letters.templates.manage` |
| `POST preview { templateId? \| template?, correspondenceId? \| payload? }` | `letters.templates.manage` or `correspondence.create` |

All endpoints are under `/api/portal/letters`.

Preview returns a PDF that is not stored, with these headers:

- `X-Letter-Template`
- `X-Letter-Missing`

Previewing a stored record needs payload access to it.

The seeded templates are **drafts**: review the wording, then publish them.

```json
"Letters": { "CompanyName": "…", "AddressLines": [ "…" ], "Phone": "…", "Website": "…", "BrandColor": "#0066CC" }
```

## 5. Alerts and live updates (`Alerts/PortalAlerts.cs`, `Realtime/PortalRealtime.cs`)

`AlertWorker` runs every `IntervalMinutes`. `sp_getapplock` makes sure only one API instance evaluates at a time.

**Rules**

| Rule | Trips when |
|---|---|
| `FailedRate` | Failed share of requests created in the last `WindowHours` is ≥ `Multiplier` × the average of the last `BaselineChecks` measurements **and** ≥ `MinIncrease` higher (or ≥ `AbsoluteThreshold`), with at least `MinSample` requests |
| `ProcessingBacklog` | ≥ `Threshold` requests stuck in an in-progress status for more than `OlderThanHours` |
| `PdfFailures` | ≥ `Threshold` PDFs failed every attempt |

**When a rule trips**

- An alert is written to `dbo.PortalAlert`.
- It is pushed to the portal.
- It is sent to Teams (a Workflows webhook URL; posts an Adaptive Card) and/or by email (SMTP), when configured.
- A rule doesn't alert again within `CooldownHours` while its alert is open.

**Endpoints**

- `GET /api/portal/alerts?open=&top=`
- `POST /api/portal/alerts/{id}/acknowledge`
- `GET /api/portal/alerts/rules` (no secrets)

All need `alerts.read`.

**Live updates**

- The SignalR hub `/hubs/portal` requires a portal user; the token is sent as `access_token`.
- It pushes these events:
  - `recordsChanged` (`ChangeFeedWorker`, every 15 s)
  - `pdfStatusChanged`
  - `alertRaised` (only to `alerts.read` users)
- Clients without WebSockets poll `GET /api/portal/changes?since=`, which needs `correspondence.read`.

```json
"Alerts": {
  "Enabled": true, "IntervalMinutes": 10, "CooldownHours": 6, "PortalUrl": "https://portal.example.com/",
  "Teams": { "WebhookUrl": "<Workflows 'Post to a channel when a webhook request is received' URL>" },
  "Email": { "Host": "smtp.example.com", "Port": 587, "EnableSsl": true, "UserName": "…", "Password": "…", "From": "alerts@example.com", "To": [ "ops@example.com" ] }
}
```

Keep the webhook URL and the SMTP password in Key Vault or user secrets, not in `appsettings.json`.

**Scale-out:** with more than one API instance, add the Azure SignalR Service, or a Redis backplane, so that events
reach clients connected to other instances. Alert evaluation is already single-instance via the app lock.

## 6. Forgot password (`Auth/PasswordResetService.cs`, `AuthController`)

Local accounts can reset a forgotten password themselves. Microsoft sign-in accounts reset theirs with Microsoft (Entra self-service password reset).

1. **Request:** "Forgot password?" on the sign-in page → `POST api/auth/forgot-password {userNameOrEmail}`.
   - It matches the user name first, otherwise the one account with that email address. An email shared by several accounts matches nothing.
   - The answer is always the same `202`, so it can't be used to find out which accounts exist. The email is sent in the background.
   - Nothing is sent to inactive or Microsoft sign-in accounts, accounts with no email, accounts outside `LocalLogin:AllowedUserNames`, or after `MaxRequestsPerHour` emails to one account (or `MaxRequestsPerIpPerHour` requests from one address). The audit log (`PasswordResetIgnored`) records the real reason.
2. **Email:** a link to `<PortalUrl>/#!/reset-password?token=…`.
   - The token is 32 random bytes. Only its SHA-256 hash is stored.
   - It sits after the `#`, so browsers never send it to a server or in a `Referer` header, and the portal removes it from the address bar as soon as it opens.
3. **Reset:** `POST api/auth/reset-password/check {token}`, then `POST api/auth/reset-password {token, newPassword}`.
   - The link works once and expires after `LinkLifetimeMinutes`. It also stops working when a newer link is requested, or when the account's security stamp changes (any password change, admin reset, role change or deactivation).
   - The new password must meet the usual policy. The reset unlocks the account and ends every session, then emails a "your password was changed" notice.

**Email delivery** uses `PortalAuth:PasswordReset:Email` (same fields as `Alerts:Email`), or `Alerts:Email` when that isn't set. Set `PortalAuth:PasswordReset:PortalUrl` (or `Alerts:PortalUrl`). The link is never built from the request's Host header. With no SMTP server and no portal URL, "Forgot password?" is hidden. In Development with no SMTP server, the link is written to the API log.

## 7. Time lapse and SLA (`Sla/Sla.cs`, `SlaController`)

Every search result carries `sla`: `{ state, targetDays, businessDays, dueUtc, stoppedUtc, elapsedSeconds, percentUsed }`. The grid shows it in the **Age / SLA** column, and Excel exports include `AgeHours`, `SLA` and `SLADue`.

- **Start and stop:** the clock starts at `RequestCreationTS`. It stops at the latest status's `StatusUpdateTS` when that status is one of the *finishing statuses*. Failed and Error don't stop it, because those requests still need action.
- **States:**
  - Open requests are `OnTrack`, then `AtRisk` (at `AtRiskPercent` of the SLA), then `Breached` (past the due time).
  - Finished requests are `Met` or `Missed`.
- **Target:** the customer's (requesting domain's) own target if it has one, otherwise the default. Days are business days (Monday to Friday, UTC) or calendar days. Public holidays aren't excluded yet.
- **Settings:** set under **Administration → SLA** (permission `sla.manage`) through `GET` and `PUT api/portal/sla`. Changes are validated and audited (`SlaChanged`). They're cached for 30 seconds, so a save shows up on the next search within that time.
- **Before the script runs:** with no SLA tables (script not run yet), searches work as before and carry no `sla`.
- **Filtering:** the grid filter field `sla` takes `eq` or `neq` with one of `Breached`, `At risk`, `On track` or `Finished` (Met or Missed). It runs in SQL across every matching row.
  - `SlaCalculator.Cutoffs` turns each target into creation-time cutoffs, so SQL only compares `RequestCreationTS`; no business-day maths runs in the database.
  - The filter and the column use the same settings and the same "now", so they always agree. A unit test checks this over thousands of random times.
  - The portal offers a **Breached only** toggle above the grid, plus the SLA column's filter row and column menu. The link `#!/?sla=breached` opens the grid with Breached only switched on.
- **Alerts:** the `SlaBreaches` rule (`Alerts:SlaBreaches`: `Enabled`, `Threshold` 1, `CriticalThreshold` 25) counts open requests whose SLA ran out since the previous check.
  - It counts new breaches only, so an old backlog doesn't alert at every check. The message gives the breached and at-risk totals.
  - The bell, the Alerts tab, and the Teams and email links all open the grid with Breached only switched on.
- **Not yet supported:** sorting by SLA, and public holidays.

## 8. Account menu (`Controllers/AccountController.cs`)

Everything in the menu concerns the caller only, and any signed-in user can use it (`[RequirePortalUser]`).

| Endpoint | What it does |
|---|---|
| `GET api/account` | Data scope (`scope.customers`, `scope.deliveryMethods`; `null` = all), the previous sign-in (time, IP, password or Microsoft) and failed attempts since it, saved preferences, support links, API version and environment |
| `PUT api/account/preferences` | Saves `{ theme: light\|dark\|system, timeZone: local\|utc, pageSize: 10\|25\|50\|100, density: comfortable\|compact, font: default\|calibri\|arial\|verdana\|georgia\|mono, textSize: sm\|md\|lg\|xl, headerColor: #rrggbb, textColor: #rrggbb\|contrast }`. Anything unset uses the portal default; invalid values return 400. The appearance values (font to textColor) apply to every screen: the top bar, dialog and popup headers and the assistant's title bar take the header colour, and grid column headers take a tint of it |
| `POST api/account/sign-out-others` | Rotates the security stamp, so every other token stops working, and returns a fresh token for this browser. Audited as `SignedOutOtherSessions` |

- **Other browsers sign out at once:** stateless tokens only fail at their next request, so an idle browser would otherwise look signed in.
  - Whenever a user's sessions end (sign out other sessions, a password change or reset, or an admin editing or resetting the account), the hub sends `sessionsEnded` to that user's connections.
  - Each browser re-checks its token 2 seconds later. The browser that started it has adopted its new token by then, so it stays signed in.
  - Browsers also re-check when the tab becomes visible again, which covers polling clients and missed pushes.
- **Previous sign-in:** the second-latest `LoginSucceeded` in the audit log, since the latest is normally this session. The failed attempts are the `LoginFailed` and `LockedOut` events after it.
- **Support links:** `PortalAuth:Support` holds `Email` (for "Report a problem", which opens a pre-filled email with the version, environment, page and browser), `GuideUrl` and `EnvironmentName` (defaults to the hosting environment's name). A link stays hidden while its setting is unset.
- **Time zone:** the API's timestamps are UTC. SQL `datetime` values serialise without a zone, so the portal reads a timestamp with no zone as UTC, then shows it in the user's time zone (the default) or in UTC.
- **Before the script runs:** with no `auth.UserPreference` table, `GET` returns empty preferences and `PUT` returns 503. The portal still applies the preferences in the browser.
- **Keyboard shortcuts** (portal only): `/` search, `b` Breached only, `[` and `]` previous and next page, `g c` and `g a` to go to Correspondence and Administration, `?` the list, and `Esc` to close. They don't run while the user is typing.

## 9. Search with several terms (`CorrespondenceQueryBuilder.AppendFreeText`)

- **Several terms:** `POST api/correspondence/search` takes `freeTerms` (a list of up to 100 strings) as well as `freeText`. A row matches when any term is in its DocId, claim number, status, customer, customer id, VF response id or correspondence id; a GUID term also matches the id exactly. More than 100 terms returns 400. Terms combine (AND) with `filter` and the other fields. `freeText` is one term, as before. LIKE wildcards (`%`, `_`, `[`) in a term are now matched literally.
- **Portal search box:** commas, semicolons, tabs and new lines separate terms. A pasted column from Excel becomes a comma list, and IDs separated only by spaces are split too. `field:value` terms become grid column filters, so they show as removable chips and in the column menus:
  - Fields: `status:`, `customer:`, `delivery:`, `doc:`, `claim:`, `id:`, `sla:` (breached, at-risk, on-track, finished).
  - Created date: `from:` and `to:` (inclusive), `after:`, `before:` and `on:`. Dates can be `2026-11-01`, `11/01/2026`, `today`, `yesterday` or `7d`.
  - `-status:Completed` leaves rows out, and `status:Failed,Processing` matches either value.
  - Quote values that contain spaces: `customer:"Claims System"`.
  - The `?` button in the box lists the syntax.

## Tests

`CustomerCorrespondence.TestProjects/PortalAuthTests/PortalFeaturesTests.cs` covers:

- **Alert rules:** unit tests.
- **Letter rendering:** unit tests.
- **SSO over HTTP**, with a locally signed test key:
  - tokens without MFA, from the wrong tenant, with the wrong audience, too old, or tampered are rejected
  - provisioning
  - group sync removing a role and ending the session
- **Records and recycle bin:** details, recycle, restore, purge.
- **Letters:** publish validation, versions, preview.
- **Alerts:** raise, the Teams card, cooldown, acknowledge.
- **The SignalR hub:** delivers events; anonymous connections are rejected.
- **The change feed.**

How to run them is described in [Auth/README.md](Auth/README.md#tests).
