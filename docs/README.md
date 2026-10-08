# Validation and preview / اعتبارسنجی و پیش‌نمایش

- [Persian interface preview / پیش‌نمایش رابط فارسی](Preview.png)
- [Installed applications with synthetic demo data / برنامه‌ها با دادهٔ مصنوعی نمایشی](InstalledApps.png)
- [Legacy and added isolated-fixture safety tests / آزمون‌های ایمنی قبلی و جدید](Safety-Test-Report.json)
- [Eight native page renders and UI interaction checks / رندر هشت صفحه و آزمون تعامل بومی](UI-Test-Report.json)

The v1.1.0 reports are sanitized public summaries tied to the published executable SHA-256. The safety report must retain every original safety case and include added regressions. The UI report includes all eight pages plus fixture-backed interaction checks, including idle/busy Exit and the read-only installed-app grid. Exact current counts are in the reports; no raw local paths, user inventories, or private exception details are included. These tests are not exhaustive certification; see both reports' `Gaps` and [SECURITY.md](../SECURITY.md).

گزارش‌ها خلاصهٔ عمومی و بدون مسیر خصوصی‌اند و به هش فایل اجرایی اشاره دارند. هیچ فایل واقعی کاربر در آزمون‌ها پاک نشده است. محدودیت‌ها در `Gaps` گزارش ایمنی و راهنمای امنیت آمده‌اند.
