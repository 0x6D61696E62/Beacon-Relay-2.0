# Beacon Relay Deployment Examples

This file provides copy/paste command examples for common deployment scenarios.

Primary scripts:

- `Deploy-BeaconRelay.ps1` (install/upgrade/remove/status)
- `Build-BeaconRelayArtifact.ps1` (build portable deployment artifact)

## 1) Build Artifact (Build Machine)

Use this on your CI/build machine or local dev box to generate a deployment artifact.

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Build-BeaconRelayArtifact.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -SelfContained
```

Optional:

- Add `-SkipZip` to avoid zip creation.
- Add `-Clean` to overwrite an existing artifact directory.

## 2) New Install From Source (Target Machine)

Use when source code is available on the target and you want the deploy script to publish.

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Deploy-BeaconRelay.ps1 `
  -Mode Deploy `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -HostName beaconrelay.company.local `
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" `
  -DatabasePassword '<sqlcipher-password>' `
  -AdminUsername "admin" `
  -AdminPassword "<bootstrap-admin-password>"
```

## 3) New Install From Artifact (Target Machine)

Use when you already copied artifact contents (`publish/` and `deploy/`) to target.

Run from the artifact `deploy` folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Deploy `
  -NoPublish `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -HostName beaconrelay.company.local `
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" `
  -DatabasePassword '<sqlcipher-password>' `
  -AdminUsername "admin" `
  -AdminPassword "<bootstrap-admin-password>"
```

Alternative: run deploy from any folder and let script copy artifact to `AppRoot` automatically:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Deploy-BeaconRelay.ps1 `
  -Mode Deploy `
  -NoPublish `
  -ArtifactSourcePath "C:\Users\bmain\Downloads\BeaconRelay-Release-win-x64-<build-id>" `
  -AppRoot "C:\Program Files\Interbit\Beacon Relay" `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -HostName beaconrelay.company.local `
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" `
  -DatabasePassword '<sqlcipher-password>' `
  -AdminUsername "admin" `
  -AdminPassword "<bootstrap-admin-password>"
```

## 4) Upgrade Existing Install (No DB Conversion)

Use when service already exists and database is already in desired format.

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Upgrade `
  -NoPublish `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -HostName beaconrelay.company.local `
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" `
  -DatabasePassword '<sqlcipher-password>'
```

You can also include `-ArtifactSourcePath` during upgrade with `-NoPublish` to sync updated artifact files into `AppRoot` before service update.

## 5) Upgrade + Convert Existing Plaintext SQLite To SQLCipher

If the existing DB is plaintext SQLite and must be encrypted.

```powershell
pwsh -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Upgrade `
  -NoPublish `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -HostName beaconrelay.company.local `
  -DatabasePath "C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db" `
  -DatabasePassword '<sqlcipher-password>' `
  -ConvertExistingDatabaseToSqlCipher `
  -DatabaseBackupPath "D:\Backups\beacon-relay-pre-sqlcipher.db"
```

Notes:

- Conversion may require PowerShell 7 (`pwsh`) on some machines.
- You can use `-SkipDatabaseConversionBackup` instead of `-DatabaseBackupPath`.

## 6) Status Check

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Status `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin"
```

## 7) Remove Installation

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Remove `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin"
```

To also remove deployment files under `AppRoot`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 `
  -Mode Remove `
  -AppRoot ".." `
  -ServiceName "Interbit Beacon Relay" `
  -SiteName "BeaconRelay Admin" `
  -PurgeAppRoot
```

Important:

- Do not run `-PurgeAppRoot` from inside `AppRoot` (for example, from `AppRoot\deploy`).
- Run the remove command from a different directory, or run without `-PurgeAppRoot` and delete `AppRoot` afterward.

## 8) Useful Optional Switches

- `-SkipIis` for service-only installs.
- `-SkipFirewall` if firewall is managed separately.
- `-AutoInstallIisProxyModules` to install ARR/URL Rewrite automatically.
- `-CreateSelfSignedCert` optional manual trigger (script now auto-creates by default when needed).
- `-ArtifactSourcePath` to robocopy artifact contents into `AppRoot` automatically when using `-NoPublish`.

## 9) Operations Template (Fill-In Once)

Use this template to avoid repeating long command lines:

```powershell
$common = @{
  ServiceName = 'Interbit Beacon Relay'
  SiteName = 'BeaconRelay Admin'
  HostName = 'beaconrelay.company.local'
  DatabasePath = 'C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db'
  DatabasePassword = '<sqlcipher-password>'
}

# Deploy from artifact (run inside artifact deploy folder)
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 -Mode Deploy -NoPublish -AppRoot '..' @common

# Upgrade from artifact
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 -Mode Upgrade -NoPublish -AppRoot '..' @common

# Remove
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 -Mode Remove -AppRoot '..' @common

# Status
powershell -ExecutionPolicy Bypass -File .\Deploy-BeaconRelay.ps1 -Mode Status -AppRoot '..' @common
```

## 10) Post-Deploy Alert Setup (Admin)

After deploy/upgrade, sign in to the admin UI as an Admin user and configure alert settings:

1. Open `Admin -> Alerts`.
2. Configure CRON monitor heartbeat settings (enable, URL, interval).
3. Configure listener-down email settings (SMTP, from/to, cooldown).
4. Save settings.
5. Validate behavior:
  - While listener is healthy, monitor heartbeat messages are sent.
  - When listener is down/unavailable, monitor messages stop.
  - Listener-down email alert is sent according to cooldown settings.

## 11) Network Prerequisites For Alerts

If alerts are enabled, ensure outbound connectivity from the service host:

- HTTP/HTTPS egress to the configured monitor endpoint.
- SMTP egress to the configured mail host and port.
