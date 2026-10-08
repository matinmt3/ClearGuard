# Validation and preview / اعتبارسنجی و پیش‌نمایش

v1.2.0: **171 safety tests / 36 native interaction checks / 11 page renders passed; no failures, skips or real-user cleanup.** Exact executable SHA-256 is recorded in both reports.

- [Persian interface preview / پیش‌نمایش رابط فارسی](Preview.png)
- [Installed applications with synthetic demo data / برنامه‌ها با دادهٔ مصنوعی نمایشی](InstalledApps.png)
- [Space dashboard with synthetic snapshots / داشبورد با snapshot مصنوعی](Dashboard.png)
- [Manual updates with synthetic release data / بررسی نسخه با انتشار مصنوعی](Updates.png)
- [Legacy and added isolated-fixture safety tests / آزمون‌های ایمنی قبلی و جدید](Safety-Test-Report.json)
- [Eleven native page renders and UI interaction checks / رندر یازده صفحه و آزمون تعامل بومی](UI-Test-Report.json)

The v1.2.0 reports are sanitized public summaries tied to the exact executable SHA-256. All safety/UI gates must pass without failures or skips before packaging. Every original safety case remains, alongside installed-app, manual-updater, dashboard-comparability/budget, protected-path, and stale-selection regressions. The UI report covers all eleven pages and fixture-backed interaction checks, including idle/busy Exit and the non-destructive new pages. Final counts are in the reports; no raw local paths, user inventories, or private exception details are included. Automated updater cases use injected responses, not public network calls; a separately performed live check is identified separately. These tests are not exhaustive certification; see both reports' `Gaps` and [SECURITY.md](../SECURITY.md).

All preview names, paths, application records, release notes, sizes, and deltas are synthetic demonstration data. `Dashboard.png` is not a real filesystem scan; `Updates.png` is not a live release check. User snapshots, journals, protection settings/backups, and raw diagnostics remain private local data and are not published. The preserved [v1.1.0 release](https://github.com/matinmt3/ClearGuard/releases/tag/v1.1.0) has its own historical reports; it does not enforce v1.2.0 custom folder protections.

گزارش‌های ۱.۲.۰ خلاصهٔ عمومی و بدون مسیر خصوصی‌اند و به هش همان فایل اجرایی اشاره دارند؛ همهٔ دروازه‌های ایمنی/رابط باید بدون شکست/ردشدن موفق باشند. آزمون‌های قبلی حفظ و بررسی نسخه، داشبورد، حفاظت و ردیف قدیمی اضافه شده‌اند؛ یازده صفحه و تعامل بومی بررسی می‌شود. تعداد نهایی در گزارش است. آزمون خودکار نسخه پاسخ تزریقی دارد و شبکهٔ عمومی را درخواست نمی‌کند؛ بررسی زندهٔ جداگانه جدا مشخص می‌شود. هیچ فایل واقعی کاربر در آزمون‌ها پاک نشده است. محدودیت‌ها در `Gaps` و راهنمای امنیت آمده‌اند.

تمام نام‌ها، مسیرها، اطلاعات برنامه/انتشار، حجم و اختلاف در تصویرها مصنوعی‌اند؛ داشبورد اسکن واقعی و صفحهٔ نسخه بررسی زنده نیست. snapshot، رسید، تنظیمات حفاظت/backup و خطای خام خصوصی منتشر نمی‌شوند. گزارش تاریخی انتشار ۱.۱.۰ حفظ می‌شود، اما آن نسخه حفاظت سفارشی جدید را نمی‌شناسد.
