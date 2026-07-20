# Running the Reports stack locally, end to end

A throwaway PostgreSQL + API + frontend on your own machine, with a real tenant and data.
Use it to see a change working in a browser, and to run the DB-backed tests that otherwise
skip.

**Nothing here touches Azure.** Do not point this at `hrcloud`, and do not add your IP to the
production firewall to run tests.

## The short version

```bash
./scripts/reports-e2e.sh up       # cluster + migrations + API + tenant + seed + frontend
./scripts/reports-e2e.sh test     # Platform suite against the same database
./scripts/reports-e2e.sh down     # stop everything, delete the cluster and the token
```

`up` prints the URLs and the snippet for injecting the access token. Everything it creates
lives in `.e2e/` at the repo root.

Prerequisites: PostgreSQL 18 client **and server** binaries (`initdb`, `pg_ctl`, `psql`), the
.NET SDK, Node, and Python 3. Override the binary location with `PG_BIN=... ./scripts/reports-e2e.sh up`.

## What each step is for

Every one of these exists because skipping it fails in a way that is not obvious.

### 1. An isolated cluster, not the machine's PostgreSQL

```bash
initdb -D .e2e/pgdata -U testadmin --auth=trust --encoding=UTF8 --locale=C
pg_ctl -D .e2e/pgdata -o "-p 55432 -c listen_addresses=localhost" start
createdb -p 55432 -U testadmin hrcloud_e2e
```

Docker is not installed on the dev machines, so Testcontainers is not an option. A fresh
cluster avoids needing the existing server's password, cannot collide with real data, and
`down` can delete the entire directory. Port **55432**, never 5432, so an existing local
server keeps working. `--auth=trust` is safe here: it listens on localhost only and holds
nothing but disposable data.

### 2. Migrations

```bash
cd backend
ConnectionStrings__DefaultConnection="Host=localhost;Port=55432;Database=hrcloud_e2e;Username=testadmin" \
  dotnet ef database update \
    --project src/HR.Infrastructure/HR.Infrastructure.csproj \
    --startup-project src/HR.Api/HR.Api.csproj
```

Permissions are seeded through `HasData`, so they exist as soon as migrations are applied —
there is no separate permission seeding step.

### 3. `DOTNET_ROLL_FORWARD=Major`

```bash
export DOTNET_ROLL_FORWARD=Major
```

The projects target `net8.0`. If the machine only has the .NET 10 runtime, every `dotnet test`
and `dotnet ef` invocation dies with:

```
You must install or update .NET to run this application.
Framework: 'Microsoft.NETCore.App', version '8.0.0'
```

That reads like a broken SDK install. It is not — roll-forward fixes it.

### 4. CORS for the frontend's port

```bash
Cors__Origins__0="http://localhost:3001"
```

`appsettings.json` allows only `http://localhost:3000`, but `package.json`'s `dev` script
serves on **3001**. Without this override the page renders its shell and every data fetch
fails preflight, so the UI looks like an empty tenant rather than a misconfiguration:

```
Access to fetch at 'http://localhost:5177/api/platform/reports' from origin
'http://localhost:3001' has been blocked by CORS policy
```

### 5. Registering a user

```bash
curl -X POST http://localhost:5177/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"companyName":"Sanad E2E","fullName":"Admin User",
       "email":"admin@sanad.test","password":"Passw0rd!23"}'
```

`register` bootstraps the tenant *and* its first admin. On an empty database there is no other
way to obtain a usable account. The response carries the access token.

### 6. The access token

The frontend reads its session from `localStorage` (`src/lib/auth-storage.ts`), so a token can
be injected without driving the login form:

```js
localStorage.setItem('hr_access_token', '<token>');
localStorage.setItem('hr_refresh_token', '<token>');
localStorage.setItem('hr_user', JSON.stringify({
  id: '0', email: 'admin@sanad.test', fullName: 'Admin User', permissions: []
}));
```

Headless, set it before any app code runs, or the first render sees no session:

```js
await page.addInitScript(t => localStorage.setItem('hr_access_token', t), token);
```

### 7. Seeding reports

```bash
POST /api/platform/objects/seed-reportable    # register the reportable ObjectDefinitions
POST /api/platform/reports/seed-system        # SYS_<subject>, one per registry subject
POST /api/platform/reports/seed-defaults      # the seven named reports
```

Order matters. `seed-reportable` registers `MasterDataItem`, which is what lets leave type,
nationality and request type render as names instead of raw GUIDs. Both report seeders are
idempotent and their codes never collide, so running them repeatedly is safe.

For rows to actually appear you also need domain data (employees, attendance, leave balances).
Seed that with SQL against `hrcloud_e2e`; there is no API seeder for it.

### 8. Running the DB-backed tests

```bash
cd backend
REPORTS_TEST_DB="Host=localhost;Port=55432;Database=hrcloud_e2e;Username=testadmin" \
  dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj
```

**This is the important one.** The report engine's integration tests are `SkippableFact` on
`REPORTS_TEST_DB`. Without it they skip, and a green run says nothing about report execution,
authorization or search. Three long-standing test bugs were only discovered the first time a
real database was attached.

Each test runs in a transaction and rolls back, so the database is reusable across runs.

### 9. Backfilling ownerless legacy reports

```bash
./scripts/reports-e2e.sh backfill            # dry run — shows the plan, writes nothing
./scripts/reports-e2e.sh backfill --apply    # write it
```

Reports created before ownership was recorded have `OwnerId = null`. Since `CanEdit` is
"owner **or** a share granting edit", those reports are editable by nobody. The backfill
restores the owner the row already implies, from `CreatedBy`.

It is deliberately conservative and prints three lists:

| List | Meaning |
|---|---|
| **Will receive an owner** | `CreatedBy` resolves to exactly one user *in the same tenant* |
| **System, staying ownerless** | `SYS_*` — system-managed, clone-only, never assigned an owner |
| **Needs an administrator** | no `CreatedBy`, creator not in this tenant, or `CreatedBy` matches several accounts |

It never overwrites an existing owner, never assigns one to a system report, and never
falls back to "the first admin" or to the caller — a wrong owner silently hands someone
else's report away, so unresolvable rows are reported for a human to decide. Running it
twice assigns nothing the second time.

`CreatedBy` holds the creator's **email**, not a user id (`ApplicationDbContext` stamps
`_currentUser.Email`), so matching is by email and scoped to the tenant — a shared address
across tenants cannot leak ownership sideways.

The same thing is available directly at `POST /api/platform/reports/backfill-owners?dryRun=true`,
gated on `Platform.Reports.Delete`.

### System reports are clone-only

`SYS_*` reports are regenerated wholesale every time `seed-system` runs — the handler
hard-removes them and rebuilds from the field registry. Anything edited into one is
destroyed on the next seed with no warning, so `ReportAccessResolver.CanEdit` refuses them
outright, regardless of owner or share. Clone one and customise the copy; the clone is
owned by whoever made it and is fully editable.

### 10. Cleanup

```bash
./scripts/reports-e2e.sh down
```

Stops the API, the frontend and the cluster, then deletes `.e2e/` — including `token.txt`,
which is a real bearer credential and should not outlive the run.

## Gotchas

- **A hung `psql` against Azure** means the IP is not allowlisted. It surfaces as a TCP
  timeout, not an auth error. Do not add a firewall rule to run tests; use this local stack.
- **`az postgres flexible-server firewall-rule`** takes `--name` on `create` but `--server-name`
  on `list`. Unrelated to local work, but it costs time every time.
- **Windows line endings** in captured `psql` output break UUID literals; strip with
  `tr -d '\r'`.
- **Arabic through `curl` on Git Bash** can arrive mangled, which looks like a search bug that
  isn't one. Verify Arabic search in the browser or from a test, not from a shell.
- **React's development double-invoke** fires two concurrent requests on mount. That is what
  exposed the `LastViewedAt` race; expect duplicated calls in `api.log` and do not treat them
  as a bug in itself.
