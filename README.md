# WinFE Builder

A safe, auditable Windows desktop application that builds a bootable **Windows Forensic Environment
(WinFE)** USB or ISO using the **official WinFE framework**, the **Windows ADK**, the **WinPE
add-on**, **DISM**, and the batch files supplied with the WinFE framework.

> **WinFE Builder orchestrates the official tools.** It does **not** reverse engineer, rewrite,
> modify, or recreate the proprietary WinFE write-protection applications or the internal WinFE
> build process. It runs the official framework batch files and supported Microsoft deployment tools.

---

## Download

Get `WinFEBuilder.exe` from the [**Releases**](../../releases/latest) page.

It is a **self-contained single-file** executable — no .NET runtime, no installer, no dependencies.
Drop it in a folder and run it. On first launch it creates `config\`, `workspace\`, `output\`,
`reports\`, and `logs\` beside itself.

**Verify the download before you use it** (good practice for any forensic tool):

```powershell
Get-FileHash .\WinFEBuilder.exe -Algorithm SHA256
```

Version 1.0.0 must match:

```
3229cbb3371275968d3b86ec2ac07ad2d9da73a9798b035e776522b59479ce00
```

The `SHA256SUMS.txt` attached to the release carries the same value.

---

## Requirements

- **Windows 10 or Windows 11 x64**
- **Administrator privileges** — the app requests elevation via its manifest (DISM and DiskPart
  require it), so expect a UAC prompt. This is normal.
- Windows PowerShell 5.1 (ships with Windows) or PowerShell 7
- The **WinFE framework** (e.g. IntelWinFE), extracted to a folder — not included here, see
  [Licensing and scope](#licensing-and-scope)
- **Windows ADK 1803 or 1809 + the matching WinPE add-on** — read the next section carefully

### ⚠️ You must install ADK 1803 or 1809 — not the latest

> **Install the Windows ADK for Windows 10 version 1803 or 1809, and the matching Windows PE add-on
> of the same version. Do not install the current ADK.**

Colin Ramsden's [build instructions](https://www.winfe.net/build) specify **ADK 1803**
(10.1.17134.x) — *"using any other version may produce unexpected results"* — and his
`MakeWinFEx64-x86.bat` repeats it in the header. **1809** (10.1.17763.x) is the next release and
remains compatible; it was used to build and boot-test the media this tool was verified against.

From **ADK 1903 onward** Microsoft restructured the WinPE payload and the surrounding tooling, and the
framework's batch files no longer produce a working WinFE image — builds either fail outright or,
worse, appear to succeed while producing media that is not correct.

Both downloads must be the **same version**. A current ADK paired with a 1809 WinPE add-on (or the
reverse) is not a supported combination.

- **Windows ADK for Windows 10, version 1809** — <https://go.microsoft.com/fwlink/?linkid=2026036>
- **Windows PE add-on for ADK, version 1809** — <https://go.microsoft.com/fwlink/?linkid=2022233>

Both remain published on Microsoft's ADK archive page (*Other ADK downloads → Previous versions*),
along with 1803: <https://learn.microsoft.com/windows-hardware/get-started/adk-install>

**If a newer ADK is already installed**, uninstall both the ADK and the WinPE add-on before
installing 1803/1809. Side-by-side installs share the `C:\Program Files (x86)\Windows Kits\10` root,
and a leftover newer WinPE payload is a common cause of confusing build failures.

**The app enforces this.** The Dashboard drops the Windows ADK card to **WARNING** when the detected
version is neither 1803 nor 1809, and the Build page **refuses to start** against an incompatible ADK
rather than letting the framework produce bad media. Two deliberate exceptions, since version
detection is best-effort: if the version can't be determined the build proceeds with a warning, and a
compatible kit installed *beside* a newer one counts as compatible with a side-by-side warning.

ADK 1803 reports as **10.1.17134.x**, ADK 1809 as **10.1.17763.x**.

---

## ⚠️ USB writes are always REAL

The USB page performs **actual, destructive writes** to the disk you select. A **red banner** says so
on screen. There is no simulation setting and no way to turn writing off — the tool is built for
operators who intend to write real media, so it does not pretend to.

**Selecting the wrong disk destroys everything on it.** Read the disk identity — model, serial,
capacity — before you confirm, every single time.

What protects a disk is the gate chain below, enforced in the disk service rather than the UI:

1. **Protected-disk rules** block the system disk, the boot disk, and any disk hosting a protected
   volume (Windows, page file, hibernation/crash-dump, the app's own workspace/output, or the source
   framework), plus disks with no unique id, zero/invalid size, or read-only state. Each block shows
   the exact reason. Non-removable disks are hidden unless you tick **Advanced**.
2. **Full disk identity** — number, model, serial, unique id, bus, capacity, partitions, drive
   letters, system/boot flags — is shown for the selected disk.
3. **Typed confirmation** — you must type exactly `ERASE DISK <n>` **and** tick *"I understand that
   all data on this disk will be destroyed."* The Create button stays disabled until both are done.
4. **Identity re-verification immediately before any write** — the disk is re-read and its identity
   signature compared to what you selected. **Any change aborts** the operation, defending against
   disk-number reassignment or a swapped device.
5. Only after every gate passes does it run DiskPart, detect the new drive letter, copy the media
   (preserving UEFI boot files), optionally run `bootsect` if present, validate the copied boot
   structure, and hash critical files.

---

## What it does

| Page | Purpose |
|---|---|
| **Dashboard** | Real environment audit — admin rights, ADK, WinPE add-on, DISM, Oscdimg, PowerShell, disk space, workspace, framework. Clickable status cards with recommended actions. |
| **Framework** | Validate an extracted WinFE framework, list discovered scripts/components with SHA-256 hashes, then copy it into a timestamped, hashed workspace **without ever modifying the original**. |
| **Tools and Drivers** | Add portable forensic tools to the workspace; inject `.inf` drivers into `boot.wim` via DISM. |
| **Build** | Run the official framework batch files, verify the boot structure, inspect `boot.wim` read-only with DISM (architecture, image count, size, SHA-256 — never mounted), build and hash the ISO. |
| **USB** | Safe disk targeting and USB creation, with the gate chain above. |
| **Wallpaper** | Set the WinFE desktop wallpaper for the next build. |
| **Validation** | Guided manual checklist → one-click **HTML** report. |
| **Settings** | Read-only summary and build-profile list. |

Every operation returns a structured result (status, message, technical detail, exit code, timing,
output paths, warnings, recommended action), and everything is logged twice: a human-readable `.log`
and a structured `.jsonl` under `logs\`.

### Build success is not forensic validation

The app keeps these strictly separate and **never auto-sets the forensic states**. `Boot Test` and
`Write-Protection Test` always report **NOT TESTED** until a human records the result on the
Validation page.

**Write protection comes from the official framework's registry patches and applications, not from
WinFE Builder.** A successful build is not a write-protection guarantee. Validate every piece of
media against a disposable target before casework.

### Third-party tools may need a runtime

WinFE Builder copies the tools you select onto the media; it does not supply their runtimes. WinPE
ships with neither .NET Framework nor .NET, so:

- Tools needing **.NET Framework 4.x** (e.g. FTK Imager) require the **Prepare Windows components
  (.NET Framework, WMI)** option at build time, which DISM-installs `WinPE-NetFx`, `WinPE-WMI` and
  `WinPE-Scripting` into `boot.wim`. Without it they fail with *"mscoree.dll was not found"*.
- Tools needing **modern .NET** — 5/6/8/9/10, identified by a `runtimeconfig.json` beside the `.exe`
  — are **not** covered by that option. Microsoft publishes no WinPE component for modern .NET, so the
  tool must carry its own runtime. Place `hostfxr.dll` and `shared\Microsoft.NETCore.App\<version>\`
  in the tool's own folder and the `.exe` runs with no launcher; the version must match the one named
  in its `runtimeconfig.json`, or you get *"You must install or update .NET to run this application"*.
- Tools with a **kernel driver** (e.g. Arsenal Image Mounter) need the driver injected into
  `boot.wim` via the Tools and Drivers page — copying files onto finished media is not enough.

Adding Windows components rewrites parts of the offline registry, so the build re-applies the
framework's write-protection patches afterwards and warns if it cannot. Re-verify write protection on
any image that gained components.

---

## How it was verified

Built and exercised end-to-end against a real IntelWinFE framework on a machine with ADK 1809
installed:

- **Real media and ISO builds** — the official batch files produce bootable `WINFE_*.iso` artifacts
  with hashed build manifests.
- **Real USB writes** — DiskPart preparation, drive-letter detection, media copy, and offline
  structural validation completed against a physical removable disk (30 GB target; 363 files /
  ~1.2 GB for the combined x86-x64 layout), with a `usb-record_*.json` written per run.
- **Boot test** — the produced x86+x64 media booted successfully.
- **260 automated tests** cover path validation, framework validation, ADK detection and the
  1803/1809 version rule, SHA-256 hashing against NIST vectors, workspace/manifest generation, DISM output
  parsing, the `ERASE DISK <n>` phrase validator, protected-disk rules, and release defaults. No
  destructive test ever runs automatically.

### Known limitations

- The **ADK version gate depends on version detection succeeding.** If no version can be read from
  `Windows Kits\10\bin\<version>` or the ADK uninstall entry, the result is *unknown* and the build
  proceeds with a warning rather than being blocked. Confirm the release yourself in that case.
- Build **stage rows** populate when the build finishes; live progress during a long build appears in
  the log panel, not incrementally in the stage table.
- Disk enumeration uses the WMI Storage namespace and requires Administrator rights.
- ADK **version** detection is best-effort (Windows Kits `bin\<version>` folders, falling back to the
  ADK uninstall entry).

---

## Licensing and scope

WinFE Builder is released under the [MIT License](LICENSE).

This repository does **not** contain or redistribute:

- the **WinFE framework** (including IntelWinFE) or any of its batch files, write-protection
  applications, or components;
- the **Windows ADK**, the **WinPE add-on**, `DISM`, `oscdimg`, `bootsect`, or any other Microsoft
  component;
- any third-party forensic tools or drivers you choose to add to a build.

Those remain the property of their respective owners and are subject to their own licenses and terms.
You must obtain them yourself and comply with those terms. WinFE Builder only orchestrates the
official tools already installed on your machine.

**No warranty.** This software is provided "as is". You are responsible for validating any media you
produce before relying on it — see [LICENSE](LICENSE).
