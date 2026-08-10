# WinFE USB — Validation Guide

This guide tells you **how to perform** each test on the Validation page and **where the answer
comes from**, so you can fill in the checklist with real, defensible results.

> **Golden rule:** validate the tool on **throwaway / non-evidence** media only. This proves the
> *WinFE build* is sound. Never do first-time validation against real evidence.

---

## What you need before you start

| Item | Purpose |
|------|---------|
| The WinFE USB you built | The thing under test |
| A **test computer** (spare PC or a VM) | To boot WinFE — not an evidence machine |
| A **test source disk** (spare HDD/SSD/USB with some files on it) | To prove write-protection; it will *not* be modified if WinFE works |
| A **destination USB** (empty external drive) | To confirm WinFE detects a writable destination |
| **FTK Imager** (free) or your imaging tool | To hash the test source disk before/after boot. Put a portable copy on a second USB |
| A way to hash a disk | FTK Imager can hash a physical device; for files you can use Windows `Get-FileHash` |

---

## Step 0 — Record identity (before testing)
On the Validation page fill:
- **Build reference** = the workspace folder or ISO name (e.g. `Build_2026-07-21_131917`)
- **USB serial number** = the stick's serial (WinFE Builder showed it on the USB page; e.g. Transcend `212308251258598X`)
- **Examiner name** and **Test date**

---

## Step 1 — Boot test (UEFI)  → *"Booted successfully in UEFI"*
1. Plug the WinFE USB into the test computer.
2. Power on and open the **boot menu** (commonly **F12 / F10 / Esc / F9** — varies by maker).
3. Choose the USB entry listed under **UEFI** (not "Legacy"/"CSM").
4. If WinFE loads to its desktop → set **Booted successfully in UEFI = Pass**.
   - If it doesn't appear, enable **UEFI boot** and disable **Secure Boot** in firmware, then retry.

## Step 2 — Boot test (Legacy BIOS)  → *"Booted successfully in legacy BIOS"*
Only if you need old BIOS-only machines.
1. In firmware, enable **CSM / Legacy boot**.
2. Boot the USB in Legacy mode.
3. Loads to desktop → **Pass**. If you only use UEFI, set this to **N/A**.
   - Note: the builder warned `bootsect returned exit 1`, so legacy boot may not work on this stick.
     That's expected/irrelevant for UEFI use.

## Step 3 — Write-protection test (THE important one)
This proves WinFE does not alter attached disks. → *"Internal source disk remained offline / read-only"*
and *"Test source disk hash matched before and after boot"*.

**Before booting WinFE (on a normal, trusted computer):**
1. Attach the **test source disk**.
2. Compute its hash and write it down:
   - **Whole-disk (best):** FTK Imager → *File → Verify Drive* (or *Create Disk Image* and note the
     source hash) on the physical device. Record the **MD5/SHA-1**.
   - **File-level (quick alternative):** hash a known file with PowerShell:
     ```powershell
     Get-FileHash 'D:\testfile.bin' -Algorithm SHA256
     ```
3. Safely remove the test disk.

**Boot WinFE with the test disk attached:**
4. Boot the test computer into WinFE (Step 1) **with the test source disk connected**.
5. Open **Disk Management** (or `diskpart` → `list disk`) inside WinFE.
6. Confirm the test/internal disk is **Offline** or **Read-only** and did **not** auto-mount.
   - If it stayed offline/read-only → **Internal source disk remained offline / read-only = Pass**.
7. Do *not* write anything to it. Shut WinFE down.

**After booting (back on the normal computer):**
8. Re-attach the test disk and recompute the **same** hash as step 2.
9. If the hash is **identical** → **Test source disk hash matched before and after boot = Pass**.
   - If it changed, write-protection **failed** — set **Fail** and do not use the build for casework.

## Step 4 — Destination detected  → *"USB destination detected"*
Attach the **destination USB** while booted in WinFE. If WinFE sees it (it appears in Disk Management
/ File Explorer) → **USB destination detected = Pass**. This confirms WinFE can present a writable
destination for a later acquisition.

---

## Step 5 — Generate the report
On the **Validation** page, click **Generate Report**. In one step it builds an **HTML** report for
the most recent build — combining the build details with the checklist you just filled in — and opens
it. That HTML file (under `reports\`) is your audit trail. No separate validation/report JSON files
are written, and there is no separate Reports page.

---

## Quick mapping: checklist item → where the answer comes from

| Checklist item | How you get the answer |
|----------------|------------------------|
| Booted UEFI | Boot the USB in UEFI mode (Step 1) |
| Booted legacy BIOS | Boot in CSM/Legacy mode (Step 2) or N/A |
| Internal source offline/read-only | Disk Management/diskpart inside WinFE (Step 3.6) |
| Test source hash matched before/after | Hash before vs. after (Step 3.2 vs 3.8) |
| USB destination detected | Attach destination in WinFE (Step 4) |

**Only mark Pass for what you actually observed.** Leave anything untested as **Not Tested** — that
honesty is the whole point of the record.
