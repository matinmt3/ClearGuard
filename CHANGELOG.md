# Changelog / تغییرات

## 1.1.0 — 2026-10-08

### Added

- Visible Exit button with cancellation-and-wait behavior while a background operation is running.
- Read-only installed-application inventory from machine/current-user 32-/64-bit registry views and a best-effort query of current-user Store packages.
- Labeled registry size estimates, explicit selected-installation-folder measurement, unknown/incomplete states, and JSON/HTML/CSV application exports; no uninstall action.
- Installed-application regression fixtures and native interaction checks for all eight pages, the installed-app grid, and idle/busy Exit.

### Changed

- Release packaging now verifies the legacy safety cases, added regressions, all page renders, interaction checks, and the exact executable hash before producing assets.
- Public test reports omit local paths and private exception diagnostics; output directories remain non-overwriting and prior releases remain available.

### Fixed / Safety

- Result and application size columns sort by numeric bytes, not formatted size strings.
- Reverified source ZIP keepers cannot be concurrently written or renamed during recycling; organizer sources retain write-denial holds through the final verification and move.
- Source project folder duplicates are report-only: Windows Shell refuses recycling under the required protection holds, so folder cleanup is blocked instead of weakening the holds. Existing verified folder-receipt restore remains supported.
- Markerless `src`/`app` child folders cannot stand in for an entire project while unique parent settings/assets are outside the comparison boundary.

### فارسی

- اضافه شدن دکمهٔ مشخص «خروج» با توقف و انتظار امن حین کار.
- بخش خواندنی برنامه‌های نصب‌شده با نام، نسخه، ناشر، مسیر و حجمِ تخمینی/اندازه‌گیری‌شده؛ مقدار نامشخص صفر معرفی نمی‌شود و Uninstall ندارد.
- اندازه‌گیری انتخابی پوشهٔ نصبِ محدود، خروجی JSON/HTML/CSV و توضیح هم‌پوشانی/فایل‌های مشترک؛ بدون ادعای جمع دقیق حجم کل برنامه‌ها.
- آزمون‌های تازهٔ برنامه‌ها و تعامل رابط، حفظ آزمون‌های قبلی، کنترل هشت صفحه و هش همان فایل اجرایی در بسته‌بندی انتشار.
- مرتب‌سازی عددی حجم، قفل نسخهٔ نگه‌داری‌شدهٔ ZIP در برابر تغییر/تغییرنام و قفل فایلِ دسته‌بندی تا بررسی/انتقال نهایی.
- پوشهٔ پروژهٔ تکراری فقط گزارش می‌شود: به‌جای ضعیف کردن قفل‌ها برای بازیافت Windows Shell، پاک‌سازی تازهٔ پوشه مسدود است؛ بازگردانی رسید معتبر قبلی حفظ شده است.
- پوشهٔ `src`/`app` بدون شناسهٔ مستقل پروژه، جای کل پروژه با دادهٔ یکتای والد فرض نمی‌شود.

## 1.0.0 — 2026-10-08

### Added

- Portable native Windows application with a Persian RTL dark interface and bilingual documentation.
- Read-only cache, large-file, drive-duplicate, and Desktop-source audits.
- Per-file cleanup of recognized rebuildable cache locations with process guards and a visible explanation of rebuilding costs.
- Exact source-duplicate recycling after full structure/hash comparison and unchanged-keeper verification; independent or changed versions stay.
- Explicit Desktop loose-file organization, operation journals, receipt-based restore, and no overwrite on conflicts.
- JSON/HTML/CSV exports, largest-item summaries, and separate logical-byte and live free-space measurements.
- Fixture-only safety tests and native UI smoke renders.

### Safety

- Media/content checks, personal-file/source/APK/key protection, LocalHistory preservation, reparse-point rejection, and fail-closed handling of uncertain files or archives.
- Revalidation before action; no automatic cleanup, process termination, uninstall, or Recycle Bin emptying.
- Recycle Bin by default; optional irreversible cache-only deletion with typed confirmation.
- Gradle `caches`, Maven artifacts, Codex caches/runtimes, AVDs, SDK components, and Windows/system locations excluded from direct cleanup.

### فارسی

- انتشار نخست برنامهٔ قابل‌حمل ویندوز با رابط فارسی راست‌به‌چپ و مستندات دوزبانه.
- اسکن خواندنی کش، فایل حجیم، تکراری‌های درایو و سورس دسکتاپ؛ پاک‌سازی انتخابی فایل‌های کشِ تأییدشده.
- بازیافت فقط سورس کاملاً یکسان پس از بررسی ساختار/هش و نسخهٔ نگه‌داری‌شده؛ حفظ نسخهٔ متفاوت و شاخهٔ مستقل.
- دسته‌بندی انتخابی فایل مستقل دسکتاپ، رسید و بازگردانی بدون بازنویسی؛ گزارش JSON/HTML/CSV و فضای آزاد قبل/بعد.
- حفاظت رسانه، فایل شخصی، سورس، APK، کلید و LocalHistory؛ حفظ مسیر نامطمئن و بررسی مجدد پیش از اقدام.
- بدون حذف خودکار، Uninstall، بستن اجباری برنامه یا تخلیهٔ Recycle Bin؛ حذف دائمی فقط کشِ مجاز با تأیید تایپی.
- Gradle caches، Maven، Codex، AVD/SDK و فایل‌های Windows/سیستمی خارج از حذف مستقیم.
