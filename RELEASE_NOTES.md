# ClearGuard v1.2.0

اعتبارسنجی / Validation: **171 safety tests + 37 native UI interaction checks + 11 page renders: PASS; 0 failed, 0 skipped.** All 75 safety / 22 UI checks from v1.1.0 are retained. Includes delayed-response dispatcher synchronization checks. Only generated fixtures were mutated; no real user cleanup. Journal persistence retries recognized transient Windows errors without a destructive fallback.

## فارسی

سه قابلیت تازه با کنترل دستی کاربر: بررسی نسخهٔ GitHub، داشبورد خواندنیِ تغییر حجم پوشه‌ها و پوشه‌های همیشه محافظت‌شده. محدودهٔ پاک‌سازی گسترش نیافته و قابلیت‌های قبلی حفظ شده‌اند.

### تازه‌ها

- بررسی دستی نسخه: فقط با دکمهٔ بررسی، درخواست HTTPS به API ثابت آخرین انتشار رسمی GitHub؛ مهلت ۱۰ ثانیه، بدون احراز هویت/Cookie، بدون دنبال کردن تغییرمسیر و با بررسی پاسخ محدود، نسخهٔ پایدار و نشانی دقیق رسمی. خطا یا محدودیت یعنی «نامشخص»، نه «به‌روز است». یادداشت‌ها متن ساده و بی‌اثرند؛ HTML، لینک یا فرمان داخل آن‌ها اجرا نمی‌شود. صفحهٔ رسمی فقط با اقدام جداگانه باز می‌شود؛ دریافت، نصب یا اجرای خودکار ندارد.
- داشبورد فضا: جمع خواندنی طول منطقی فایل‌ها، دسته‌های پوشهٔ سطح اول/فایل مستقیم و نمودار افقی. سقف پیش‌فرض ۲۰۰٬۰۰۰ ورودی، ۶۰ ثانیهٔ تعاونی، ۶۴ سطح و ۴٬۰۹۶ دسته؛ بدون خواندن محتوا یا دنبال کردن reparse/junction/symlink. اختلاف دقیق فقط میان دو snapshot کامل با ریشه/سیاست/استثنا/سقف یکسان است. اسکن اول، ناقص، نامتجانس یا تاریخچهٔ نامعتبر اختلاف قطعی ندارد؛ فایل قبلی حفظ می‌شود. حجم منطقی، فضای فیزیکی یا قابل پاک‌سازی نیست. پروفایل پیش‌فرض معمولاً junction/خطای دسترسی دارد؛ مسیر کوچک‌تر برای مقایسهٔ کامل انتخاب کنید. این بخش حذف، انتقال، اسکن خودکار یا پایش پس‌زمینه ندارد.
- حفاظت شخصی: افزودن دستی پوشهٔ محلیِ موجود با مسیر کامل؛ فقط تنظیمات برنامه تغییر می‌کند، نه محتوای پوشه. مرز canonical و بدون حساسیت به حروف به قواعد ثابت اضافه می‌شود. زیرپوشهٔ محافظت‌شده کل ردیف کش/پوشهٔ سورس را مسدود می‌کند؛ سورس/نسخهٔ نگه‌داری‌شده، مبدأ/مقصد دسته‌بندی و بازگردانی هم رعایتش می‌کنند. ردیف قدیمی دوباره بررسی و انتخاب‌های قبلی باطل می‌شوند. حذف فقط ورودی سفارشی با تأیید جداگانه است؛ قواعد ثابت حذف نمی‌شوند. پوشهٔ ناپدیدشده محافظت می‌ماند.
- مسیر نسبی/شبکه/UNC/device/ADS، متغیر بازنشده، wildcard، زنجیرهٔ پیوندی/نامطمئن و اجزای مبهم رد می‌شوند. هر جزء دارای `~`، شامل alias کوتاه 8.3 و حتی نام واقعی دارای tilde، محافظه‌کارانه رد می‌شود؛ از مسیر بلند عادی بدون `~` استفاده کنید.
- تنظیمات معتبر با فایل موقت تازه، flush، بازبینی و جایگزینی atomic همراه backup یکتای JSON قبلی ذخیره می‌شود. تنظیمات ناشناخته، خراب، قفل‌شده، پیوندی یا فایلِ قبلاً شناخته‌شدهٔ ناپدیدشده fail-closed است: اقدام روی فایل/تغییر تنظیمات مسدود و فهرست آخر حفظ می‌شود. ترمیم دستی لازم است؛ بازنویسی/بازیابی خودکار فایل خراب ندارد.

### ایمنی و قابلیت‌های قبلی

کش مجاز C همچنان فایل‌به‌فایل، انتخابی و پس از بررسی محتوا/هش/فرایند پاک می‌شود. پیش‌فرض Recycle Bin است؛ فضای همان درایو را فوراً آزاد نمی‌کند و برنامه آن را تخلیه نمی‌کند. حذف دائمی فقط کشِ تأییدشده با تایپ «پاک کن» و غیرقابل بازگردانی است. رسانه، فایل شخصی، سورس، APK، کلید، LocalHistory، Codex، AVD/SDK، Gradle `caches` و Maven همچنان محافظت یا فقط گزارش‌اند.

فهرست خواندنی برنامه‌ها با حجم تخمینی/نامشخص/اندازه‌گیری محدود، گزارش JSON/HTML/CSV بدون بازنویسی، تکراری‌های خواندنی درایو، دسته‌بندی انتخابی، رسید/بازگردانی بدون بازنویسی و خروج با توقف/انتظار امن حفظ شده‌اند. Uninstall یا بستن اجباری برنامهٔ نصب‌شده ندارد. پوشهٔ سورس همچنان فقط گزارش است؛ قفل لازم برای وادار کردن بازیافت Windows Shell ضعیف نمی‌شود. ZIP سورسِ یکسان و تأییدشده با نسخهٔ نگه‌داری‌شدهٔ معتبر و محافظت‌نشده انتخابی بازیافت می‌شود.

### داده، دانلود و ارتقا

حفاظت در `%LOCALAPPDATA%\ClearGuard\settings\protected-folders.json` و backupها در همان پوشه است؛ snapshot در `%LOCALAPPDATA%\ClearGuard\Dashboard` و گزارش/رسید در `%LOCALAPPDATA%\ClearGuard\Reports`. این داده‌ها خصوصی و محلی‌اند و در انتشار نیستند؛ مسیرهای خود برنامه محافظت می‌شوند. مسیر، نام کاربر، فهرست، محتوا، snapshot یا تنظیمات به GitHub ارسال نمی‌شود؛ telemetry ندارد. GitHub و واسطهٔ شبکه IP عمومی و اطلاعات عادی اتصال را می‌بینند؛ صفحهٔ انتشار تابع رفتار عادی حساب/Cookie مرورگر است.

`ClearGuard-1.2.0-Windows-Portable.zip` را استخراج و فایل اجرایی را کنار config اجرا کنید. Windows 10/11 و .NET Framework 4.8؛ بدون نصب، بستهٔ اضافی یا Administrator. سورس در `ClearGuard-1.2.0-Source.zip` و هش‌ها در `SHA256SUMS.txt` است. باینری امضای دیجیتال ندارد؛ محافظ ویندوز/آنتی‌ویروس را غیرفعال نکنید.

پیش از ارتقا برنامه را ببندید و تنظیمات/backup، snapshot و رسیدها را نگه دارید. انتشار ۱.۱.۰ حفظ می‌شود، اما **نسخهٔ قدیمی حفاظت سفارشی ۱.۲.۰ را نمی‌شناسد؛ downgrade را برای پاک‌سازی، دسته‌بندی یا بازگردانی مسیر محافظت‌شده استفاده نکنید**. در صورت مشکل، اقدام تغییردهنده را متوقف کنید؛ رسید را پاک و Recycle Bin را تخلیه نکنید.

### اعتبارسنجی

همهٔ دروازه‌های ایمنی/رابط، شامل آزمون‌های قبلی و تازه و یازده صفحه، باید بدون شکست/ردشدن برای همان باینری موفق باشند. تعداد نهایی و SHA-256 در گزارش‌های پالایش‌شده ثبت می‌شود. دادهٔ تغییردهنده فقط fixture مصنوعی است. آزمون خودکار نسخه پاسخ تزریقی دارد، نه شبکهٔ عمومی؛ بررسی زندهٔ جداگانه جدا مشخص می‌شود. تصویرهای داشبورد/نسخه مصنوعی‌اند، نه اسکن کاربر یا بررسی زنده. این شواهد محلی، تأیید جامع همهٔ ACLها، فرایندهای زنده، نسخه‌های ویندوز یا ظرفیت سطل زباله نیست.

[راهنمای فارسی](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/README.fa.md) · [گزارش ایمنی](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/docs/Safety-Test-Report.json) · [آزمون رابط](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/docs/UI-Test-Report.json) · [محدودیت‌ها](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/SECURITY.md)

## English

Three new user-controlled features: manual GitHub release checks, a read-only folder-growth dashboard, and always-protected custom folders. The cleanup allowlist is not expanded; existing features remain.

### New features

- Manual updates: only the Check action requests the fixed official latest-release HTTPS endpoint. A 10-second deadline, no authentication/cookies, blocked redirects, bounded strict metadata, stable-version comparison, and an exact official release URL are enforced. Errors/rate limits/timeouts leave status unknown. Notes remain inert plain text; HTML, links, and commands are not executed. A separate explicit action opens the official page. No automatic asset download, installation, or execution.
- Space dashboard: read-only logical file-length totals, immediate-folder/direct-root categories, horizontal bars, and local snapshots. Default limits: 200,000 entries, 60 seconds cooperatively checked, 64 levels, and 4,096 categories. No file-content reads or reparse/junction/symlink traversal. Exact arithmetic deltas require two complete snapshots with identical root/policy/exclusions/budgets; first, partial, different-scope, or invalid-history results remain unknown. Prior history is retained. Logical bytes are not physical allocation or reclaimable space. Profile roots commonly contain compatibility junctions/access failures; choose a narrower ordinary folder for complete comparisons. No cleanup, move, automatic scan, or background monitoring.
- Custom protected folders: manually add an existing full-path local folder; only app settings change, not folder contents. Canonical case-insensitive hierarchy checks add to immutable built-in protections. A protected descendant blocks the whole encompassing cache/source-folder candidate; source keepers, organizer endpoints, and restore paths are blocked too. Old selections are invalidated and current settings revalidated before mutation. Only custom entries can be removed, with explicit confirmation. Vanished folders remain protected.
- Relative/network/UNC/device/ADS/variable/wildcard paths, ambiguous components, and linked/uninspectable chains are refused. Every component containing `~` is conservatively refused, including 8.3 aliases and legitimate tilde names; use a normal long path without `~`.
- Valid settings edits use flushed new temporary files, revalidation, atomic replacement, and unique prior-JSON backups. Unknown, corrupt, locked, linked, or disappeared previously known settings fail closed: file actions and setting changes stop while last-known protections are retained. Repair manually; malformed files are not silently overwritten or automatically recovered.

### Retained behavior and safety

C-drive allowlisted cache cleanup remains per-file, explicitly selected, and content/hash/process-revalidated. Recycle Bin is the default, does not immediately free same-drive space, and is never emptied by ClearGuard. Irreversible permanent deletion remains verified-cache-only with typed `پاک کن` confirmation. Media, personal files, source, APKs, keys, LocalHistory, Codex, AVD/SDK, Gradle `caches`, and Maven remain protected or report-only.

Read-only installed-app inventory with estimated/unknown/bounded-measurement provenance, non-overwriting JSON/HTML/CSV exports, read-only drive duplicates, optional loose-file organization, no-overwrite receipt recovery, and cancellation-and-wait Exit remain. No installed-app uninstall or forced termination is performed. Source folders remain report-only rather than weakening required holds to force Windows Shell recycling. Verified exact safe source ZIP duplicates with a valid unchanged, unprotected keeper remain explicitly recyclable.

### Local data, download, and upgrade

Protections reside at `%LOCALAPPDATA%\ClearGuard\settings\protected-folders.json`, with backups in the same directory. Snapshots reside at `%LOCALAPPDATA%\ClearGuard\Dashboard`; reports/receipts at `%LOCALAPPDATA%\ClearGuard\Reports`. These are private local data, not release assets; app-owned locations are protected. No personal paths, usernames, inventories, contents, snapshots, or settings are sent to GitHub; no telemetry is implemented. GitHub/network intermediaries still see the public IP and normal connection metadata. The release page uses the browser's ordinary account/cookie behavior.

Extract `ClearGuard-1.2.0-Windows-Portable.zip` and run the executable beside its config. Windows 10/11 with .NET Framework 4.8; no installer, extra packages, or Administrator requirement. Source is in `ClearGuard-1.2.0-Source.zip`; verify `SHA256SUMS.txt`. The executable is unsigned; do not disable antivirus or Windows protection.

Close ClearGuard before upgrading; retain settings/backups, snapshots, and receipts. v1.1.0 assets remain preserved, but **older versions do not enforce v1.2.0 custom protections: do not downgrade and then clean, organize, or restore protected paths**. If a feature misbehaves, stop file mutations. Do not remove receipts or empty the Recycle Bin to change versions.

### Validation

All safety/UI gates—legacy and added cases, native interactions, and eleven page renders—must pass without failures/skips for the same binary. Sanitized reports record final counts and executable SHA-256. Mutation tests use generated isolated fixtures only. Automated updater tests use injected responses without public network calls; any separate live check is identified separately. Dashboard/update previews are synthetic, not user scans or live checks. This local evidence is not exhaustive certification across every ACL, live process, Windows version, or Recycle Bin capacity policy.

[English guide](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/README.md) · [Safety tests](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/docs/Safety-Test-Report.json) · [Native UI checks](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/docs/UI-Test-Report.json) · [Safety limits](https://github.com/matinmt3/ClearGuard/blob/v1.2.0/SECURITY.md)
