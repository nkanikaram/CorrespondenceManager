# Correspondence Manager portal: sign-in, RBAC and ABAC

This folder holds local portal accounts, permission checks and attribute-based policies for the Correspondence Manager
UI (`admin-portal`).

It adds a second JWT scheme, `EccoPortal`, next to the existing OIDC one. The default authentication scheme is
unchanged. Only endpoints marked `[RequirePermission]` / `[RequirePortalUser]` use the new scheme.

## Setup

1. **Database.** Run `Build/DBScripts/Auth` in order against the API's database. The scripts are re-runnable.
   - `001_PortalAuth_Schema.sql` creates schema `auth` with these tables:
     - AppUser
     - Role
     - Permission
     - RolePermission
     - UserRole
     - AttributeDefinition
     - UserAttribute
     - AccessPolicy
     - AuthAuditLog

     It also adds two indexes.
   - `002_PortalAuth_ReferenceData.sql` adds the permissions, roles, role grants, attribute definitions and access
     policies. It only inserts missing rows, so edits made in the portal survive a re-run.
   - `003_PortalAuth_DevUsers.sql` adds `admin`, `operator`, `analyst` and `viewer`, all with the password
     `EccoDev!2026`. **Development and test only.** It refuses to run on databases whose name looks like Prod or
     Demo, but don't rely on that.

   Run them with `sqlcmd -I` or SSMS. The scripts set `QUOTED_IDENTIFIER ON` themselves.
2. **Signing key.** Set `PortalAuth:SigningKey`: base64, 32 or more random bytes. In Azure, use an App Service setting
   `PortalAuth__SigningKey` or a Key Vault reference.
   - Without it, the API refuses to start outside Development.
   - In Development without it, the API uses a temporary key, so sessions end on restart.
   - `appsettings.Development.json` contains a random development-only key.
3. **First administrator (non-dev environments).** Set `PortalAuth__BootstrapAdmin__UserName` and
   `PortalAuth__BootstrapAdmin__Password`.
   - On start-up, if `auth.AppUser` is empty, an Administrator with that password is created. It must change the
     password at first sign-in.
   - Remove the settings afterwards.

Other settings are in `appsettings.json` → `PortalAuth`: token lifetime, lockout threshold and minutes, minimum
password length, and profile cache seconds.

## How a request is authorized

1. `[RequirePermission("x")]` authenticates with the `EccoPortal` scheme. `OnTokenValidated` then rejects the token if
   the user is inactive or its security stamp has changed.
2. `PermissionHandler` loads the user's access profile through `PortalAccessService`, which caches it for 60 seconds.
   The profile holds roles, permissions and attributes. The handler requires the permission (RBAC). A pending forced
   password change blocks everything except `/api/auth/*`.
3. For record-level decisions, the action calls `PortalAccessService.CheckAsync(permission, resource)`. That runs
   `AccessPolicyEvaluator`: active Deny policies for the permission, in priority order, against `subject.*`,
   `resource.*` and `env.*` attributes (ABAC). Missing values and malformed rules fail closed.
4. Data scope is applied in SQL: `CorrespondenceQueryBuilder.AppendScope` turns `scope.customer` and
   `scope.deliveryMethod` into `IN (...)` filters.

Microsoft Entra ID sign-in and the other portal features are described in [PORTAL-FEATURES.md](../PORTAL-FEATURES.md).

## Endpoints

| Endpoint | Access |
|---|---|
| `POST /api/auth/login`, `GET /api/auth/password-policy` | anonymous |
| `GET /api/auth/me`, `POST /api/auth/change-password`, `POST /api/auth/logout` | any signed-in portal user |
| `POST /api/correspondence/search`, `GET statuses`, `GET values/{customer\|delivery}` | `correspondence.read`, plus data scope |
| ↳ `includePayload` | payloads only with `correspondence.payload.read` and the PII policy, otherwise `payloadsHidden: true` |
| ↳ `purpose: "export"` | `correspondence.export` and the export-row-limit policy on the total |
| `POST /api/portal/correspondence/requests` | `correspondence.create`, plus the "create within customer scope" policy |
| `POST /api/portal/correspondence/{id}/resubmit` | `correspondence.resubmit`, plus policies on the stored record; the payload must belong to that record's customer |
| `POST /api/portal/correspondence/delete` `{ correspondenceIds }` | `correspondence.delete` (Administrator), plus the "delete within customer scope" policy per record; moves the records, their status history and PDF to the recycle bin in one transaction (max 500) |
| Microsoft sign-in, record details, recycle bin, letter templates, alerts, live updates | see [PORTAL-FEATURES.md](../PORTAL-FEATURES.md) |
| `/api/ai/*`, `/api/chathistory/*` | `ai.use`; conversations are owned by the signed-in user |
| `GET /api/chathistory/frequent-queries` | also `admin.users.manage` (reads every user's messages) |
| `/api/admin/*` (users, roles, permissions, attributes, policies, lookups) | `admin.users.manage` |
| `DELETE /api/admin/users/{id}` | `admin.users.manage`; not yourself, not the last active Administrator |
| `GET /api/admin/audit` | `admin.audit.read` |

**Portal write endpoints.** The portal create and resubmit endpoints authorize the request, then call
`CommManagerController.ProcessCorrespondenceRequest`. Validation, document ids and messaging therefore stay in one
place.

`/api/CommManager/*` is unchanged and still anonymous at the API, as it was before this change.

## Files

- **New:** everything in this folder, plus `Controllers/AuthController.cs`, `Controllers/AdminController.cs` and
  `Controllers/PortalCorrespondenceController.cs`.
- **Changed:**
  - `Program.cs`: one line, `AddPortalAuth(builder)`.
  - `Controllers/CorrespondenceSearchController.cs`: permission, data scope, grid filters, payload and export policies,
    sort columns, and the `values` endpoint.
  - `Controllers/ChatHistoryController.cs`: owner from the token, ownership checks.
  - `Controllers/AiController.cs`: `ai.use`.
  - `appsettings.json` and `appsettings.Development.json`: the `PortalAuth` section.

## Tests

`CustomerCorrespondence.TestProjects/PortalAuthTests` covers:

- the evaluator, including the seeded policies
- the SQL builder: parameterization, LIKE escaping, rejected fields and operators
- password hashing, including the dev-seed hashes

The repository and HTTP-pipeline tests run only when `ECCO_AUTH_TEST_CONNECTION` points at a **throwaway** database with
the three scripts applied. They create and edit users. Example:

```
set ECCO_AUTH_TEST_CONNECTION=Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=ECCO_AuthScratch;Integrated Security=True;Encrypt=False
dotnet test CustomerCorrespondence.TestProjects/PortalAuthTests
```
