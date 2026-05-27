# Beacon-Relay-2.0

## Pull Request #1 — Summary

**Title:** Add production-grade .NET 8 LPD receiver service with strict RFC 1179 handling, SQLite tracking, retention, dedup, and health endpoints
**Status:** Draft (open)
**Branch:** `copilot/implement-self-healing-lpd-receiver` → `main`
**Stats:** 4 commits · 41 files changed · 1 891 additions

---

### Overview

PR #1 introduces a complete, production-oriented .NET 8 service (`BeaconRelay.LpdReceiver`) that listens for inbound LPD print jobs (RFC 1179), persists both raw payloads and rich metadata, and provides operational safeguards for a long-running deployment. A focused unit-test project (`BeaconRelay.LpdReceiver.Tests`) is included alongside the service.

---

### Key Components

#### Service architecture
- **Hosted/Worker service** built on ASP.NET Core with a self-healing TCP listener loop (bounded-backoff restart on fatal errors).
- **Per-client session isolation** — a single faulty client cannot take down the listener.
- **Cancellation-aware shutdown** — integrates cleanly with the .NET host lifetime.

#### RFC 1179 protocol handling (strict)
- Receives the top-level `0x02` *Receive a printer job* command.
- Handles subcommands inside the job flow:
  - `0x02` — Receive control file
  - `0x03` — Receive data file
- Enforces correct ACK (`0x00`) / NAK (`0x01`) sequencing at every phase.
- Validates byte counts exactly; requires the trailing NUL byte after each transmitted block.
- Rejects malformed/oversized requests with logging and NAK.
- Configurable defensive limits: max line length, max files per job, max bytes per job/file, session timeout.
- Extracts rich metadata from control-file records (job name, user, host, banner/class, source file hint) while preserving the raw control text.

#### File persistence and integrity
- Configurable output directory, created automatically on startup.
- Safe filename sanitization and path-containment checks (no directory traversal or injection).
- Deterministic, traceable stored filenames (UTC timestamp + queue/job index + sanitized source name).
- **Atomic writes** via temp-file-then-move.
- **SHA-256** content hash computed and stored for every file.

#### SQLite persistence (EF Core + migrations)
- `AppDbContext` + `ReceivedFileRecord` entity storing:
  - UTC received/created/updated timestamps
  - Queue name, remote endpoint, LPD job identifier
  - Original and stored filenames, byte size, SHA-256
  - Parsed control-file metadata fields + raw control text
  - Status, error details, duplicate flag and duplicate reference
- Indexed on received time, hash, status, and job ID.
- EF Core migration files included; DB auto-migrates on startup.

#### Duplicate detection
- Hash + size-based duplicate detection.
- Configurable behavior:
  - `MarkAndStore` — store both copies, flag the duplicate in the DB.
  - `SkipWrite` — skip the second write, reuse the original stored path, mark as duplicate.

#### Retention background worker
- Configurable retention window (days to keep) and poll interval.
- Deletes expired files from disk; updates DB record status.
- Handles already-missing files gracefully.
- Logs a summary of each cleanup run.

#### Health and readiness endpoints
- HTTP `/healthz` and `/readyz` on a configurable port.
- Health checks: TCP listener activity + DB connectivity.

---

### Configuration

Strongly typed options cover every subsystem:

| Option group | Key settings |
|---|---|
| LPD | port (default 515), limits (line length, files/job, bytes), session timeout |
| Storage | output directory |
| Database | SQLite connection string |
| Dedup | mode (`MarkAndStore` / `SkipWrite`) |
| Retention | retention days, poll interval |
| Health | HTTP port, paths |
| Logging | per-category levels |

---

### Test Coverage (`BeaconRelay.LpdReceiver.Tests`)

Unit tests cover:
- Protocol parser validation (including malformed/edge-case inputs)
- Session-level validation (oversized payloads, missing trailing NUL)
- Control-file metadata extraction
- Filename sanitization and path-traversal prevention
- SHA-256 hash computation
- Duplicate-detection policy behavior

---

### Files Added (highlights)

| Path | Purpose |
|---|---|
| `.gitignore` | Standard .NET + IDE ignore rules |
| `BeaconRelay.LpdReceiver/` | Main service project (worker, protocol, storage, DB, retention, health) |
| `BeaconRelay.LpdReceiver.Tests/` | Unit test project |
| `Migrations/` | EF Core migration files |
| `appsettings.json` | Default configuration with inline comments |
| `README.md` (PR branch) | Extended operational documentation |
