# ECCO Correspondence Manager (AngularJS 1.8)

AngularJS port of the React app in `ECCO-CommunicationManager/CustomerCorrespondence.UI`. It uses the same backend and
has the same features, plus sign-in with role- and attribute-based access control. There's no build step: plain
AngularJS components served as static files.

## Run

```bash
npm install
npm start          # http://localhost:8080 — proxies /api and /hubs (WebSockets too) to https://localhost:7228 (the .NET API)
npm run start:mock # http://localhost:8081 — app + in-memory mock API, no backend needed
npm test           # unit tests (plain Node, no browser)
```

**Signing in.** With the mock, and with a database seeded by `003_PortalAuth_DevUsers.sql`, these accounts exist. All
use the password `EccoDev!2026`.

| User | Role | Data access |
|---|---|---|
| `admin` | Administrator | all customers, PII clearance |
| `operator` | Operator | Life Services + Claims System, PII clearance, export limit 500 |
| `analyst` | Analyst | all customers, no PII clearance, export limit 100 |
| `viewer` | Viewer | Annuity Correspondence, Email only |

The mock also has **Sign in with Microsoft**. It opens an account picker instead of Microsoft:

| Account | What happens |
|---|---|
| Alex Rivera | groups *CC Operators* + *Life Services team* → Operator, Life Services records, PII clearance |
| Sam Patel | group *CC Analysts* → Analyst, all customers |
| Noa Guest | in no mapped group → refused |
| Max NoMfa | signed in without MFA → refused |

The local form is behind "Use a local account instead".

**Backend setup for `npm start`.**

1. Run the scripts in `Build/DBScripts/Auth` against the API's database:
   - `001` creates the schema.
   - `002` adds permissions, roles and policies.
   - `003` adds the development users. Development only.
   - `004` adds Microsoft sign-in (external accounts, group mappings).
2. Run the scripts in `Build/DBScripts/Pdf` and `Build/DBScripts/Portal` (PDFs, recycle bin, letter templates, alerts).
3. Restart the API.

See [Access control](#access-control-rbac--abac).

`npm start` runs [tools/dev-server.js](tools/dev-server.js). It serves the portal's files, uncached, and forwards `/api/*` and `/hubs/*` to
`https://localhost:7228` with certificate checks off for the dev certificate. WebSockets are forwarded too, so live updates work:
new records, alerts, and being signed out at once when another browser uses *Sign out other sessions*. Use `--api <url>` or `--port <n>` to
change either. (`npm run start:static` is the old http-server setup; it can't forward WebSockets, so the portal falls back to checking
every 30 seconds.) To call the API directly instead, set
`apiBaseUrl` in [app/config.js](app/config.js) to an absolute URL; the API must then allow CORS from this origin.

The mock server's `/api/ai/messages` matches on keywords and returns tool calls shaped like the real API's. It is not a
language model. It exists only so the assistant's actions (search, chart, export, draft, undo…) can be clicked
through.

## Features

- **Sign-in and administration:**
  - **Sign in with Microsoft** (Entra ID, MFA required), with roles and data access from Entra groups; see
    [Microsoft sign-in](#microsoft-sign-in-entra-id)
  - a local sign-in form (can be switched off or limited to break-glass accounts), with lockout after 5 failed attempts
  - a forced password change for temporary passwords
  - a user menu with Change password and Sign out
  - an **Administration** page. Each tab needs its own permission:
    - Users
    - Roles and permissions
    - Access policies
    - Microsoft sign-in (group mappings)
    - Recycle bin
    - Letter templates
    - Alerts
    - Audit log
- **Grid:**
  - server-side search, sort, paging and column filters
  - row selection; status badges, with a left accent on Failed and Processing rows
  - short ids with a copy button
  - a column chooser and a Comfortable/Compact density toggle, both remembered per browser
  - payload viewer (JSON, or flattened key–value)
  - "Explain" sends the record to the assistant
- **Active filters bar:** every filter in one row of removable chips: the AI search (marked "AI"), the search text and
  each column filter. It also shows the match count and a "Clear all" link.
- **Selection bar:** appears when rows are checked, with Resubmit, Export selected, Delete and Clear selection.
- **PDFs:** every request gets a PDF of its payload, stored in Blob Storage by the API's background worker. The
  row's PDF icon shows the state: green means ready (click to download), grey means not generated yet (click to
  generate), amber means failed (click to retry). Selection bar → **Generate PDFs** does several at once. Downloading
  needs payload access, like viewing the payload. See `ProcessRequestEndPoint/Pdf/README.md` in the API.
- **Record details:** click a Doc Id, or the ⓘ icon, to open a side panel with these tabs:
  - **Overview:** the record's fields.
  - **Timeline:** every status from the status history, with how long each one lasted.
  - **Payload:** shown if the user may see it.
  - **PDF:** its state, plus View or Generate.
  - **Activity:** resubmits, PDFs, deletes and restores, and refused actions on that record, from the audit log.

  The panel refreshes itself when the record changes.
- **Delete and the recycle bin:** Delete (row or selection bar) moves records, with their status history and PDF, to
  the recycle bin. **Administration → Recycle bin** restores them exactly as they were, or deletes them for good.
  - A scheduled job purges the bin after 30 days.
  - It can also expire old records once a retention period is configured. That is off until the business sets one.

  On the Users tab, Delete removes a user account. You can't delete yourself or the last active Administrator;
  deactivating the account is the reversible option. Everything is in the audit log.
- **Letter templates:** the PDF of a request uses the **published** template for its document type: a branded letter
  with letterhead, address block, subject, body and signature. Types without a published template get the data sheet.
  - Templates are versioned: drafts are editable; published versions are frozen.
  - Publishing retires the previous version. Each PDF records the template version it used.
  - The editor has placeholder chips (`{{RecipientName}}`, `{{ClaimNumber}}`, `{{PolicyNumber}}` (masked), `{{payload.Some.Path}}`, …).
  - **Preview** renders the editor's text with sample data. Unknown placeholders block publishing.
  - In the Insert form, **Preview letter** shows the letter the payload would produce before you submit it. Missing
    values are marked `[Name]`.
- **Alerts and live updates:** the API checks the Failed rate, the processing backlog and failed PDFs on a schedule.
  - When a rule trips, it raises an alert: in the header bell, on **Administration → Alerts**, and in Teams and/or
    email if configured.
  - Alerts are acknowledged in the portal.
  - The grid gets live updates over SignalR. A banner offers "N new / N updated — Refresh", and PDF icons update in
    place. Without WebSockets it polls a change feed every 30 s.
- **Toolbar:**
  - search
  - Excel export (page / selected / all matching; SpreadsheetML)
  - analytics
  - manual insert

  Each control appears only if the user's role allows it.
- **Analytics:** computed from every matching record, not just the current page.
- **"Ask ECCO" assistant:** a floating, dockable window, unchanged from the port. It sees the grid's column filters, and
  its chat history belongs to the signed-in user.
- **Themes:** Light and Dark, switched by the button in the header.

### Grid filtering

The filter row and column menus follow
[KendoReact Grid filtering](https://www.telerik.com/kendo-react-ui/components/grid/filtering). The filter state is a
Kendo-style `CompositeFilterDescriptor` (`{ logic, filters: [{ field, operator, value, ignoreCase }] }`).

**The search API applies it to every matching record,** not just the loaded page:

- The UI sends `filter` in the search request. `gridFilter.toWire` writes dates as `yyyy-MM-dd`.
- The API turns it into parameterized SQL (`CorrespondenceQueryBuilder.cs`), using an allow-list of fields and
  operators.
- Totals, paging, analytics and "Export all" all reflect the filters.

| Column type | Editor | Operators (first one is the default) |
|---|---|---|
| Text (Id, Doc Id, Claim, Customer, Delivery) | text box | Contains, Does not contain, Is equal to, Is not equal to, Starts with, Ends with, Is null, Is not null, Is empty, Is not empty |
| Date (Created) | date picker | Is equal to, Is not equal to, Is after or equal to, Is after, Is before or equal to, Is before, Is null, Is not null |
| Status | dropdown of known statuses | Is equal to, Is not equal to, Is null, Is not null |

Column menu (⋮):

- **Sort:** Sort Ascending / Descending.
- **Two conditions joined by And/Or:** Id, Doc Id, Claim Number, Created.
- **Checkbox list:** Customer, Delivery, Status. The values come from the API and are limited to what the user may
  see.

Other behaviour:

- Text matching ignores case.
- Dates compare by calendar day.
- Filters on different columns combine with AND.
- An AI search replaces a column filter on the same field, so the two can't contradict each other. It keeps unrelated
  column filters.

The pager follows the KendoReact Pager: first/prev, page numbers in blocks of 4 with "..." between blocks, next/last,
"items per page", and "1 - 10 of 77 items".

## Access control (RBAC + ABAC)

Sign-in and every rule are enforced by the API. The UI only hides what the user can't use.

**Accounts and tokens**

- Local portal accounts stored in `auth.AppUser`.
- Passwords are hashed with PBKDF2-HMAC-SHA256 (600,000 iterations).
- The API issues a 60-minute HS256 token carrying the user id and a *security stamp*. Roles, permissions and
  attributes are re-read per request (cached 60 seconds).
- Changing a password, roles or attributes, or deactivating a user, rotates the stamp. That ends the user's sessions.

**Roles (RBAC).** A role grants permissions:

| Permission | Administrator | Operator | Analyst | Viewer |
|---|:-:|:-:|:-:|:-:|
| `correspondence.read` | ✓ | ✓ | ✓ | ✓ |
| `correspondence.payload.read` | ✓ | ✓ | | |
| `correspondence.create` / `correspondence.resubmit` | ✓ | ✓ | | |
| `correspondence.export`, `analytics.view`, `ai.use` | ✓ | ✓ | ✓ | |
| `correspondence.delete` | ✓ | | | |
| `correspondence.pdf.generate` | ✓ | ✓ | | |
| `correspondence.recycle` (recycle bin), `letters.templates.manage` | ✓ | | | |
| `alerts.read` | ✓ | ✓ | ✓ | |
| `admin.users.manage`, `admin.audit.read` | ✓ | | | |

**Attributes (ABAC).** Per-user values in `auth.UserAttribute`:

- `scope.customer` and `scope.deliveryMethod` filter every search in SQL. `*` means all; no value means no records.
- `piiClearance` is required to see payloads.
- `maxExportRows` caps "Export all matching".

**Access policies (ABAC).** Deny rules in `auth.AccessPolicy`, checked after the role check against the user, the record
and the time:

| Seeded policy | Effect |
|---|---|
| Payloads require PII clearance | payloads are left out of search results for users without `piiClearance = true` |
| Resubmit failed records only | resubmitting a non-Failed record is refused with the reason |
| Resubmit / Create / Delete within customer scope | the record's customer must be in `scope.customer` |
| Export row limit | "Export all" larger than `maxExportRows` is refused |
| Resubmit during business hours (sample) | off by default; shows a time-based rule |

Administrators can switch policies on or off and edit their conditions on the Administration page. Missing values and
malformed rules fail closed (deny). Sign-ins, denials and admin changes are written to `auth.AuthAuditLog`.

The portal writes through `POST /api/portal/correspondence/requests` and `/api/portal/correspondence/{id}/resubmit`.
These run the checks above and then the existing `CommManager` pipeline. The system-facing
`/api/CommManager/processcorrespondencerequest` is unchanged.

## Microsoft sign-in (Entra ID)

The API reports whether Microsoft sign-in is on at `GET /api/auth/config`. The same build works everywhere.

**How sign-in works**

1. The browser signs in with MSAL.js: authorization code with PKCE, full-page redirect.
2. It sends the ID token to `POST /api/auth/entra`.
3. The API checks:
   - the signature
   - the issuer and tenant
   - the audience (client id)
   - the token's age (10 minutes at most)
   - that `amr` contains `mfa`
4. If all pass, it issues the usual portal session.
   - Accounts are created on first sign-in, keyed by the Entra object id (never matched by user name).
   - Roles and data access are re-synced from the token's `groups` / `roles` claims at every sign-in.
   - Removing someone from a group removes their access at their next sign-in.
   - A change ends their open sessions.

**Setup (once per environment)**

1. **Entra admin center → App registrations → New registration.** Use single tenant.
   - Platform **Single-page application**.
   - Redirect URI = the portal URL, e.g. `https://portal.example.com/` (and `http://localhost:8080/` for dev).
2. **Token configuration → Add groups claim → Security groups** (ID token). Alternatively, define **App roles**
   (e.g. `Portal.Operator`) and assign them under Enterprise applications. Use app roles if users can be in more than
   200 groups.
3. **Conditional Access:** a policy requiring MFA for this app. The API refuses tokens without MFA either way.
4. **API `appsettings`:**

   ```json
   "PortalAuth": {
     "Entra": { "Enabled": true, "TenantId": "<tenant guid>", "ClientId": "<app client id>" },
     "LocalLogin": { "Enabled": true, "AllowedUserNames": [ "admin" ] }
   }
   ```

   `LocalLogin.AllowedUserNames` keeps one break-glass local account. Set `Enabled: false` to turn local sign-in off
   completely.
5. **Administration → Microsoft sign-in:** map each group's object id (or app-role value) to a role and/or data
   access, e.g. *CC Operators* → Operator, and *Life Services team* → `scope.customer = Life Services`.

On the Users tab, Microsoft accounts show a *Microsoft* badge and have no password. Roles that come from groups have a
dashed outline and can't be edited there.

## Structure

```
app/
  app.module.js, config.js            module + runtime settings (API base URL, AI model)
  shell/                              <app-shell>: sign-in gate, header (nav, theme, user menu), routing
  services/
    auth.service.js                   session, $http interceptor (bearer token, 401 → sign-in), adminApi
    api.service.js                    correspondenceApi, chatHistoryApi, aiService ($http)
    grid-filter.service.js            Kendo-style filter descriptors, filterBy, toWire, describe
    search-query.service.js           AI params ⇄ backend request, filter labels and chips
    ai-prompt.service.js              tool schemas, system prompt, response → action parsing
    correspondence-analytics.service.js, payload-validation.service.js, search-refinement.service.js,
    markdown.service.js, excel-export.service.js, speech.service.js, theme.service.js, storage.service.js,
    error-recovery.service.js, grid.service.js, assistant-bridge.service.js
    sso.service.js                    Microsoft sign-in (MSAL redirect → /api/auth/entra)
    realtime.service.js               SignalR live updates, change-feed polling fallback
  components/
    auth/ (sign-in page, change-password form)
    record/                           record detail panel (timeline, payload, PDF, activity)
    pdf/                              PDF viewer (stored PDFs and letter previews)
    correspondence-grid/  pagination/  analytics-panel/  charts/  payload/  assistant/  shared/
  pages/search/                       the Correspondence page
  pages/admin/                        users, roles, policies, Microsoft sign-in, recycle bin, letter templates, alerts, audit
css/styles.css                        light/dark themes + component styles
tools/mock-api.js, mock-auth.js,      mock backend incl. sign-in (local + mock Microsoft), RBAC/ABAC, admin endpoints,
  mock-pdf.js, mock-features.js       PDFs, record details, recycle bin, letters, alerts, change feed (Node built-ins only)
tests/                                Node unit tests with a tiny AngularJS DI stand-in
```

The matching backend changes are listed in
`ECCO-CommunicationManager/CustomerCorrespondence.Orchestrator/ProcessRequestEndPoint/Auth/README.md`.

### React → AngularJS mapping

| React | AngularJS |
|---|---|
| `useState` / props | component controllers, `<` / `&` bindings |
| `ThemeContext` | `themeService` |
| `forwardRef` + `useImperativeHandle` (`openAndAsk`) | `assistantBridge.openAndAsk()` |
| `searchContextRef` (fresh data inside async turns) | `assistantBridge.context()` provider, read at call time |
| axios + interceptor | `$http` + `authInterceptor` + `errorRecovery.classifyError` |
| Kendo Grid / Dialog, react-bootstrap | `correspondence-grid`, `confirm-dialog`, `prompt-dialog`, CSS |
| recharts | hand-rolled SVG (`analytics-panel`, `mini-chart`) |

## Behaviour that differs from the React app

These are fixes for bugs found while porting:

- "Summarize… / summary" now triggers narrative mode. The regex `\bsummar\b` never matched those words.
- The "Consider adding a phone/email" suggestions appear when the value is *missing*. They used to appear only when it
  was already present.
- Clearing a column sort now actually clears it. Paging keeps the current sort instead of reverting to the AI's.
- Responses that arrive out of order are dropped.
- Applying a "Search Tips" suggestion no longer caps the result total at the page size.
- "Fix Typo" searches the corrected text. Before, it searched the old text.
- Top-N analytics count only those N records. "Export all" respects multi-status filters and includes payloads.
- A follow-up question sent while the first turn is still saving no longer splits one chat into two in History.
- Closing the assistant keeps the conversation. It switches to the Conversation tab when you ask something.
- After an AI draft, the chat says "Draft opened for review" (it used to say "submitted").
- A draft without a requesting system is flagged before submit. The API rejects it anyway.

Known, unchanged: the AI's `deliveryMethod` enum offers `ePresent`, but payload validation only recognises
email/sms/phone/mail/physical, so an ePresent draft is flagged as an unknown delivery method. Decide which side should
change before relying on ePresent drafts.
