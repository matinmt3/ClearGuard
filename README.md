# ClearGuard

**Know what you clean. Keep what matters.**

ClearGuard is a portable Windows disk-cleanup and Desktop audit tool with a Persian, right-to-left interface. It scans first, explains each candidate, and requires an explicit choice before an operation. The cleanup engine is deliberately conservative: it works from a small cache allowlist, rechecks files before acting, and keeps unknown or protected data.

**[Download v1.0.0](https://github.com/matinmt3/ClearGuard/releases/tag/v1.0.0)** · [راهنمای فارسی](README.fa.md) · [Safety and limitations](SECURITY.md)

> ClearGuard does not automatically delete files. Moving a file to the Recycle Bin does not free space on the same drive. The app never empties the Recycle Bin. Optional permanent deletion is limited to verified cache files and cannot be undone.

![ClearGuard Persian Windows interface](docs/Preview.png)

## فارسی، کوتاه

ClearGuard یک برنامهٔ ویندوزی قابل‌حمل با رابط فارسی برای بررسی فضای درایو C، کش‌های قابل بازسازی، فایل‌های حجیم، سورس‌های تکراری و دسته‌بندی انتخابی دسکتاپ است. ابتدا اسکن می‌کند؛ حذف خودکار ندارد. عکس و فیلم، پروژه‌ها، فایل‌های شخصی، SDK/AVD، LocalHistory و Codex محافظت می‌شوند. راهنمای کامل در [README.fa.md](README.fa.md) است.

## Run

1. Download `ClearGuard-1.0.0-Windows-Portable.zip` from the release page.
2. Extract the ZIP. Keep `ClearGuard.exe` and `ClearGuard.exe.config` together.
3. Run `ClearGuard.exe`, scan, inspect the results, and select only the operations you understand.

Built for Windows 10/11 with .NET Framework 4.8. No installer, SDK, npm, external package downloads, or Administrator privileges are needed to run it. The interface is Persian only; this repository's documentation is bilingual. The executable is not digitally signed. Compare the release checksums; do not disable antivirus or Windows protections to run it.

## What v1 does

| Area | Behavior |
|---|---|
| Rebuildable caches | Per-file cleanup of recognized eligible files, not blind folder removal. Lists full paths, size, application, cleanup effect, and skip reasons. |
| Large-file inventory | Read-only folder sizes and the 100 largest files under a selected root; can inspect the user profile or C drive. |
| Drive duplicates | Read-only size grouping followed by SHA-256 comparison for files at least 1 MiB. No deletion in this section. |
| Desktop source audit | Discovers projects/source archives up to eight levels deep. Reads manifest versions and compares complete structure and hashes. Only verified exact duplicates with a verified unchanged keeper can be selected for recycling. |
| Desktop organization | Preview plus explicit movement of recognized loose files at the Desktop root into `دسکتاپ مرتب`. Folders, projects, code, APKs, executables, archives, and shortcuts stay in place. |
| History and restore | Operation journals and verified Recycle Bin receipts; restores only items in a selected receipt, without overwriting existing files. Permanent deletion is not restorable. |
| Reports | JSON, CSV, and HTML; largest items, eligible/protected sizes, operation outcomes, logical processed bytes, and actual C-drive free space before/after. |
| Windows storage | Opens the official Windows Storage settings. Does not directly delete Windows Update files. |

### Cache scope and cost of rebuilding

Only recognized files that pass the protection checks are eligible; not every file inside these locations will be removed.

| Cache family | Eligible scope | What happens afterward |
|---|---|---|
| Gradle | `wrapper`, `daemon`, `.tmp` | Wrapper distributions may download again; daemon state/logs and temporary files are recreated. Active Gradle/Java uncertainty blocks cleanup. |
| Android Studio | `index`, `caches`, `log`, `lint`, `tmp`, `gmaven.index`, `maven.google` | Indexes and caches rebuild; Maven indexes may refresh. Android Studio must be fully closed. `LocalHistory` stays untouched. |
| Updaters | Recognized cache locations for Chatbox, Oblivion, BlueStacks, 4ebur, iGap, Namava | Update packages may download again. Installed apps are not uninstalled. |
| Chrome | `Cache`, `Code Cache`, `GPUCache`, `Service Worker/CacheStorage` in recognized profiles | Cached content rebuilds/downloads again. Chrome must be closed. Passwords, cookies, history, bookmarks, and profile data are not cleanup targets. |
| npm, pip, node-gyp | Recognized cache files | Packages or Node headers may download again; unsupported/uncertain compressed content stays. |
| Go | `go-build`, module `cache/download` | Build artifacts recompile; cached module downloads may download again. The broader module source tree is not a cleanup target. |
| CrashDumps | Recognized crash dump files | Old crash-debugging evidence is lost. Dumps are not rebuilt unless a future crash occurs. |
| Direct3D | `D3DSCache` | Shaders rebuild; an initial application/game launch can be slower. |
| Android download cache | `.android/cache` | Cached downloads are recreated. SDK components and virtual devices remain. |

**Report-only / protected:** Gradle `caches`, Maven `.m2/repository`, Codex cache/tmp/runtime, Android AVD, and Chrome's on-device AI model. Local Maven artifacts cannot reliably be separated from downloadable dependencies, so v1 does not delete Maven artifacts.

## Safety boundaries

- Photos, videos, audio, source code, APK/AAB, keys, sensitive environment files, and Android Studio `LocalHistory` are protected from cleanup. Content checks include extensions, recognized media signatures inside opaque cache data, and supported ZIP contents. Unsupported, ambiguous, or overly complex archives are kept.
- Desktop, Documents, Downloads, and projects are not cache-cleanup targets. The separate organizer can move explicitly selected recognized loose documents/media; it never deletes them. All organizer rows start unselected.
- SYMEXVPN and proxyx project identities receive additional source-audit protection. Different branches, changed copies, or unique data are kept. A newer timestamp or version number alone is never a deletion rule.
- AVDs, emulator snapshots, SDK system images, SDK/NDK/build-tools/platforms, IDM downloads, Visual Studio packages, Codex sessions/runtimes, installed programs, Windows Installer, WinSxS, Program Files, ProgramData, and system files are not cleanup targets.
- Reparse points, symlinks, and junctions are not followed or removed. Unknown files, locked files, permission failures, and uncertain process checks are skipped.
- Approved paths, content, hashes, sizes, modification times, and process conditions are checked again before action. Deletion is restricted to C. No process is forcibly terminated.
- Recycle Bin is the default. Permanent cache deletion requires typing the Persian confirmation `پاک کن`. Source duplicates cannot use permanent deletion.

The safety policy reduces risk; it is not a guarantee that every possible file format, OS policy, or race is detectable. Back up irreplaceable data and read [SECURITY.md](SECURITY.md) before using destructive modes.

## Local data and privacy

Reports and operation receipts are stored under `%LOCALAPPDATA%\ClearGuard\Reports`. They may contain full filenames, local paths, hashes, and crash metadata. Do not share them publicly without reviewing/redacting them. ClearGuard has no telemetry or file-inventory upload feature. It does not itself download replacement dependencies; the relevant application does that when needed.

## Build and validate

The source uses C# 5, WPF, and .NET Framework 4.8. Building requires the .NET Framework compiler and WPF assemblies at the paths checked by `Build.ps1`; the runtime alone is the requirement for running the published app. No external package downloads are needed. `Build.ps1` invokes the installed compiler and copies the application configuration:

```powershell
& .\Build.ps1 -Destination .\build
```

Run from the repository root. `Test.ps1` keeps fixture-only safety tests inside the repository's `work\ClearGuardTests` tree, waits for the executable, verifies all 33 tests, then renders all seven native pages:

```powershell
& .\Test.ps1
& .\Package.ps1
```

Tests create isolated synthetic files. Some tests genuinely recycle, restore, move, or permanently delete **those generated fixtures only**. Never point a test at your projects or personal data. `Package.ps1` requires reports for the exact binary, creates portable/source ZIPs with checksums in a fresh output directory, and refuses to overwrite existing package directories. It packages sanitized public test reports, not the raw local fixture report. UI smoke rendering is also available with `--ui-smoke <output-directory>`; it is not a substitute for manual interaction testing.

The v1 release binary was revalidated with **33 passed, 0 failed, 0 skipped** safety tests and seven successful native page renders. This is local fixture/render evidence, not exhaustive certification of all Windows versions, live applications, ACL policies, or Recycle Bin capacity behavior. See the published test report and its remaining gaps.

### Source layout

- `App.cs`, `MainWindow.xaml`: native UI and user confirmations.
- `CacheEngine.cs`, `SafetyPolicy.cs`: candidate allowlist, inspection, and revalidation.
- `DesktopTools.cs`: source comparison, read-only duplicates, organization, and restore.
- `RecycleService.cs`: Windows shell recycling and receipt verification.
- `Models.cs`, `ReportExport.cs`: local reports and safe exports.
- `SafetyTests.cs`: isolated safety and recovery fixtures.

## Contributions and license

MIT License — see [LICENSE.txt](LICENSE.txt). Safety-sensitive changes need fixture tests and a clear explanation of the protection boundaries. Do not broaden deletion targets or weaken fail-closed behavior without explicit review. Report issues with a small synthetic reproduction, not personal files or unredacted local inventories.
