using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web;
namespace ClearGuard {
 public static class ReportExport {
  private static string H(string text){return HttpUtility.HtmlEncode(text??"");}
  private static string C(string text){text=text??"";string significant=text.TrimStart();if((text.Length>0&&"\t\r\n".IndexOf(text[0])>=0)||(significant.Length>0&&"=+-@".IndexOf(significant[0])>=0))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
  private static string Start(string title){return "<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>"+H(title)+"</title><style>body{background:#101820;color:#f1f7f8;font:16px Segoe UI,Tahoma,sans-serif;max-width:1400px;margin:40px auto;padding:0 24px}h1{color:#59e3c0}table{width:100%;border-collapse:collapse;background:#17242f}td,th{padding:14px;border-bottom:1px solid #33444f;text-align:right;vertical-align:top}small,p{color:#b3c5ce}code{direction:ltr;display:block;overflow-wrap:anywhere;font-size:13px}th{color:#59e3c0}aside{background:#223440;border-radius:14px;padding:20px;margin:24px 0}</style><h1>"+H(title)+"</h1>";}
  public static void Scan(string path,ScanResult r,bool createNew=false){StringBuilder b=new StringBuilder(Start("ClearGuard · گزارش اسکن"));b.Append("<p>زمان: "+r.CompletedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")+" · فقط اسکن؛ هیچ حذف خودکاری انجام نشده است.</p><aside>قابل پاک‌سازی مجاز: "+Format.Bytes(r.Rows.Where(x=>x.Eligible).Sum(x=>x.SafeBytes))+"<br>نیازمند تصمیم / محافظت: "+Format.Bytes(r.Rows.Where(x=>!x.Eligible&&x.Category!="LargeFile").Sum(x=>x.Bytes))+"<br>فضای آزاد قبل: "+Format.Bytes(r.FreeBefore)+"<br>ردیف‌های LargeFile جزئیات زیرمجموعهٔ پوشه‌ها هستند؛ در جمع محافظت دوباره شمرده نمی‌شوند. این جمع اندازهٔ کل درایو نیست.</aside>");b.Append("<h2>۱۰ مورد بزرگ‌تر</h2><ol>");foreach(ScanRow row in r.Rows.OrderByDescending(x=>x.Bytes).Take(10))b.Append("<li>"+H(row.Name)+" · "+row.Bytes.ToString("N0")+" bytes<code>"+H(row.Path)+"</code></li>");b.Append("</ol><h2>تمام موارد</h2><table><tr><th>مورد / برنامه</th><th>مسیر</th><th>حجم دقیق</th><th>وضعیت</th><th>اثر و شرط</th></tr>");foreach(ScanRow row in r.Rows.OrderByDescending(x=>x.Bytes)){b.Append("<tr><td>"+H(row.Name)+"<br>"+H(row.App)+"</td><td><code>"+H(row.Path)+"</code></td><td>"+row.Bytes.ToString("N0")+" B<br>مجاز: "+row.SafeBytes.ToString("N0")+" B</td><td>"+H(row.Status)+"</td><td>"+H(row.Effect)+"<br>"+H(row.BlockReason)+"<code>"+H(row.Keeper)+"</code></td></tr>");}b.Append("</table><h2>محدودیت‌ها / هشدارها</h2><ul>");foreach(string warn in r.Warnings)b.Append("<li>"+H(warn)+"</li>");b.Append("</ul></html>");WriteReport(path,b.ToString(),new UTF8Encoding(false),createNew);}
  public static void Operation(string path,OperationReport r,bool createNew=false){StringBuilder b=new StringBuilder(Start("ClearGuard · رسید عملیات"));string mode=r.Permanent?"حذف دائمی؛ غیرقابل بازگردانی":(r.Kind??"").StartsWith("organize")?"دسته‌بندی / بازگردانی روی همان درایو؛ فایل موجود بازنویسی نشده است.":(r.Kind??"").IndexOf("restore",StringComparison.OrdinalIgnoreCase)>=0?"بازیابی موارد همین رسید؛ بدون بازنویسی.":"سطل زباله تخلیه نشده؛ انتقال به سطل به‌تنهایی فضا آزاد نمی‌کند.";b.Append("<aside>شناسه: "+H(r.Id)+"<br>حجم دقیق پردازش‌شده: "+r.ProcessedBytes.ToString("N0")+" bytes<br>فضای آزاد قبل: "+r.FreeBefore.ToString("N0")+" bytes<br>بعد: "+r.FreeAfter.ToString("N0")+" bytes<br>تغییر واقعی: "+(r.FreeAfter-r.FreeBefore).ToString("N0")+" bytes<br>"+mode+"</aside><table><tr><th>مسیر اصلی</th><th>وضعیت</th><th>حجم</th><th>جزئیات</th></tr>");foreach(OperationEntry e in r.Entries)b.Append("<tr><td><code>"+H(e.OriginalPath)+"</code></td><td>"+H(e.Status)+"</td><td>"+e.Bytes.ToString("N0")+" B</td><td>"+H(e.Detail)+"<code>"+H(e.DestinationPath)+"</code></td></tr>");b.Append("</table></html>");WriteReport(path,b.ToString(),new UTF8Encoding(false),createNew);}
  public static void Csv(string path,ScanResult r,bool createNew=false){StringBuilder b=new StringBuilder("Name,Path,Bytes,SafeBytes,Application,Status,Effect,Reason,Keeper\r\n");foreach(ScanRow row in r.Rows.OrderByDescending(x=>x.Bytes))b.Append(String.Join(",",new[]{row.Name,row.Path,row.Bytes.ToString(),row.SafeBytes.ToString(),row.App,row.Status,row.Effect,row.BlockReason,row.Keeper}.Select(C))+"\r\n");WriteReport(path,b.ToString(),new UTF8Encoding(true),createNew);}
  public static void Apps(string path,InstalledAppsResult r,bool createNew=false){StringBuilder b=new StringBuilder(Start("ClearGuard · برنامه‌های نصب‌شده"));b.Append("<aside>فقط خواندنی؛ حذف نصب انجام نمی‌شود. حجم ثبت‌شدهٔ ویندوز تخمینی است؛ اندازهٔ پوشه، مصرف واقعی کل برنامه نیست. فایل‌های مشترک و داده‌های بیرون پوشه دوباره‌شماری یا کم‌شماری ایجاد می‌کنند. حجم نامشخص، صفر محسوب نمی‌شود.</aside><table><tr><th>برنامه / نسخه</th><th>ناشر</th><th>حجم / منشأ</th><th>مسیر / منبع</th><th>محدودیت</th></tr>");foreach(InstalledAppRow row in r.Apps.OrderByDescending(x=>x.SizeBytes.HasValue).ThenByDescending(x=>x.SizeBytes)){b.Append("<tr><td>"+H(row.Name)+"<br>"+H(row.Version)+"</td><td>"+H(row.Publisher)+"</td><td>"+H(row.SizeDisplay)+"<br>"+H(row.SizeKind)+"</td><td><code>"+H(row.InstallLocation)+"</code>"+H(row.Source)+"</td><td>"+H(row.SizeNote)+"<br>"+H(row.MeasureBlockReason)+"</td></tr>");}b.Append("</table><h2>محدودیت‌ها</h2><ul>");foreach(string warning in r.Warnings)b.Append("<li>"+H(warning)+"</li>");b.Append("</ul></html>");WriteReport(path,b.ToString(),new UTF8Encoding(false),createNew);}
  public static void AppsCsv(string path,InstalledAppsResult r,bool createNew=false){StringBuilder b=new StringBuilder("Name,Version,Publisher,InstallLocation,Source,Architecture,SizeBytes,SizeKind,SizeNote\r\n");foreach(InstalledAppRow row in r.Apps.OrderByDescending(x=>x.SizeBytes.HasValue).ThenByDescending(x=>x.SizeBytes))b.Append(String.Join(",",new[]{row.Name,row.Version,row.Publisher,row.InstallLocation,row.Source,row.Architecture,row.SizeBytes.HasValue?row.SizeBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"",row.SizeKind,row.SizeNote}.Select(C))+"\r\n");WriteReport(path,b.ToString(),new UTF8Encoding(true),createNew);}
  // User-selected report exports never replace a file. Existing owned operation journals retain their explicit overwrite mode.
  public static void SaveNewScanBundle(string path,ScanResult r){
   if(r==null || r.Rows==null || r.Warnings==null)throw new ArgumentException("Scan report data is incomplete.");
   string[] targets=NewBundlePaths(path);string json=Format.Json(r);
   WriteReport(targets[0],json,new UTF8Encoding(false),true);
   Scan(targets[1],r,true);Csv(targets[2],r,true);
  }
  public static void SaveNewAppsBundle(string path,InstalledAppsResult r){
   if(r==null || r.Apps==null || r.Warnings==null)throw new ArgumentException("Installed application report data is incomplete.");
   string[] targets=NewBundlePaths(path);string json=Format.Json(r);
   WriteReport(targets[0],json,new UTF8Encoding(false),true);
   Apps(targets[1],r,true);AppsCsv(targets[2],r,true);
  }
  private static string[] NewBundlePaths(string path){
   if(String.IsNullOrWhiteSpace(path))throw new IOException("برای ذخیرهٔ گزارش، مسیر تازه با پسوند .json انتخاب کنید.");
   string json=Path.GetFullPath(path);
   if(!String.Equals(Path.GetExtension(json),".json",StringComparison.OrdinalIgnoreCase))throw new IOException("گزارش سه‌فرمتی باید با نام تازه و پسوند .json ذخیره شود؛ پسوند HTML یا CSV ممکن است روی همان فایل بنویسد.");
   string[] targets={json,Path.ChangeExtension(json,"html"),Path.ChangeExtension(json,"csv")};
   if(targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)throw new IOException("مسیرهای گزارش باید مجزا باشند.");
   if(!Directory.Exists(Path.GetDirectoryName(json)))throw new DirectoryNotFoundException("پوشهٔ گزارش وجود ندارد؛ مسیر معتبر دیگری انتخاب کنید.");
   foreach(string target in targets)if(TargetExists(target))throw new IOException("گزارش ذخیره نشد: فایل یا پوشهٔ JSON / HTML / CSV هم‌نام وجود دارد. نام تازه انتخاب کنید؛ چیزی بازنویسی نمی‌شود.");
   return targets;
  }
  private static bool TargetExists(string path){
   try{File.GetAttributes(path);return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
  }
  private static void WriteReport(string path,string text,Encoding encoding,bool createNew){
   if(!createNew){File.WriteAllText(path,text,encoding);return;}
   // CreateNew, not a check-then-Create, is the final protection against a target appearing after preflight.
   // A later error can leave newly created partial report files. They are not deleted or overwritten automatically.
   using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
   using(var writer=new StreamWriter(file,encoding)){writer.Write(text);}
  }

 }
}
