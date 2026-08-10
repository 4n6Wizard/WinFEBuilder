# File-by-file summary

## Solution / config

| File | Purpose |
|------|---------|
| `WinFEBuilder.sln` | Solution referencing Core, App, and Tests (Debug/Release, x64). |
| `config/settings.json` | Default app settings (workspace/output/log roots, min free space, simulation mode, preferred PowerShell). |
| `config/build-profiles.json` | Seed build profiles (Agency Standard, UEFI Only, Legacy BIOS). Disk numbers are never stored. |
| `.gitignore` | Excludes build output and runtime artifacts. |
| `README.md` | Overview, build/test/run instructions, interface description, known limitations. |

## WinFEBuilder.Core (no WinForms dependency)

| File | Purpose |
|------|---------|
| `WinFEBuilder.Core.csproj` | net8.0-windows library; DI + System.Management packages. |
| `CoreServiceRegistration.cs` | `AddWinFeBuilderCore()` — central DI wiring. |
| `Models/CheckStatus.cs` | PASS / WARNING / FAIL / NOT CONFIGURED. |
| `Models/ValidationStatus.cs` | Build vs. forensic states (Build Successful … Organization Approved / Not Tested). |
| `Models/OperationResult.cs` | Structured result (success, status, timing, exit code, outputs, warnings, recommended action). |
| `Models/AuditItem.cs` | One dashboard audit line (name, status, summary, details, action). |
| `Models/EnvironmentAuditResult.cs` | Aggregate audit result + overall roll-up. |
| `Models/AdkInstallation.cs` | Detected ADK/WinPE paths, version, architectures, warnings. |
| `Models/FileHashEntry.cs` | SHA-256 record for a file. |
| `Models/FrameworkValidationResult.cs` | Framework validation output + `DiscoveredFile`. |
| `Models/WorkspaceManifest.cs` | Workspace manifest + `FrameworkMetadata`. |
| `Logging/LogEntry.cs` | Structured log entry + `LogSeverity`. |
| `Logging/ILogService.cs` / `LogService.cs` | Dual text + JSONL logger with a live `EntryLogged` event. |
| `Hashing/IHashService.cs` / `HashService.cs` | Streaming SHA-256 (file, entry, and byte-buffer). |
| `Validation/PathValidator.cs` | Absolute-path validation, invalid-char rejection, quoting, containment checks. |
| `Validation/FrameworkValidator.cs` | Pure (IO-free) heuristics: build-script/component/config classification, double-nesting, x64 hints. |
| `Validation/ConfirmationPhraseValidator.cs` | Exact `ERASE DISK <n>` phrase validation (used in Milestone 3). |
| `Configuration/AppSettings.cs` | Settings model. |
| `Configuration/BuildProfile.cs` | Profile model (no disk numbers). |
| `Configuration/ISettingsService.cs` / `SettingsService.cs` | Load/save settings; forces simulation mode ON in DEBUG. |
| `Configuration/AppPaths.cs` | Resolves config/logs/workspace/output/reports/scripts folders. |
| `Services/ProcessRunResult.cs` | Captured stdout/stderr/exit/timing of an external process. |
| `Services/IProcessRunner.cs` / `ProcessRunner.cs` | Safe process + PowerShell-script runner (argument list, hidden window, cancellation, timeout). |
| `Services/IAdkDetectionService.cs` / `AdkDetectionService.cs` | Registry + Program Files + env-var ADK/WinPE/DISM/Oscdimg detection; version discovery. |
| `Services/IEnvironmentService.cs` / `EnvironmentService.cs` | Builds the full dashboard audit (admin, arch, .NET, PowerShell, ADK, WinPE, DISM, Oscdimg, temp space, workspace, framework). |
| `Services/IWorkspaceService.cs` / `WorkspaceService.cs` | Timestamped workspace creation + manifest writing. |
| `Services/IFrameworkService.cs` / `FrameworkService.cs` | Framework validation, hashing, and copy-to-workspace (original untouched). |

## WinFEBuilder.App (WinForms)

| File | Purpose |
|------|---------|
| `WinFEBuilder.App.csproj` | net8.0-windows WinForms exe; Per-Monitor-V2 DPI; references Core. |
| `app.manifest` | `requireAdministrator`, Windows 10/11 compatibility. |
| `Program.cs` | High-DPI setup, DI container, global exception handlers, launches `MainForm`. |
| `UiTheme.cs` | Neutral palette, status colors/text, fonts. |
| `Forms/MainForm.cs` | Left navigation, page host, lazy page creation, settings summary. |
| `Controls/StatusCard.cs` | Clickable status card with colored badge; raises details request. |
| `Controls/DetailsDialog.cs` | Modal details + recommended action for an audit item. |
| `Controls/LiveLogPanel.cs` | Live, color-coded log view bound to `ILogService`. |
| `Controls/DashboardPage.cs` | Run Environment Audit, status-card grid, live log, cancel/progress. |
| `Controls/FrameworkPage.cs` | Browse/validate framework, file+hash list, warnings, copy-to-workspace. |
| `Controls/PlaceholderPage.cs` | Informational page for not-yet-implemented milestones (no fake buttons). |
| `ViewModels/DashboardViewModel.cs` | UI-agnostic wrapper over the environment audit. |
| `ViewModels/FrameworkViewModel.cs` | UI-agnostic wrapper over framework validate/copy. |

## WinFEBuilder.PowerShell/Scripts

| File | Milestone | Purpose |
|------|-----------|---------|
| `Get-ADKInstallation.ps1` | 1 | Read-only ADK/WinPE detection → JSON. |
| `Test-WinFEEnvironment.ps1` | 1 | Read-only environment audit → JSON. |
| `Test-WinFEFramework.ps1` | 1 | Read-only framework validation (+ hashes) → JSON. |
| `Build-WinFEMedia.ps1` | 2 | Runs an official build .bat with captured output. |
| `Build-WinFEIso.ps1` | 2 | Runs the official ISO .bat, locates + hashes the ISO. |
| `Get-RemovableDisk.ps1` | 3 | Read-only removable-disk enumeration with identity + system/boot flags. |
| `Initialize-WinFEUsb.ps1` | 3 | **Simulation-first, guarded** DiskPart preparation (refuses system/boot disks; requires exact confirm phrase to execute). |
| `Test-WinFEUsb.ps1` | 3 | Read-only post-copy boot-structure validation (marks boot/write-protection as NOT TESTED). |
| `Dismount-WinFEWorkspace.ps1` | 4 | DISM mounted-image cleanup / unmount. |

## Milestone 2 additions (WinFE media + ISO build)

| File | Purpose |
|------|---------|
| `Core/Models/WimInfo.cs` | boot.wim info from DISM (arch, images, size, SHA-256) + `WimImage`. |
| `Core/Models/MediaValidationResult.cs` | Boot-structure check + boot.wim inspection result. |
| `Core/Models/IsoValidationResult.cs` | ISO located/validated/copied/hashed result. |
| `Core/Models/BuildResult.cs` | End-to-end build result + `BuildStage` rows. |
| `Core/Models/BuildManifest.cs` | Auditable build manifest (commands, exit codes, hashes, statuses). |
| `Core/Validation/DismOutputParser.cs` | Pure parser for `dism /Get-WimInfo` output. |
| `Core/Validation/BuildScriptSelector.cs` | Pure media-vs-ISO script selection heuristics. |
| `Core/Validation/MediaLocator.cs` | Pure boot.wim / media-root / newest-ISO location helpers. |
| `Core/Services/IDismService.cs` / `DismService.cs` | Read-only boot.wim inspection via DISM (no mount). |
| `Core/Services/IBuildService.cs` / `BuildService.cs` | Full build orchestration + `BuildRequest`. |
| `Core/Services/ProcessRunner.cs` | Extended: `RunBatchFileAsync` (cmd /c, stdin closed) + `closeStandardInput`. |
| `App/ViewModels/BuildViewModel.cs` | UI-agnostic wrapper over build + script discovery. |
| `App/Controls/BuildPage.cs` | Build UI: script pickers, options, stage table, result summary, live log, cancel. |

## Milestone 3 additions (safe USB creation)

| File | Purpose |
|------|---------|
| `Core/Models/DiskInfo.cs` | Full disk identity + state + stable identity signature. |
| `Core/Models/DiskEligibility.cs` | Eligibility result + `ProtectedContext` (protected volumes). |
| `Core/Models/UsbCreationResult.cs` | USB result + status lines (build vs. manual forensic). |
| `Core/Models/UsbBuildRequest.cs` | USB inputs (disk, media, label, phrase, acknowledgement). |
| `Core/Validation/DiskEligibilityRules.cs` | Pure protected-disk rules (system/boot/protected/uniqueid/size/removable). |
| `Core/Validation/DiskPartScriptBuilder.cs` | Pure standard DiskPart layout + label sanitizer. |
| `Core/Validation/DiskIdentity.cs` | Identity match + diff for pre-write re-verification. |
| `Core/Services/IDiskService.cs` / `DiskService.cs` | WMI Storage enumeration, protected context, simulation-first guarded USB creation. |
| `App/ViewModels/UsbViewModel.cs` | UI-agnostic wrapper + media auto-detect. |
| `App/Controls/UsbPage.cs` | USB UI: simulation banner, disk list, identity panel, confirm phrase + checkbox, results. |

## Milestone 5 additions (validation records, reports, profiles)

| File | Purpose |
|------|---------|
| `Core/Models/ValidationRecord.cs` | Human-entered manual test record + `ManualCheck` tri-state; derived `WriteProtectionVerified` / `BootVerified`. |
| `Core/Models/UsbRecord.cs` | Persisted USB-creation record (disk identity, statuses, critical hashes). |
| `Core/Reports/ReportModel.cs` | Aggregate report model; forensic statuses reflect only recorded data. |
| `Core/Reports/IReportService.cs` / `ReportService.cs` | Generate the **HTML** report from a build manifest + an in-memory validation record; never auto-marks boot/write-protection. No JSON side-file. |
| `Core/Configuration/IProfileService.cs` / `ProfileService.cs` | Load/save build profiles, seed defaults; self-heal stale absolute paths; no disk numbers. |
| `Core/Services/DiskService.cs` | Extended: writes a `usb-record_*.json` after a successful USB creation. |
| `App/ViewModels/ValidationViewModel.cs` | UI-agnostic wrapper; generates the report from the latest build + entered record. |
| `App/Controls/ValidationPage.cs` | Guided manual-validation checklist; **Generate Report** builds and opens the HTML report in one step. |

## Milestone 4 additions (tools & driver injection)

| File | Purpose |
|------|---------|
| `Core/Models/DriverInfo.cs` | Discovered .inf + detected architectures/class/provider/compatibility. |
| `Core/Models/DriverInjectionResult.cs` | Per-driver + session injection outcome (mount/commit state, hashes, revalidation). |
| `Core/Validation/InfParser.cs` | Pure .inf parsing: architecture, class, provider, compatibility. |
| `Core/Services/IToolService.cs` / `ToolService.cs` | Resolve the framework tools folder; copy/list/remove portable tools baked into the build. |
| `Core/Services/IDriverService.cs` / `DriverService.cs` | Enumerate .inf; DISM mount → add-driver → commit → unmount with guaranteed cleanup; rehash + revalidate. |
| `App/ViewModels/ToolsAndDriversViewModel.cs` | UI-agnostic wrapper. |
| `App/Controls/ToolsAndDriversPage.cs` | Tools list (add/remove/copy) + driver list (scan/select/inject) + mount check/cleanup + live log. |

## tests/WinFEBuilder.Tests (xUnit, 210 tests)

| File | Coverage |
|------|----------|
| `TempDir.cs` | Disposable temp-dir helper. |
| `PathValidatorTests.cs` | Absolute-path acceptance/rejection, invalid chars, quoting, containment, missing-file. |
| `ConfirmationPhraseValidatorTests.cs` | Exact `ERASE DISK <n>` matching (case, spacing, wrong number). |
| `FrameworkValidatorTests.cs` | Build-script/component classification, double-nesting, x64 hints. |
| `HashServiceTests.cs` | SHA-256 NIST vectors + file/entry hashing. |
| `WorkspaceAndManifestTests.cs` | Workspace naming, creation, manifest JSON round-trip. |
| `FrameworkServiceTests.cs` | Valid/invalid/zero-byte/double-nested validation; copy creates manifest and leaves the original untouched. |
| `AdkDetectionTests.cs` | Safe smoke test — detector runs and returns a consistent result. |
| `DismOutputParserTests.cs` | Parsing DISM /Get-WimInfo: index/name/description/size, architecture, success line. |
| `BuildScriptSelectorTests.cs` | Media-vs-ISO script selection + fallbacks. |
| `MediaLocatorTests.cs` | boot.wim discovery, media root, newest non-empty ISO selection. |
| `BuildServiceMediaTests.cs` | Media-structure completeness rules + build preflight blocks when ADK missing (stubs, no ADK needed). |
| `DiskEligibilityRulesTests.cs` | Protected-disk rules: system/boot/protected-volume/uniqueid/size/read-only/removable/simulated. |
| `DiskPartAndIdentityTests.cs` | Standard DiskPart script, label sanitize, identity match/diff. |
| `DiskServiceSimulationTests.cs` | Simulation generates script but never starts a process; phrase/ack/media guards reject. |
| `ValidationRecordTests.cs` | `WriteProtectionVerified` / `BootVerified` derivations. |
| `ProfileServiceTests.cs` | Default seeding; save/get/delete; no disk-number field. |
| `ReportServiceTests.cs` | Report generation; forensic statuses never fabricated without a validation record. |
| `InfParserTests.cs` | .inf architecture/class/provider parsing + compatibility. |
| `ToolAndDriverServiceTests.cs` | Tool analyze/copy/definitions round-trip; driver enumeration + arch compatibility. |
