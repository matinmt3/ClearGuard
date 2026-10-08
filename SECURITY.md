# Safety and security / ایمنی و امنیت

ClearGuard can mutate local files after explicit user approval. Treat it as a safety-sensitive tool, not a substitute for backups. Its conservative checks reduce risk but cannot prove every possible file safe.

## Supported release

The published v1.0.0 source and release assets document the current implementation. There is no automatic update or cleanup service. Use the official repository/release, verify checksums, and do not disable Windows protection or antivirus. The executable is not digitally signed.

## Safety model

- Read-only scans and explicit operation selection are separate. Default selections are empty.
- Cleanup accepts only recognized per-file candidates within its C-drive cache allowlist. Personal/system locations are not general deletion targets.
- Media extensions, recognized content signatures, sensitive files, supported archive contents, project ancestry, and reparse points are checked. Unknown compression and archives beyond inspection budgets are kept.
- Path, length, modification time, content hash, and process state are rechecked before action. File handles deny writers while the final action is performed, within Windows sharing semantics.
- Recycle Bin is the default; there is no permanent-delete fallback if recycling fails. Receipts and payload hashes are checked. A receipt failure is reported for manual inspection rather than claimed as a successful verified recycle.
- Restore verifies its receipt and content and refuses to overwrite an existing destination. Retain the local journal and Recycle Bin item if you need recovery.
- Permanent deletion is explicitly selected, cache-only, requires typed confirmation, and is not recoverable through the app.
- No process termination, uninstall, direct Windows Update removal, whole Recycle Bin emptying, or remote inventory upload is implemented.

## Limits and operational precautions

1. Back up irreplaceable data before any operation. Test changes against generated fixtures, never personal directories.
2. Close related applications and scan again. An unrecognized process, permission error, lock, changed file, or uncertain artifact should be skipped.
3. Detection is finite. Unknown encodings, exotic formats, Windows policies, privileged interference, and concurrency can defeat assumptions. Review the candidate list and skip anything you do not understand.
4. Recycle Bin capacity limits and shell policy are not universally tested. The application requests recycling without silently falling back to permanent deletion, but users should still watch for Windows warnings and inspect reported failures.
5. Recycle Bin storage still occupies the drive. ClearGuard never empties it. Logical processed bytes are not the same as newly available disk space.
6. Local journals/reports contain paths, names, hashes, and possibly crash metadata. Do not publish them unredacted. A crash dump can contain sensitive application memory; deleting an old dump also loses debugging evidence.
7. Public validation is local fixture and native-render evidence. It is not a blanket certification across Windows versions, live Android Studio/Gradle/Chrome states, ACL-denied directories, or enormous/corrupt archive behavior.

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
- حذف دائمی فقط کشِ مجاز با تأیید تایپی است؛ قابل بازیابی نیست. برنامه سطل زباله را تخلیه، برنامه را Uninstall یا فرایندی را به‌اجبار متوقف نمی‌کند.
- انتقال به سطل زباله فضای همان درایو را آزاد نمی‌کند؛ اندازهٔ منطقی پردازش با فضای واقعاً آزادشده متفاوت است.
- گزارش و Dump می‌تواند اطلاعات خصوصی داشته باشد؛ آن را بدون بازبینی منتشر نکنید. حذف Dump امکان تحلیل خرابی گذشته را از بین می‌برد.
- آزمون‌ها روی دادهٔ مصنوعی و رندر محلی انجام شده‌اند؛ همهٔ نسخه‌های ویندوز، فرایندهای زنده، ACL و سیاست ظرفیت سطل زباله پوشش کامل ندارند.

برای مشکل ایمنی، استفاده از بخش حذف مرتبط را متوقف کنید و رسید/مورد قابل بازیابی را حفظ کنید. گزارش را با نمونهٔ مصنوعی و بدون فایل شخصی، کلید، رمز، Dump خام یا فهرست محلیِ بدون پوشاندن اطلاعات ارائه دهید. برای آسیب‌پذیری از گزارش خصوصی امنیتی GitHub، در صورت فعال بودن، استفاده کنید.
