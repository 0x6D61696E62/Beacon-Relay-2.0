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

## 8) Useful Optional Switches

- `-SkipIis` for service-only installs.
- `-SkipFirewall` if firewall is managed separately.
- `-AutoInstallIisProxyModules` to install ARR/URL Rewrite automatically.
- `-CreateSelfSignedCert` optional manual trigger (script now auto-creates by default when needed).

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
