# Changelog / تغییرات

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
