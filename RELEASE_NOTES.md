# ClearGuard v1.0.0

## فارسی

نسخهٔ نخست ClearGuard: ابزار قابل‌حمل ویندوز با رابط فارسی برای بررسی و پاک‌سازی محافظه‌کارانهٔ کش‌های قابل بازسازی، گزارش فایل‌های حجیم، بررسی سورس‌های تکراری و دسته‌بندی انتخابی دسکتاپ.

### دانلود و اجرا

فایل `ClearGuard-1.0.0-Windows-Portable.zip` را استخراج کنید و `ClearGuard.exe` را کنار فایل config اجرا کنید. Windows 10/11 و .NET Framework 4.8؛ بدون نصب و نیاز به Administrator. سورس در `ClearGuard-1.0.0-Source.zip` و مخزن موجود است. هش فایل‌های انتشار در `SHA256SUMS.txt` آمده است.

### نکات مهم

- ابتدا اسکن؛ هیچ حذف خودکار یا زمان‌بندی‌شده‌ای ندارد.
- عکس و فیلم، فایل شخصی، پروژه، سورس، APK، کلید و LocalHistory از پاک‌سازی محافظت می‌شوند. Codex sessions/runtime، AVD و SDK حفظ می‌شوند.
- Gradle wrapper/daemon/tmp و کش‌های Android Studio با بسته بودن برنامهٔ مرتبط بررسی می‌شوند. Gradle `caches` و Maven `.m2` فقط گزارش هستند.
- کش‌های محدود Chrome، npm/pip/node-gyp/Go، updaterهای شناخته‌شده، CrashDumps، D3DSCache و `.android/cache` فقط در صورت تأیید نوع و محتوا قابل انتخاب‌اند.
- سورس متفاوت یا شاخهٔ مستقل حذف نمی‌شود؛ بازیافت سورس تنها برای تکراری کاملاً یکسان با نسخهٔ نگه‌داری‌شدهٔ دوباره‌بررسی‌شده است.
- پیش‌فرض Recycle Bin است؛ انتقال به آن فضای C را فوراً آزاد نمی‌کند و برنامه آن را تخلیه نمی‌کند. حذف دائمی تنها برای کش مجاز، با تایپ «پاک کن» و غیرقابل بازگردانی است.
- گزارش عملیات اندازهٔ منطقی پردازش‌شده و فضای واقعی آزاد C قبل/بعد را جداگانه اعلام می‌کند.

گزارش اعتبارسنجی: ۳۳ آزمون fixture موفق، صفر ناموفق، صفر ردشده؛ رندر هفت صفحه موفق. هیچ فایل واقعی کاربر در این آزمون‌ها حذف یا جابه‌جا نشد. پوشش کامل همهٔ برنامه‌های فعال، نسخه‌های ویندوز، ACL و سیاست ظرفیت سطل زباله ادعا نمی‌شود. فایل اجرایی امضای دیجیتال ندارد؛ محافظ ویندوز و آنتی‌ویروس را غیرفعال نکنید.

[راهنمای فارسی](https://github.com/matinmt3/ClearGuard/blob/v1.0.0/README.fa.md) · [محدودیت‌های ایمنی](https://github.com/matinmt3/ClearGuard/blob/v1.0.0/SECURITY.md)

## English

The first ClearGuard release: a portable native Windows tool with a Persian RTL interface for conservative cache cleanup, large-file reports, source-duplicate audits, and explicit Desktop organization.

### Download and run

Extract `ClearGuard-1.0.0-Windows-Portable.zip` and run `ClearGuard.exe` next to its config. Requires Windows 10/11 and .NET Framework 4.8; no installation or Administrator privileges. Source is available in `ClearGuard-1.0.0-Source.zip` and the repository. Check release assets against `SHA256SUMS.txt`.

### Highlights and boundaries

- Scan first; no automatic or scheduled deletion.
- Cleanup protects media, personal files, projects, source, APKs, keys, and LocalHistory. Codex sessions/runtimes, AVDs, and SDK components remain untouched.
- Process-guarded Gradle wrapper/daemon/tmp and Android Studio caches/indexes; Gradle `caches` and Maven `.m2` are report-only.
- Recognized Chrome, npm/pip/node-gyp/Go, updater, crash-dump, Direct3D, and Android download cache files are eligible only after type/content checks.
- Changed sources and independent branches stay. Only verified exact duplicates with a reverified keeper can be recycled.
- Recycle Bin is the default and does not immediately free C-drive space; the app never empties it. Irreversible permanent deletion is cache-only and requires typing `پاک کن`.
- Reports separate logical processed bytes from actual free-space changes.

Validation artifacts record 33 passed, 0 failed, 0 skipped fixture tests and successful rendering of seven native pages. No real user files were modified by these tests. This is not exhaustive testing of every live application, Windows version, ACL, or Recycle Bin capacity policy. The executable is unsigned; do not disable antivirus or Windows protections.

[English guide](https://github.com/matinmt3/ClearGuard/blob/v1.0.0/README.md) · [Safety limits](https://github.com/matinmt3/ClearGuard/blob/v1.0.0/SECURITY.md)
