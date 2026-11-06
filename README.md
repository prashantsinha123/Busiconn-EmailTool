# Busiconn Email Tool

A small local desktop-first admin dashboard for selecting existing Busiconn users, previewing HTML email, sending a test, and sending campaigns through SendGrid. Phone numbers are displayed only; SMS/WhatsApp sending is not implemented.

Busiconn.Backend was not modified.

## Architecture and API inspection

React/TypeScript/Vite/Tailwind -> EmailTool ASP.NET Core minimal API -> existing Busiconn HTTP API and SendGrid REST API. No existing backend assemblies, database connections, Appwrite collections, or migrations are used.

Inspected controllers and contracts:
- src/Busiconn.Api/Controllers/AdminController.cs: GET /api/v1/admin/users
- src/Busiconn.Api/Contracts/AdministrationContracts.cs: PagedResponse<AdminUserResponse>
- src/Busiconn.Api/Controllers/UsersController.cs: POST /api/v1/users/by-ids and GET /api/v1/users/{id}
- src/Busiconn.Api/Contracts/UserContracts.cs: UserResponse
- src/Busiconn.Api/Controllers/AuthController.cs and Program.cs: JWT bearer authentication.

Base route: /api/v1. Configure the actual host, without /api/v1, as BusiconnApi:BaseUrl; no deployment host was guessed.

GET /api/v1/admin/users requires role Admin or System. It returns documents, total, page, pageSize, totalPages. Documents contain id, displayName, username, email, isActive, roles, createdAtUtc, updatedAtUtc. Page starts at 1; pageSize is clamped to 1–100. Supported query filters: query (email/username/display name), role, isActive. The tool retrieves all pages and searches name/email/mobile and filters roles/status locally.

POST /api/v1/users/by-ids accepts { "userIds": ["guid"] } with at most 100 IDs per request; it requires authentication and returns active users only. Returned fields: id, name, username, emailOrPhone, imageUrl, bio, acceptedSla, isSupportAgent. Its mapping uses PhoneNumber first, then email. The tool combines admin email with phone-shaped emailOrPhone. Inactive users have no mobile enrichment. GET /api/v1/users/{id} also returns this DTO but would require one call per user, so batch lookup is used.

GET /api/v1/users is public, limited to 100, has no page traversal, and omits contacts; it is unsuitable for campaigns.

No existing marketing preference field was found in the backend API.
acceptedSla is not treated as marketing consent. No consent state is fabricated.

## Server configuration

Requires .NET 10 SDK and Node.js 20.19+ or 22.12+ (Node 20.18 also passed the local build).

Keep secrets in ASP.NET Core user secrets or server process environment variables. appsettings.example.json has placeholders only; do not copy real secrets into it. appsettings.json has blank credentials. The server reads standard ASP.NET Core section names, not a frontend .env file.

From PowerShell:
    cd C:\Projects\React\Busiconn.EmailTool\Server
    dotnet user-secrets set "BusiconnApi:BaseUrl" "https://YOUR-API-HOST/"
    dotnet user-secrets set "BusiconnApi:AccessToken" "YOUR-ADMIN-OR-SYSTEM-JWT"
    dotnet user-secrets set "SendGrid:ApiKey" "YOUR-SENDGRID-KEY"
    dotnet user-secrets set "SendGrid:FromEmail" "updates@busiconn.com"
    dotnet user-secrets set "SendGrid:FromName" "Busiconn"
    dotnet user-secrets set "Tool:AdminKey" "YOUR-RANDOM-LOCAL-ACCESS-KEY"
    dotnet run --environment Development

Alternatively use BusiconnApi__BaseUrl, BusiconnApi__AccessToken, SendGrid__ApiKey, SendGrid__FromEmail, SendGrid__FromName, Tool__AdminKey environment variables.

Use an existing supported admin sign-in/session to obtain the access token. POST /api/v1/auth/sign-in accepts login, password, deviceName. Authentication is validated by Busiconn; the tool does not bypass it, handle passwords, mint tokens, or automatically refresh sessions. Replace the configured token when it expires.

Configure a verified sender in SendGrid and an API key with Mail Send permission. SendGrid is called at https://api.sendgrid.com/v3/mail/send with server-side bearer authorization. The browser receives sender name/email only, never the API key or backend token.
Official API reference: https://www.twilio.com/docs/sendgrid/api-reference/mail-send/mail-send

The local tool access key is separate from SendGrid/backend credentials. Enter it in the dashboard; it is held in React memory only and sent in X-Tool-Key. This protects the local proxy and send endpoints. Both services default to localhost. Development CORS permits only http://localhost:5173. This V1 is intended for one trusted local administrator; deployment for shared use needs an appropriate session/login and hosting review.

## Run client

    cd C:\Projects\React\Busiconn.EmailTool\Client
    npm ci
    npm run dev

Client: http://localhost:5173
Server: http://localhost:5080
Vite proxies /api to the server.

## Workflow

1. Enter the local tool access key and load users.
2. Search/filter and select recipients. Select all filtered includes all eligible filtered pages; selection persists across filters.
3. Enter subject and HTML body. Supported placeholders: {{name}}, {{email}}. Values are HTML encoded and missing names use "there".
4. Open preview, enter a test recipient, and send exactly one test message.
5. Review the test, then send and confirm the campaign summary.
6. Inspect sent/failed/skipped totals and per-recipient results.

Preview is sandboxed and blocks scripts and external resources. No email addresses are accepted in the campaign payload: selected IDs are refreshed through the admin list and batch lookup before sending. Inactive, malformed/missing/synthetic emails, missing IDs, and duplicate addresses are skipped. No preference field exists to exclude marketing opt-outs locally; SendGrid's existing suppressions are not bypassed.

Server endpoints:
- GET /api/config: sender display only
- GET /api/users: normalized CampaignUser list
- POST /api/campaign/test: { email, subject, htmlContent }; returns one-use testToken
- POST /api/campaign/send: { userIds, subject, htmlContent, testToken }; returns selected, validRecipients, sent, failed, skipped, results

Test authorization is bound to the exact subject/body, expires after 30 minutes, and is consumed once. Composer changes require a new test. One campaign runs at a time; batches of 25 use at most four concurrent SendGrid calls and a 200 ms delay between batches. A single failure does not stop other recipients. SendGrid responses/exceptions are reduced to safe messages; provider response bodies and secrets are not shown.

## Verification

    dotnet build Server\Busiconn.EmailTool.Server.csproj
    dotnet run --project Tests\Tests.csproj
    cd Client
    npm run build

The isolated console harness uses simulated HTTP handlers: pagination, authorization headers, mobile enrichment, email validation, synthetic exclusion, HTML encoding, missing-name fallback, exactly-one test send, changed-content rejection, authoritative refresh, duplicate/inactive skips, failure isolation, result totals, and one-use test authorization. It never contacts the live backend or SendGrid.

Actual users and real email delivery require configured credentials and have not been verified in this environment. Use one controlled recipient for the first live campaign. No real emails were sent during implementation.

## Limitations

- "Sent" means SendGrid accepted the request (HTTP 202), not confirmed delivery. No delivery webhooks.
- No persisted campaign history, durable queue, retry, or unsubscribe management. Existing SendGrid suppression behavior remains in effect.
- Campaign processing is in-process. Browser disconnect does not cancel sending after data resolution; process termination interrupts it and loses results. An uncertain response should be checked in SendGrid before retrying.
- No automatic retry prevents accidental duplicate sends after ambiguous timeouts.
- All admin pages are refreshed before each campaign; offset pagination is not a snapshot and concurrent user changes can affect enumeration.
- Synthetic *@oauth.busiconn.local / *@busiconn.local email addresses are excluded. Admin list emails may differ from OAuth provider email.
- User data is available to the trusted administrator's browser while the page is open.
- Only active users can supply phone numbers through the batch endpoint.
- Production serving of the compiled client is not configured; use the documented local Vite workflow.

## File inventory

Authored files:
- .gitignore — excludes dependency/build outputs and local configuration.
- README.md — inspection, setup, workflow, verification, and limitations.
- Server/Busiconn.EmailTool.Server.csproj — standalone .NET 10 web project; no packages or backend references.
- Server/appsettings.json — local URL and blank server configuration.
- Server/appsettings.example.json — placeholders.
- Server/Program.cs — minimal API routes, access guard, typed HTTP client, models/options, SendGrid service, campaign batching and validation.
- Client/package.json — React/Vite/Tailwind dependencies and scripts.
- Client/package-lock.json — reproducible dependency lockfile.
- Client/tsconfig.json — TypeScript configuration.
- Client/vite.config.ts — React/Tailwind plugins and local API proxy.
- Client/index.html — browser entry.
- Client/src/main.tsx — recipients, composer, preview, test and send/result UI.
- Client/src/style.css — Tailwind import and minimal UI styles.
- Tests/Tests.csproj — isolated console verification project.
- Tests/Program.cs — simulated API/SendGrid checks.

Generated artifacts: Client/node_modules, Client/dist, Client/tsconfig.tsbuildinfo, Server/bin, Server/obj, Tests/bin, Tests/obj. These are reproducible and should not be committed.

Additional authored file:
- Tests/ui-fixture.mjs — loopback-only simulated user HTTP API for dashboard smoke tests. Requires the test-only key "ui-fixture"; every send endpoint is disabled. Run it only when the real server is stopped, then stop it after testing.

Browser smoke checks passed with this fixture: user loading, mobile search, bulk selection excluding invalid email, role filter, deselect-all, individual selection, and personalized HTML preview. The fixture and preview processes were stopped after verification.
# Busiconn-EmailTool
