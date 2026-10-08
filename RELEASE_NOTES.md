# ClearGuard v1.1.0

## فارسی

این نسخه دکمهٔ مشخص «خروج» و بخش «برنامه‌های نصب‌شده» را اضافه می‌کند؛ قابلیت‌های پاک‌سازی محافظه‌کارانهٔ نسخهٔ قبل حفظ شده‌اند.

### تغییرات

- خروج در حالت بیکار برنامه را می‌بندد؛ هنگام کار ابتدا درخواست توقف می‌دهد و تا پایان امن عملیات منتظر می‌ماند.
- فهرست خواندنی برنامه‌های نصب‌شده: نام، نسخه، ناشر، مسیر نصب و اطلاعات حجم از Registry کاربر/سیستم در نماهای ۳۲/۶۴بیتی، همراه پرس‌وجوی Store کاربر فعلی در صورت دسترسی.
- حجم Registry با برچسب «تخمینی» است؛ حجم ناموجود «نامشخص» است، نه صفر. اندازه‌گیری پوشهٔ نصبِ واقعی و محدود، انتخابی و فقط خواندنی است. حجم مشترک/هم‌پوشان یا ناقص جمع دقیق فضای کل سیستم نیست.
- گزارش برنامه‌ها در JSON/CSV/HTML؛ هیچ برنامه‌ای Uninstall نمی‌شود و فرمان Uninstall اجرا نمی‌شود.
- گزارش جدید فایل JSON/HTML/CSV موجود را بازنویسی نمی‌کند؛ حفاظت CSV در برابر فرمول با فاصلهٔ ابتدایی هم فعال است. انتخاب موارد مجاز فقط روی ردیف‌های قابل‌مشاهده انجام می‌شود.
- گسترش آزمون‌های fixture برای تشخیص/اندازه‌گیری برنامه‌ها و آزمون تعامل بومی هشت صفحه، از جمله خروج بیکار/مشغول و جدول برنامه‌های بدون امکان حذف.
- اصلاح مرتب‌سازی عددی حجم و حفظ قفل ضد تغییر نسخهٔ نگه‌داری‌شدهٔ ZIP و فایل دسته‌بندی تا بررسی/اقدام نهایی.
- **پوشهٔ سورس فقط گزارش می‌شود**؛ Windows Shell با قفل‌های حفاظتی لازم بازیافت پوشه را رد می‌کند، پس حذف پوشه مسدود است و حفاظت ضعیف نمی‌شود. ZIP سورسِ کاملاً یکسان و تأییدشده همچنان انتخابی بازیافت می‌شود؛ بازگردانی رسید معتبر قبلی پوشه حفظ شده است. `src`/`app` بدون شناسهٔ مستقل نیز فقط گزارش است.

### دانلود و اجرا

`ClearGuard-1.1.0-Windows-Portable.zip` را استخراج و `ClearGuard.exe` را کنار config اجرا کنید. Windows 10/11 و .NET Framework 4.8؛ بدون نصب و نیاز به Administrator. سورس در `ClearGuard-1.1.0-Source.zip` و مخزن موجود است؛ هش فایل‌ها در `SHA256SUMS.txt` آمده است. برای ارتقا ابتدا نسخهٔ در حال اجرا را ببندید؛ رسیدهای بازیابی را نگه دارید. انتشار ۱.۰.۰ برای بازگشت باقی می‌ماند.

### ایمنی و اعتبارسنجی

ابتدا اسکن؛ حذف خودکار ندارد. عکس و فیلم، فایل شخصی، سورس، APK، کلید، LocalHistory، Codex sessions/runtime، AVD و SDK حفظ می‌شوند. Gradle `caches` و Maven `.m2` فقط گزارش هستند. پیش‌فرض Recycle Bin است؛ انتقال به آن فضای C را فوراً آزاد نمی‌کند و برنامه آن را تخلیه نمی‌کند. حذف دائمی فقط کشِ مجاز، با تایپ «پاک کن» و غیرقابل بازگردانی است.

گزارش‌های انتشار، تعداد دقیق آزمون‌های موفق و هش همان فایل اجرایی را ثبت می‌کنند. بسته‌بندی فقط وقتی مجاز است که تمام ۳۳ آزمون قبلی، آزمون‌های اضافه، رندر هشت صفحه و آزمون‌های تعامل بدون شکست/ردشدن موفق باشند. آزمون‌های تغییردهنده فقط روی دادهٔ مصنوعی تازه اجرا می‌شوند؛ فایل واقعی کاربر پاک یا جابه‌جا نمی‌شود. این شواهد محلی است، نه تأیید جامع همهٔ برنامه‌ها، نسخه‌های ویندوز، ACL یا سیاست ظرفیت سطل زباله. محدودیت‌های باقی‌مانده در گزارش‌ها و راهنمای امنیت آمده‌اند. فایل اجرایی امضای دیجیتال ندارد؛ محافظ ویندوز/آنتی‌ویروس را غیرفعال نکنید.

[راهنمای فارسی](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/README.fa.md) · [گزارش آزمون](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/docs/Safety-Test-Report.json) · [آزمون رابط](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/docs/UI-Test-Report.json) · [ایمنی](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/SECURITY.md)

## English

This release adds a visible **Exit** button and a read-only **Installed applications** section while retaining the previous conservative cleanup features.

### Changes

- Idle Exit closes the window; busy Exit requests cancellation and waits for safe completion instead of killing the process midway through an operation.
- Read-only application name, version, publisher, installation path, and size information from machine/current-user 32-/64-bit registry views, plus a best-effort query of current-user Store packages.
- Registry size is labeled as an estimate; missing size is unknown, not zero. Explicit measurement of a genuine leaf installation folder is read-only. Shared, overlapping, or incomplete figures are not an exact total of system disk usage.
- JSON/CSV/HTML application exports; no uninstall feature and no execution of uninstall commands.
- New report bundles never overwrite existing JSON/HTML/CSV files; CSV formula guards also cover leading whitespace. Select-safe operates only on currently visible filtered rows.
- Expanded synthetic regression tests for application discovery/measurement and native interaction tests covering all eight pages, idle/busy Exit, and the non-destructive installed-app grid.
- Fixed numeric size sorting and retained write-denial holds on source ZIP keepers and organizer files through final verification/action.
- **Source folders are report-only**: Windows Shell refuses recycling with the required protection holds, so new folder deletion is blocked instead of weakening protection. Verified safe exact source ZIP duplicates can still be explicitly recycled; existing verified folder receipts remain restorable. Markerless `src`/`app` children are report-only as well.

### Download and run

Extract `ClearGuard-1.1.0-Windows-Portable.zip` and run `ClearGuard.exe` beside its config. Windows 10/11 and .NET Framework 4.8; no installation or Administrator requirement. Source is in `ClearGuard-1.1.0-Source.zip` and the repository. Verify assets against `SHA256SUMS.txt`. Close the running application before upgrading and retain recovery receipts. The v1.0.0 release remains available for rollback.

### Safety and validation

Scan first; no automatic cleanup. Media, personal files, source, APKs, keys, LocalHistory, Codex sessions/runtimes, AVDs, and SDK components remain protected. Gradle `caches` and Maven `.m2` are report-only. Recycle Bin is the default and does not immediately free C-drive space; the app never empties it. Irreversible permanent deletion is verified-cache-only and requires typing `پاک کن`.

Release reports record the exact passing counts and executable SHA-256. Packaging requires all 33 legacy cases, added regressions, eight page renders, and interaction checks to pass without failure or skips. Mutation tests use newly generated isolated fixtures only, not real user files. This is local evidence, not exhaustive certification of every installed application, Windows version, ACL, or Recycle Bin capacity policy. Remaining gaps are documented in the reports and security guide. The executable is unsigned; do not disable antivirus or Windows protection.

[English guide](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/README.md) · [Safety tests](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/docs/Safety-Test-Report.json) · [Native UI checks](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/docs/UI-Test-Report.json) · [Safety limits](https://github.com/matinmt3/ClearGuard/blob/v1.1.0/SECURITY.md)
