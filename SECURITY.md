# Safety and security / ایمنی و امنیت

ClearGuard can mutate local files after explicit user approval. Treat it as a safety-sensitive tool, not a substitute for backups. Its conservative checks reduce risk but cannot prove every possible file safe.

## Supported release

The v1.2.0 source and release assets document the current implementation. There is no automatic update, scan, monitoring, or cleanup service. Use the official repository/release, verify checksums, and do not disable Windows protection or antivirus. The executable is not digitally signed. Older release assets, including v1.1.0, remain preserved for reference/binary recovery, but those versions do not enforce v1.2.0 custom protections. Do not downgrade and then clean, organize, or restore protected paths. Preserve local settings/backups, snapshots, reports, and recovery receipts when changing versions.

## Safety model

- Read-only scans and explicit operation selection are separate. Default selections are empty.
- Cleanup accepts only recognized per-file candidates within its C-drive cache allowlist. Personal/system locations are not general deletion targets.
- Media extensions, recognized content signatures, sensitive files, supported archive contents, project ancestry, and reparse points are checked. Unknown compression and archives beyond inspection budgets are kept.
- Path, length, modification time, content hash, and process state are rechecked before action. File handles deny writers while the final action is performed, within Windows sharing semantics.
- Recycle Bin is the default; there is no permanent-delete fallback if recycling fails. Receipts and payload hashes are checked. A receipt failure is reported for manual inspection rather than claimed as a successful verified recycle.
- Restore verifies its receipt and content and refuses to overwrite an existing destination. Retain the local journal and Recycle Bin item if you need recovery.
- Permanent deletion is explicitly selected, cache-only, requires typed confirmation, and is not recoverable through the app.
- Source project folders remain report-only in v1.2.0. Windows Shell refuses folder recycling under the required file protection holds; the application refuses new folder cleanup, including stale/forged eligible rows, instead of removing the holds. Explicit recycling of verified safe exact source ZIP duplicates and restore of existing verified folder receipts remain available, subject to current custom protections.
- No installed-application or unrelated-process termination, uninstall, direct Windows Update removal, whole Recycle Bin emptying, or remote inventory upload is implemented. The best-effort Store metadata query can terminate only the private read-only PowerShell helper that ClearGuard itself started, on cancellation or timeout.
- Installed-application discovery and size measurement are read-only. Registry estimates are not verified disk usage; unknown, shared, overlapping, inaccessible, and cancelled measurements must not be treated as an exact reclaimable total. Uninstall commands recorded by Windows are never executed by this feature.
- Exit during background work cancels and waits for completion. Cancellation is cooperative and may not interrupt an in-progress OS call or an individual file inspection immediately.

## v1.2.0 trust boundaries

### Manual release metadata

Only an explicit update-check action contacts the fixed HTTPS public release endpoint `https://api.github.com/repos/matinmt3/ClearGuard/releases/latest`. The transport uses a 10-second deadline, ordinary static headers, no authentication/credentials or cookies, no redirects, normal certificate validation, and bounded JSON. The parser accepts a stable numeric version and exactly the official release-tag URL. Release notes remain bounded inert plain text; they never authorize commands, files, browser navigation, download, or installation. The official page opens only through a separate user action. Errors/rate limits/timeout do not prove the app is current. See the official [GitHub release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).

The request does not include personal paths, filenames, usernames, inventories, file contents, snapshots, settings, or backups, and there is no telemetry. This is not an anonymity claim: GitHub and network intermediaries see the requesting public IP and ordinary connection metadata; opening the release page follows your browser's normal cookies/account behavior.

### Read-only dashboard and local history

The dashboard reads filesystem entry metadata and logical file lengths, not file contents, after an explicit scan. Default traversal budgets are 200,000 entries, 60 seconds cooperatively checked, 64 levels, and 4,096 categories. Links/reparse points are not traversed, and the owned snapshot directory is excluded. Access errors, links, cancellation, changing entries, ambiguous aliases, or budgets can make results partial. A profile-wide scan commonly encounters Windows compatibility junctions or access restrictions; select a narrower ordinary folder for a complete scope.

Only two complete validated snapshots with identical canonical root, policy, exclusions, and budgets are comparable. Their deltas are arithmetic differences of observed logical lengths, not allocated blocks, reclaimable bytes, or an atomic view of the filesystem. Hard links are counted per path. First/partial/different-scope/invalid-history results show unknown deltas. New snapshots use new local filenames; prior files are retained, and cancellation/failed persistence does not overwrite the prior baseline. The dashboard cannot delete or move scanned files.

### Additive protected folders and settings integrity

The user manually adds an existing normal local folder with a full path. Only app settings are written; protected-folder contents are untouched. Comparison is canonical, case-insensitive, and hierarchy-boundary-aware. Vanished protected folders stay in the list and still block future descendants. A protected descendant blocks the entire encompassing cache or source-folder candidate. Source keepers, organizer source/destination, and restore endpoints are subject to current protections; stale selections are rechecked before mutation. Removing a custom entry requires explicit confirmation and cannot remove a built-in hard protection.

UNC/network/device/relative/drive-relative/ADS/variable/wildcard inputs, ambiguous trailing dot/space components, and linked or uninspectable chains are refused. Any component containing `~` is conservatively refused, including 8.3 aliases and legitimate tilde names, because lexical comparison cannot safely establish their long-path identity after disappearance. ClearGuard's own settings, reports, and dashboard stores remain protected.

Settings reside at `%LOCALAPPDATA%\ClearGuard\settings\protected-folders.json`. Only a bounded known owned schema is accepted; duplicate keys, unknown fields, malformed paths/data, unsupported versions, locked files, and reparse settings fail closed. An initially new missing configuration is allowed, but a previously known primary file that disappears—or a missing primary with evidence of prior settings—blocks operations. Last loaded protections are retained on errors. Valid edits use a cooperative private lock, flushed `CreateNew` temporary file, current-file revalidation, atomic replacement, and uniquely named previous-JSON backups. Unknown/malformed files are never intentionally overwritten or automatically recovered. Repair the local configuration manually; do not discard it to bypass protection. This remains subject to ordinary Windows sharing/path race limitations and does not claim immunity to privileged or hostile concurrent filesystem changes.

Snapshots reside at `%LOCALAPPDATA%\ClearGuard\Dashboard`; reports/receipts at `%LOCALAPPDATA%\ClearGuard\Reports`. These stores and settings/backups contain private local names/paths and must not be published unredacted. Public packages contain sanitized fixture reports and synthetic previews, not user snapshots, settings, backups, raw inventories, or journals.

## Limits and operational precautions

1. Back up irreplaceable data before any operation. Test changes against generated fixtures, never personal directories.
2. Close related applications and scan again. An unrecognized process, permission error, lock, changed file, or uncertain artifact should be skipped.
3. Detection is finite. Unknown encodings, exotic formats, Windows policies, privileged interference, and concurrency can defeat assumptions. Review the candidate list and skip anything you do not understand.
4. Recycle Bin capacity limits and shell policy are not universally tested. The application requests recycling without silently falling back to permanent deletion, but users should still watch for Windows warnings and inspect reported failures.
5. Recycle Bin storage still occupies the drive. ClearGuard never empties it. Logical processed bytes are not the same as newly available disk space.
6. Local journals/reports contain paths, names, hashes, and possibly crash metadata. Do not publish them unredacted. A crash dump can contain sensitive application memory; deleting an old dump also loses debugging evidence.
7. Public validation is local fixture, native-render, and UI interaction evidence. It is not a blanket certification across every installed application, Windows version, live Android Studio/Gradle/Chrome state, ACL-denied directory, or enormous/corrupt archive behavior.

## Report a safety issue

Stop using the affected destructive feature. Keep the operation receipt and recoverable item if possible. Report a minimal synthetic reproduction, the ClearGuard version, Windows version, expected behavior, and actual behavior. Do not attach personal files, secrets, raw crash dumps, or unredacted inventories.

For a vulnerability, use GitHub's private vulnerability reporting or a private maintainer security advisory if available. Do not publish exploit details or personal evidence in a public issue before coordinated disclosure. Ordinary non-sensitive bugs may be filed in repository issues.

## فارسی

ClearGuard پس از انتخاب و تأیید شما می‌تواند فایل محلی را حذف یا جابه‌جا کند؛ جایگزین پشتیبان نیست. بررسی محافظه‌کارانه خطر را کاهش می‌دهد، اما تضمین تشخیص تمام فرمت‌ها و شرایط ویندوز نیست.

- ابتدا اسکن خواندنی و سپس اقدام انتخابی؛ ردیف‌ها پیش‌فرض انتخاب‌نشده‌اند.
- پاک‌سازی محدود به فایلِ شناخته‌شده در فهرست کش‌های مجاز C است؛ مسیر شخصی یا سیستمی هدف حذف عمومی نیست.
- رسانه، دادهٔ حساس، پروژه، آرشیو قابل بررسی و مسیر پیوندی کنترل می‌شود؛ مورد نامطمئن یا خارج از محدودیت بررسی نگه داشته می‌شود.
- قبل از اقدام مسیر، اندازه، زمان، هش و وضعیت برنامه دوباره بررسی می‌شود. برنامهٔ مرتبط را خودتان ببندید و دوباره اسکن کنید.
- Recycle Bin پیش‌فرض است و شکست آن به حذف دائمی تبدیل نمی‌شود. رسید ناموفق را برای بررسی دستی گزارش می‌کند.
- بازگردانی فایل موجود را بازنویسی نمی‌کند. برای بازیابی، رسید محلی و مورد داخل سطل زباله را نگه دارید.
- پوشهٔ پروژه در ۱.۲.۰ همچنان فقط گزارش است. Windows Shell بازیافت پوشه را با قفل‌های حفاظتی لازم رد می‌کند؛ قفل‌ها ضعیف نمی‌شوند و ردیف قدیمی/دست‌کاری‌شدهٔ پوشه هم مجوز حذف ندارد. ZIP سورسِ کاملاً یکسان و تأییدشده انتخابی بازیافت می‌شود و بازگردانی رسید معتبر قبلی پوشه، با رعایت حفاظت جاری، حفظ شده است.
- حذف دائمی فقط کشِ مجاز با تأیید تایپی است؛ قابل بازیابی نیست. برنامه سطل زباله را تخلیه، برنامه را Uninstall یا برنامهٔ نصب‌شده/فرایند نامرتبط را به‌اجبار متوقف نمی‌کند. تنها helper خواندنیِ PowerShell که خود ClearGuard برای پرس‌وجوی Store اجرا کرده ممکن است هنگام لغو یا پایان مهلت متوقف شود.
- انتقال به سطل زباله فضای همان درایو را آزاد نمی‌کند؛ اندازهٔ منطقی پردازش با فضای واقعاً آزادشده متفاوت است.
- بخش برنامه‌های نصب‌شده فقط خواندنی است؛ حجم Registry تخمینی است و نامشخص/ناقص یا هم‌پوشان، حجم قطعی قابل آزادسازی نیست. فرمان Uninstall اجرا نمی‌شود.
- خروج حین کار به‌صورت درخواست توقف و انتظار امن است؛ فراخوانی جاری ویندوز یا بررسی یک فایل ممکن است فوراً متوقف نشود.
- گزارش و Dump می‌تواند اطلاعات خصوصی داشته باشد؛ آن را بدون بازبینی منتشر نکنید. حذف Dump امکان تحلیل خرابی گذشته را از بین می‌برد.
- آزمون‌ها روی دادهٔ مصنوعی، رندر و تعامل بومی محلی انجام شده‌اند؛ تمام برنامه‌های نصب‌شده، نسخه‌های ویندوز، فرایندهای زنده، ACL و سیاست ظرفیت سطل زباله پوشش کامل ندارند.

### مرزهای تازهٔ ۱.۲.۰

- بررسی نسخه فقط با درخواست دستی شما به API ثابت HTTPS آخرین انتشار GitHub متصل می‌شود؛ ۱۰ ثانیه مهلت، Header عادی ثابت، بدون احراز هویت/Cookie یا دنبال کردن تغییرمسیر. پاسخ محدود، نسخهٔ پایدار و نشانی رسمی بررسی می‌شود؛ یادداشت‌ها متن ساده‌اند و فرمان/HTML/لینک اجرا نمی‌شوند. باز کردن صفحهٔ رسمی اقدام جداگانه می‌خواهد؛ دریافت، نصب یا اجرای خودکار وجود ندارد. خطا یعنی وضعیت نامشخص، نه تأیید به‌روز بودن.
- مسیر، نام فایل/کاربر، فهرست، محتوا، snapshot، تنظیمات و backup برای GitHub ارسال نمی‌شود؛ telemetry ندارد. بااین‌حال GitHub و واسطهٔ شبکه IP عمومی و اطلاعات عادی اتصال را می‌بینند. مرورگرِ صفحهٔ انتشار رفتار عادی Cookie/حساب خودش را دارد.
- داشبورد فقط با اسکن انتخابی، مشخصات و طول منطقی فایل را می‌خواند؛ محتوای فایل باز نمی‌شود. سقف پیش‌فرض ۲۰۰٬۰۰۰ ورودی، ۶۰ ثانیهٔ تعاونی، ۶۴ سطح و ۴٬۰۹۶ دسته است. مسیر پیوندی دنبال و snapshot خود برنامه شمرده نمی‌شود. خطای دسترسی یا لینک در پروفایل معمول است؛ برای نتیجهٔ کامل مسیر کوچک‌تر انتخاب کنید.
- اختلاف فقط میان دو snapshot کامل و معتبر با ریشه/سیاست/استثنا/سقف یکسان است؛ جمع منطقیِ مشاهده‌شده، نه فضای فیزیکی یا قابل آزادسازی. hardlink به‌ازای مسیر شمرده و فایل ممکن است حین پیمایش تغییر کند. اسکن ناقص، اول، نامتجانس یا تاریخچهٔ خراب اختلاف قطعی ندارد؛ فایل قبلی حفظ می‌شود و لغو/خطای ذخیره خط مبنا را بازنویسی نمی‌کند.
- حفاظت سفارشی فقط به قواعد ثابت اضافه می‌شود. افزودن، پوشهٔ محلی موجود و مسیر کامل می‌خواهد و محتوا را دست نمی‌زند؛ حذف ورودی سفارشی تأیید جداگانه می‌خواهد. پوشهٔ ناپدیدشده محافظت می‌ماند؛ هم‌پوشانی زیرپوشه، کل ردیف کش/پوشهٔ سورس، نسخهٔ نگه‌داری‌شده، مبدأ/مقصد دسته‌بندی و بازگردانی را مسدود می‌کند. ردیف قدیمی پیش از اقدام دوباره کنترل می‌شود.
- مسیر نسبی/شبکه/UNC/device/ADS، متغیر بازنشده، wildcard، جزء مبهم نقطه/فاصله و زنجیرهٔ پیوندی یا غیرقابل‌بررسی رد می‌شوند. هر جزء دارای `~`، شامل نام کوتاه 8.3 و نام واقعی دارای tilde، محافظه‌کارانه رد می‌شود.
- تنظیمات در `%LOCALAPPDATA%\ClearGuard\settings\protected-folders.json` با قالب محدود و شناخته‌شده است. کلید تکراری، قالب/نسخهٔ نامعلوم، فایل خراب، قفل، پیوند یا فایلِ قبلاً شناخته‌شدهٔ ناپدیدشده fail-closed است؛ عملیات فایل/تغییر تنظیمات مسدود و فهرست آخر حفظ می‌شود. تنظیمات معتبر با قفل تعاونی، فایل موقت تازه و flush، بازبینی پیش از جایگزینی atomic و backup یکتای JSON قبلی ذخیره می‌شود. فایل خراب خودکار بازیابی/بازنویسی نمی‌شود؛ دستی ترمیم کنید. محدودیت‌های معمول رقابت مسیر/اشتراک‌گذاری ویندوز و دخالت فرایند مخرب یا دارای امتیاز همچنان وجود دارد.
- snapshot در `%LOCALAPPDATA%\ClearGuard\Dashboard` و گزارش/رسید در `%LOCALAPPDATA%\ClearGuard\Reports` محلی و خصوصی‌اند؛ همراه تنظیمات/backup بدون پوشاندن اطلاعات منتشر نشوند. بستهٔ انتشار فقط گزارش مصنوعیِ پالایش‌شده و پیش‌نمایش مصنوعی دارد.
- فایل‌های انتشار ۱.۱.۰ حفظ می‌شوند، اما نسخهٔ قدیمی حفاظت سفارشی ۱.۲.۰ را نمی‌شناسد. downgrade برای پاک‌سازی، دسته‌بندی یا بازگردانی مسیر محافظت‌شده ایمن فرض نمی‌شود؛ اقدام تغییردهنده را متوقف و تنظیمات/رسید/snapshot را حفظ کنید.

برای مشکل ایمنی، استفاده از بخش حذف مرتبط را متوقف کنید و رسید/مورد قابل بازیابی را حفظ کنید. گزارش را با نمونهٔ مصنوعی و بدون فایل شخصی، کلید، رمز، Dump خام یا فهرست محلیِ بدون پوشاندن اطلاعات ارائه دهید. برای آسیب‌پذیری از گزارش خصوصی امنیتی GitHub، در صورت فعال بودن، استفاده کنید.
