# Beacon-Relay-2.0

Beacon Relay 2.0 is a production-oriented .NET 8 service that receives LPD (RFC 1179) print jobs, stores data files to disk, and records job/file metadata in SQLite.

## What it does

- Runs as a long-lived hosted service.
- Listens for LPD `0x02` (Receive printer job) commands on configurable TCP port (default `515`).
- Parses and validates receive-job subcommands:
  - `0x02` receive control file
  - `0x03` receive data file
- Enforces strict ACK/NAK exchange and payload validation:
  - malformed lines rejected
  - exact byte-count reads required
  - trailing NUL byte required for file payload blocks
  - configurable limits for line length, file/job size, files/job, and session timeout
- Extracts control-file metadata (job/user/host/class/banner/source hints) and stores raw control text.
- Persists files with safe deterministic filenames and atomic write behavior.
- Computes and stores SHA-256 for each file.
- Uses SQLite (EF Core) for low-maintenance persistence.
- Supports duplicate handling by content hash:
  - `MarkAndStore` (default): keep file and flag duplicate
  - `SkipWrite`: skip second write and reference existing file
- Runs retention cleanup in background with configurable schedule/window.
- Exposes health endpoints:
  - `/healthz` (liveness)
  - `/readyz` (readiness incl. DB check)

## Project layout

- `/BeaconRelay.LpdReceiver` - service implementation
- `/BeaconRelay.LpdReceiver.Tests` - focused unit tests for parser/session validation, sanitization, hashing/dedup, and control metadata parsing

## Configuration

`BeaconRelay.LpdReceiver/appsettings.json` includes strongly typed sections:

- `Lpd`
  - `Port`
  - `MaxLineLength`
  - `MaxFileBytes`
  - `MaxJobBytes`
  - `MaxFilesPerJob`
  - `SessionTimeoutSeconds`
  - `InitialRestartBackoffSeconds`
  - `MaxRestartBackoffSeconds`
- `Storage`
  - `OutputDirectory`
- `Database`
  - `ConnectionString` (SQLite)
- `Deduplication`
  - `Mode` = `MarkAndStore` or `SkipWrite`
- `Retention`
  - `Enabled`
  - `RetentionDays`
  - `IntervalMinutes`
- `Health`
  - `Port`
  - `HealthPath`
  - `ReadinessPath`

## Build, test, run

From repository root:

```bash
dotnet test BeaconRelay.slnx
dotnet run --project BeaconRelay.LpdReceiver/BeaconRelay.LpdReceiver.csproj
```

## Deployment Workflow

Two deployment scripts are provided under `deploy/`:

- `Deploy-BeaconRelay.ps1`: installs/upgrades/removes the service and IIS proxy on the target machine.
- `Build-BeaconRelayArtifact.ps1`: builds a portable artifact (`publish/`, deploy scripts, checksums, optional zip) for target machines.

### Build artifact on build machine

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Build-BeaconRelayArtifact.ps1 \
  -Configuration Release \
  -Runtime win-x64 \
  -SelfContained
```

This creates an artifact folder under `artifacts/` and, by default, a `.zip` archive.

### Deploy to test machine (from artifact)

1. Copy the artifact contents to the target (ensure `publish/` and `deploy/` are present).
2. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Deploy-BeaconRelay.ps1 \
  -Mode Deploy \
  -NoPublish \
  -HostName beaconrelay-test.local \
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" \
  -DatabasePassword "<test-sqlcipher-password>" \
  -AdminUsername "admin" \
  -AdminPassword "<initial-test-admin-password>"
```

### Deploy to production machine (from artifact)

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Deploy-BeaconRelay.ps1 \
  -Mode Upgrade \
  -NoPublish \
  -HostName beaconrelay.company.local \
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" \
  -DatabasePassword "<prod-sqlcipher-password>" \
  -ConvertExistingDatabaseToSqlCipher \
  -DatabaseBackupPath "D:\Backups\beacon-relay-pre-sqlcipher.db" \
  -AdminUsername "admin" \
  -AdminPassword "<initial-prod-admin-password>" \
  -CertificateThumbprint "<tls-cert-thumbprint>"
```

Notes:

- `-NoPublish` is intended for artifact-based deploys where binaries are already present in `publish/`.
- `-ConvertExistingDatabaseToSqlCipher` is required to convert an existing plaintext SQLite database.
- Use either `-DatabaseBackupPath` or `-SkipDatabaseConversionBackup` when converting, not both.
- `AdminAuth` values are bootstrap credentials for first-run admin seeding.

## Database migration/init

Migrations are included under:

- `BeaconRelay.LpdReceiver/Data/Migrations`

On startup, the service runs `Database.Migrate()` automatically.

## Protocol scope and limitations

- Implements strict receive path for RFC 1179 receive printer job flow (`0x02`) plus subcommands (`0x02` control file, `0x03` data file).
- Requires control file before data files in a job.
- Rejects unsupported top-level commands.
- No TLS is built into RFC 1179 path (deploy behind secure network controls or a secure tunnel if needed).

## Operational notes

- LPD default port (`515`) commonly requires elevated privileges on some systems.
- Ensure service account has write permission to configured output directory.
- Retention updates DB status if files are deleted or already missing.
- All persisted timestamps use UTC.
