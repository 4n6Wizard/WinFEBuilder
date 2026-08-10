# WinFE Builder — building from source

> For downloading and using the released executable, see the [README](../README.md). This document
> covers building, running, and testing the solution yourself.

A safe, auditable Windows desktop application that simplifies building a bootable **Windows
Forensic Environment (WinFE)** USB using the **official WinFE framework**, the **Windows ADK**,
the **WinPE add-on**, **DISM**, and the batch files supplied with the WinFE framework.

> **WinFE Builder orchestrates the official tools.** It does **not** reverse engineer, rewrite,
> modify, or recreate the proprietary WinFE write-protection applications or the internal WinFE
> build process. It runs the official framework batch files and supported Microsoft deployment tools.

---

## Milestone status

This repository implements **all five milestones (1–5)**.

| Page                | Status                                                          |
|---------------------|-----------------------------------------------------------------|
| **Dashboard**       | ✅ Implemented (real environment audit)                          |
| **Framework**       | ✅ Implemented (validate + copy-to-workspace + manifest + Explorer) |
| **Build**           | ✅ Implemented (run official batch files, verify media/boot.wim/ISO) |
| **USB**             | ✅ Implemented (safe disk targeting, simulation-first USB creation) |
| **Tools and Drivers** | ✅ Implemented (portable tools → workspace; .inf → boot.wim via DISM) |
| **Wallpaper**       | ✅ Implemented (sets the WinFE desktop wallpaper for the next Build) |
| **Validation**      | ✅ Implemented (guided manual checklist; one-click **HTML** report) |
| Settings            | Read-only summary + build-profile list                          |

> This project was built end-to-end against a real IntelWinFE framework: an x86+x64 WinFE USB was
> successfully produced and boot-tested. Several real bugs were found and fixed along the way
> (empty-directory copy, combined multi-arch media detection, over-sensitive DISM-error warning).

### Verified on real hardware

Every stage has now been exercised for real on a machine with the Windows ADK and WinPE add-on
installed — not just in simulation:

- **Real media + ISO builds**: the official framework batch files were run through the Build page,
  producing bootable `WINFE_10x86-x64_*.iso` artifacts with hashed build manifests.
- **Real USB writes**: DiskPart preparation, drive-letter detection,
  media copy, and offline structural validation all completed against a physical removable disk
  (a 30 GB USB target; 363 files / ~1.2 GB copied for the combined `x86-x64` layout), with a
  `usb-record_*.json` written for each run.
- **Boot test**: the produced x86+x64 media was booted successfully.

Write-protection remains a **human-verified** step — see *Known limitations*.

Reporting is not a separate page: the **Validation** page generates the final **HTML** report in one
step from the recorded checklist (there is no report/validation JSON side-file).

---

## Requirements

- Windows 10 or Windows 11 **x64**
- **.NET 8 SDK** to build. Published builds are **self-contained** (single-file, x64), so the target machine needs **no** .NET runtime installed.
- Windows PowerShell 5.1 (ships with Windows) and/or PowerShell 7
- For actual WinFE builds: the **Windows ADK version 1803 or 1809** and the **matching WinPE add-on
  of the same version** — see the version warning below. **Newer ADK releases do not work.**

The application requests **Administrator privileges** via its manifest (required by DISM/DiskPart
in later milestones).

### ⚠️ You must install ADK **1803 or 1809** — not the latest

> **Install the Windows ADK for Windows 10 version 1803 or 1809, and the matching Windows PE add-on
> of the same version. Do not install the current ADK.**

Colin Ramsden's [build instructions](https://www.winfe.net/build) specify **ADK 1803**
(10.1.17134.x) — *"using any other version may produce unexpected results"* — and
`MakeWinFEx64-x86.bat` repeats it in its header. **1809** (10.1.17763.x) is the next release and
remains compatible; it has been used here to build and boot-test working media.

From **ADK 1903 onward** Microsoft restructured the WinPE payload and the surrounding tooling, and the
framework's batch files no longer produce a working WinFE image — builds either fail outright or,
worse, appear to succeed while producing media that is not correct.

Both downloads must be the **same version**. A current ADK paired with a 1809 WinPE add-on (or the
reverse) is not a supported combination.

- **Windows ADK for Windows 10, version 1809** — <https://go.microsoft.com/fwlink/?linkid=2026036>
- **Windows PE add-on for ADK, version 1809** — <https://go.microsoft.com/fwlink/?linkid=2022233>

Both are still published on Microsoft's ADK archive page (*Other ADK downloads → Previous versions*),
along with 1803: <https://learn.microsoft.com/windows-hardware/get-started/adk-install>

**If you already have a newer ADK installed**, uninstall both the ADK and the WinPE add-on before
installing 1803/1809. Side-by-side installs share the `C:\Program Files (x86)\Windows Kits\10` root,
and a leftover newer WinPE payload is a common cause of confusing build failures.

**WinFE Builder enforces this.** The **Dashboard** audit reports the detected version and drops the
Windows ADK card to **WARNING** when it is neither 1803 nor 1809, with the download links as the
recommended action. The **Build** page then **refuses to start** against an incompatible ADK rather
than letting the framework batch files produce bad media.

Two deliberate exceptions, because version detection is best-effort and must not block a valid setup:

- If the version **cannot be determined**, the build proceeds and records a warning — it is never a
  hard block.
- If a **compatible kit is installed alongside a newer one**, that counts as compatible (the required
  payload is present) and you get a side-by-side warning instead of a refusal. 1803 and 1809 together
  is not treated as a mixed install — both work.

ADK 1803 reports as **10.1.17134.x**, ADK 1809 as **10.1.17763.x**.

---

## Build instructions

```powershell
# From the solution root:
cd <path-to>\WinFE_Builder

# Fresh clone only — config\settings.json is git-ignored (it holds machine-local paths):
New-Item -ItemType Directory -Force config | Out-Null
Copy-Item config-template\settings.json config\settings.json

# If nuget.org is not already a source on this machine:
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org

dotnet restore WinFEBuilder.sln
dotnet build   WinFEBuilder.sln -c Debug
```

Build the app only:

```powershell
dotnet build src\WinFEBuilder.App\WinFEBuilder.App.csproj -c Release
```

Publish the release artifact (self-contained, single-file):

```powershell
dotnet publish src\WinFEBuilder.App\WinFEBuilder.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Ship only `WinFEBuilder.exe`, a clean `config\settings.json`, and `SHA256SUMS.txt`. The app creates
its `workspace\`, `output\`, `reports\`, and `logs\` folders beside the exe on first run — do not
bundle those in the distributable.

### Verified build result on the development machine

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

(Built with .NET SDK 8.0.423, both `Debug|x64` and `Release|x64`.)

---

## Run instructions

```powershell
# Debug build output:
.\src\WinFEBuilder.App\bin\x64\Debug\net8.0-windows\win-x64\WinFEBuilder.exe
```

Because the app manifest requests `requireAdministrator`, launching it triggers a UAC prompt —
this is expected. On first run:

1. The **Dashboard** auto-runs the environment audit and shows status cards (PASS / WARNING /
   FAIL / NOT CONFIGURED). Click any card for full details and a recommended action.
2. Open **Framework**, browse to an extracted WinFE framework folder, click **Validate
   Framework**, review the discovered scripts/components and their SHA-256 hashes, then click
   **Copy Framework to Workspace** to create a timestamped, hashed copy with a manifest.

Logs are written under `logs\` (both a human-readable `.log` and a structured `.jsonl`).

---

## Test instructions

```powershell
dotnet test WinFEBuilder.sln -c Debug
```

### Verified test result on the development machine

```
Passed!  - Failed: 0, Passed: 250, Skipped: 0, Total: 250
```

Unit tests cover: path validation, framework validation heuristics, ADK detection (safe smoke
test), SHA-256 hashing (against NIST vectors), workspace/manifest generation, framework
validate + copy (verifying the original is untouched), and the destructive-confirmation-phrase
validator (`ERASE DISK <n>`).

**No destructive USB tests run automatically.** DEBUG builds force simulation mode on, and the
suite asserts that no process is ever started while simulating.

---

## Using the Build page (Milestone 2)

1. First select and **Validate** a framework on the **Framework** page (this stores it as the
   current framework).
2. Open **Build** and click **Refresh scripts** — the media and ISO batch files are auto-detected
   and pre-selected (you can override either). Set a per-batch **timeout** and optionally **Skip ISO**.
3. Click **Start Build**. The workflow:
   - re-runs the environment audit and **stops with guidance** if the ADK/WinPE add-on is missing;
   - revalidates the framework, creates a fresh timestamped workspace, and copies the framework in;
   - runs the official **media build** batch (via `cmd /c`, hidden window, stdin closed so `pause`
     prompts don't hang), streaming output to the live log;
   - verifies the boot structure (`Boot`, `EFI`, `Sources`, `Sources\boot.wim`) and inspects
     `boot.wim` **read-only** with DISM (architecture, image count/name, size, SHA-256) — it never
     mounts the WIM;
   - runs the official **ISO build** batch, locates the newest non-empty `.iso`, hashes it, and
     copies it to the output directory;
   - writes `build-manifest.json` + `build-report.txt` into the workspace.
4. The **Result summary** keeps build success separate from forensic validation:
   `Boot Test` and `Write-Protection Test` always show **NOT TESTED** (those are manual, Milestone 5).

> The actual build requires the **Windows ADK 1803 or 1809 + the matching WinPE add-on** to be
> installed — see [the ADK version warning](#️-you-must-install-adk-1803-or-1809--not-the-latest). The
> preflight stops with clear guidance in both bad cases: no ADK at all, **and** an ADK of the wrong
> version (1903+), which is refused before any batch file runs.

## Using the USB page (Milestone 3) — READ THIS

> ### ⚠️ USB writes are always REAL
>
> The USB page performs **actual, destructive writes** to the disk you select. A **red banner** says
> so on screen. There is no simulation setting and no way to turn writing off — the tool is built for
> operators who intend to write real media, so it does not pretend to.
>
> **Selecting the wrong disk destroys everything on it.** Read the disk identity — model, serial,
> capacity — before you confirm, every single time.

There is no operator-facing simulation mode. It used to be a `SimulationMode` option in
`settings.json`, defaulting to on, which made the tool look broken for the people most likely to use
it: nothing happened and a fake disk #99 appeared. The setting is gone — it is `[JsonIgnore]`d out of
`settings.json`, and a leftover `"SimulationMode": true` in an upgraded config is ignored.

**DEBUG builds still never write.** `SettingsService` forces the internal guard on under `#if DEBUG`,
so running from an IDE cannot erase a disk; the banner reads *DEBUG BUILD — simulated* and a demo disk
(#99) appears for walking the flow. Release builds — which is what ships — always write for real.

What actually stands between you and a wiped disk is the gate chain below, which is enforced in
`DiskService` rather than the UI.

Safety gates (all enforced in `DiskService`, not just the UI):

1. **Protected-disk rules** block the system disk, boot disk, and any disk hosting a protected
   volume (Windows, page file, hibernation/crash-dump, the app workspace/output, or the source
   framework), plus disks with no `UniqueId`, zero/invalid size, or read-only state. Each block
   shows the exact reason. Non-removable disks are hidden unless you tick **Advanced**.
2. **Full disk identity** (number, model, serial, unique id, bus, size, partitions, letters,
   system/boot flags) is shown for the selected disk.
3. **Typed confirmation**: you must type exactly `ERASE DISK <n>` **and** tick
   *"I understand that all data on this disk will be destroyed."* The Create button stays disabled
   until both are satisfied.
4. **Immediately before any write**, the disk is re-read and its identity signature is compared to
   what you selected; **any change aborts** the operation (defends against disk-number reassignment
   or a swapped device).
5. Only when every gate passes does it run DiskPart, detect the new drive
   letter, copy the media (preserving UEFI boot files), optionally run `bootsect` (only if present),
   validate the copied boot structure, and hash critical files. It reports:
   `USB Creation / Boot Structure / Offline Structural Validation` as PASS/FAIL and
   `Boot Test / Write-Protection Test` as **NOT TESTED** (manual, Milestone 5).

Notes for developers, so behavior is never a surprise:

- **DEBUG builds never write to a disk** — `SettingsService` forces the internal simulation guard on
  under `#if DEBUG`, so running from Visual Studio can't erase one. Release builds always write.
- **There is no config switch for it.** `AppSettings.SimulationMode` is `[JsonIgnore]`d, so it is
  neither written to nor read from `settings.json`. Tests pin this: a legacy config containing
  `"SimulationMode": true` deserializes to `false`, so operators upgrading from 1.0.0 don't end up with
  a tool that silently refuses to write.
- **A missing or corrupt `settings.json` is normal, not an edge case** — the released exe ships without
  one, so first run always falls back to `AppSettings` defaults. Those defaults must therefore be
  usable as-is; `ReleaseDefaultsTests` pins them.

## Interface description

```
┌────────────┬────────────────────────────────────────────────────────────┐
│ WinFE      │  Dashboard                                                   │
│ Builder    │  Environment audit — verifies tools & prerequisites.         │
│            │  [ Run Environment Audit ] [Cancel]  ▮▮▮   Overall: PASS     │
│ Dashboard  │  ┌───────────┐ ┌───────────┐ ┌───────────┐                  │
│ Framework  │  │ PASS      │ │ WARNING   │ │ FAIL      │  … status cards   │
│ Tools …    │  │ Admin     │ │ WinPE …   │ │ Oscdimg   │                  │
│ Build      │  └───────────┘ └───────────┘ └───────────┘                  │
│ USB        │  ──────────────────────────────────────────────────────    │
│ Validation │  Live log                                                    │
│ Wallpaper  │  14:30:02 [INFO] Environment audit started.                 │
│ Settings   │  14:30:03 [PASS] Administrator privileges detected.         │
│            │  14:30:05 [FAIL] WinPE add-on not found.                    │
│ v1.0 · M1  │                                                              │
└────────────┴────────────────────────────────────────────────────────────┘
```

- **Left navigation** with a selected-item highlight; **status cards** colored by state; a
  **marquee progress bar** and **Cancel** button for long-running operations; a dark **live log**
  panel; clickable cards open an **expandable details dialog** with recommended actions.
- Neutral, professional styling. Per-Monitor-V2 High-DPI, DPI auto-scaling, resizable, keyboard-
  navigable, accessible names on interactive controls.

---

## Design notes (how the rules are honored)

- **Success is validated, never assumed.** Every operation returns a structured `OperationResult`
  (success, status, message, technical detail, exception, exit code, start/finish/duration,
  output paths, warnings, recommended action).
- **Build success ≠ forensic validation.** `ValidationStatus` separates *Build Successful*,
  *Boot Structure Validated*, *Boot Test Passed*, *Write-Protection Test Passed*, and
  *Organization Approved*. The application never auto-sets the operational/forensic states.
- **Safe process execution.** `ProcessRunner` uses `UseShellExecute = false`, redirected
  stdout/stderr, hidden window, explicit working directory, and an **argument list** (never a
  concatenated shell string), capturing exit code and timing.
- **Path safety.** All paths flow through `PathValidator` (absolute-path checks, invalid-char
  rejection, quoting, containment checks).
- **The original framework is never modified** — it is copied into a timestamped workspace with a
  hashed manifest.
- **Dual logging.** Human-readable `.log` + structured `.jsonl` (timestamp, severity, operation,
  message, command, exit code, duration, related path, disk identity slot, exception).
- **DI throughout**; **UI logic is separated** from build/disk logic (Core has zero WinForms
  dependency).
- **Destructive-disk groundwork is already conservative:** the `ERASE DISK <n>` phrase validator
  is implemented and tested now, and `Initialize-WinFEUsb.ps1` is **simulation-first** and refuses
  system/boot disks — full USB safety lands in Milestone 3.

---

## Known limitations

- Build **stage rows** populate when the build completes; **real-time** progress during a long build
  is shown in the **live log** panel (not incrementally in the stage table).
- **Write-protection is not verified by this application.** The WinFE write-protection behavior comes
  from the official framework's registry patches and applications, not from WinFE Builder. The
  `Write-Protection Test` state is **only ever set by a human** on the Validation page, after testing
  the booted media against a disposable target. Do not treat a successful build as a write-protection
  guarantee — validate every piece of media before casework.
- Disk enumeration uses the WMI Storage namespace (`root\Microsoft\Windows\Storage`) and requires
  Administrator rights (already requested by the manifest).
- ADK **version** detection is best-effort (from Windows Kits `bin\<version>` folders, falling
  back to the ADK uninstall entry); if neither is present, version shows as *unknown* while
  detection of DISM/Oscdimg/WinPE still works.
- **The ADK version gate depends on version detection succeeding.** An incompatible ADK is refused at
  Build preflight and warned about on the Dashboard, but if no version can be read from
  `Windows Kits\10\bin\<version>` or the uninstall entry, the result is *unknown* and the build is
  allowed to proceed with a warning rather than blocked. In that case confirm the release yourself —
  see [the ADK version warning](#️-you-must-install-adk-1803-or-1809--not-the-latest).
- ADK detection is **not unit-tested against a fixed installed version** (it is environment-
  dependent); the test verifies the detector runs safely and returns an internally consistent
  result.
- The GUI was **not launched headlessly** during automated verification (the manifest forces a
  UAC elevation prompt); it was verified by a clean compile of the WinForms project plus the
  Core unit tests. Interactive launch is expected to work on a normal desktop session.
- PowerShell scripts under `src\WinFEBuilder.PowerShell\Scripts` are included and functional/guarded;
  their end-to-end behavior (real WinFE build, real USB write) requires the ADK and a target disk.

---

## Solution layout

See [docs/FILES.md](docs/FILES.md) for a file-by-file summary.

---

## License

[MIT](LICENSE) — for the WinFE Builder source, scripts, and documentation in this repository only.

This repository does **not** contain or redistribute the WinFE framework (including IntelWinFE),
the Windows ADK / WinPE add-on, any Microsoft deployment binaries, or any third-party forensic
tools or drivers. You must obtain those yourself and comply with their own licenses. WinFE Builder
orchestrates the official tools already installed on your machine — see the scope note in
[LICENSE](LICENSE).
