# MemCls – Cross-Platform Memory Cleaner
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

English | [简体中文](README.zh.md)

## Overview
MemCls is a lightweight, cross-platform native C# console utility that frees up system memory. It automatically detects the host operating system at runtime and applies native optimization techniques for **Windows**, **Linux**, and **macOS**.

### Platform Features & Optimizations

| Feature / Operation | Windows | Linux | macOS |
| :--- | :---: | :---: | :---: |
| **Process Working Sets Cleanup** | Yes (`EmptyWorkingSet`) | Simulated | Simulated |
| **System Cache Purge** | Yes (Standby / Low-priority / System File Cache) | Yes (`drop_caches` / `sync`) | Yes (Native `purge` command) |
| **Registry Cache Reconcile** | Yes (`NtSetSystemInformation`) | N/A | N/A |
| **Physical Page Combining** | Yes (Memory Compression) | N/A | N/A |
| **Auto-Elevation (UAC)** | Yes | No (requires `sudo`) | No (requires `sudo`) |
| **Fallback Mode (Standard User)** | Yes (Working Sets only) | Yes (Memory stats only) | Yes (Memory stats only) |

---

## Detailed Platform Behavior

### 1. Windows
When running on Windows, MemCls utilizes low-level Win32/NT APIs (`ntdll.dll`, `kernel32.dll`, `advapi32.dll`, `psapi.dll`):
- **Standard User Mode:** Optimizes working sets of all accessible user processes.
- **Administrator Mode:** Automatically attempts UAC elevation to enable necessary security privileges (`SeDebugPrivilege`, `SeProfileSingleProcessPrivilege`, `SeIncreaseQuotaPrivilege`). Once elevated, it performs:
  - Purging standby and low-priority standby page lists.
  - Flushing system modified page lists.
  - Flushing the system file cache via `SetSystemFileCacheSize`.
  - Reconciling the registry cache.
  - Combining physical memory pages.

### 2. Linux
When running on Linux, MemCls interacts with system configuration and procfs:
- **Standard User Mode:** Reads system-wide memory metrics from `/proc/meminfo` and shows initial/final memory states.
- **Root Mode (run via `sudo`):** Flushes dirty page cache buffers using the `sync` command, then drops memory pagecaches, dentries, and inodes by writing `3` to `/proc/sys/vm/drop_caches`.

### 3. macOS
When running on macOS, MemCls leverages native system utilities:
- **Standard User Mode:** Queries memory statistics via `sysctl` (`hw.memsize`) and `vm_stat` to display memory load, total, and available physical memory.
- **Root Mode (run via `sudo`):** Executes the native `purge` command to clear the OS-level system cache.

---

## UI and Console Aesthetics
- **True Color Gradients:** Uses ANSI virtual terminal processing to output elegant gradient headings (Ice Blue to Bright Cyan/Blue) and color-coded status prefixes.
- **Muted Console Output:** Keeps the terminal output neat and clean by only writing `Error` messages to the console.
- **Structured File Logging:** All log entries (`Info`, `Warning`, `Error`) are logged to a daily file inside a `log` directory next to the executable (e.g., `log/memcls_YYYYMMDD.log`).

---

## Usage

### Windows
1. Run `MemCls.exe`.
2. If not running as Administrator, UAC will prompt you for elevation.
   - Choose **Yes** to run a complete cleanup (all 6 optimization steps).
   - Choose **No** to fall back to Standard User mode (only process working sets are cleared).
3. The program will execute the cleanup and display before-and-after statistics.

### Linux & macOS
1. Open a terminal and run the binary.
2. To purge system caches, run with root privileges:
   ```bash
   sudo ./MemCls
   ```
   *Note: If run without `sudo`, it will report memory statistics but skip system cache purging.*

---

## Build Configurations
The project ships with three publish configurations defined in [MemCls.csproj](file:///c:/Users/Harry/Documents/GitHub/MemCls/MemCls/MemCls.csproj):

| Configuration | Description | Output | Runtime Dependency |
|---|---|---|---|
| **JIT** (self-contained) | Regular JIT compilation, but the publish bundles the .NET runtime. | `publish/JIT/MemCls.exe` | None – fully bundled |
| **R2R** | Ready-to-Run (pre-compiled) + self-contained runtime – faster start-up. | `publish/R2R/MemCls.exe` | None – fully bundled |
| **AOT** | Native AOT – completely native binary, single file, smallest size. | `publish/AOT/MemCls.exe` | None – fully bundled |

### Compilation Commands

By default, the publish configurations in the csproj target `win-x64`. You can compile for other platforms by specifying the appropriate runtime identifier (RID):

```powershell
# Windows x64 Native AOT
dotnet publish -c AOT -r win-x64 -o publish/AOT

# Linux x64 Self-Contained
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux

# macOS ARM64 (Apple Silicon) Self-Contained
dotnet publish -c Release -r osx-arm64 --self-contained -o publish/osx
```

---

## Configuration & Roadmap
* **Configuration File (Roadmap):** In future versions, behavior can be customized by placing a `memcls.json` configuration file beside the executable.

---

## License
This project is licensed under the **Apache License, Version 2.0**.
See the [LICENSE](LICENSE) file for the full license text.
For additional attribution notices, please see the [NOTICE](NOTICE) file.
