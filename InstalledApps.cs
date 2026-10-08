using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ClearGuard {
 public sealed class InstalledAppRow {
  public string Name {get;set;} public string Version {get;set;} public string Publisher {get;set;}
  public string InstallLocation {get;set;} public string Source {get;set;} public string Architecture {get;set;}
  public long? SizeBytes {get;set;} public string SizeKind {get;set;} public string SizeNote {get;set;}
  public string Identifier {get;set;} public bool CanMeasure {get;set;} public string MeasureBlockReason {get;set;}
  public string SizeDisplay {get {return !SizeBytes.HasValue ? "نامشخص" : Format.Bytes(SizeBytes.Value)+(SizeKind=="MeasuredFolder" ? " (پوشه)" : " (تخمینی)");}}
  public string SizeKindDisplay {get {return SizeKind=="Estimated" ? "اعلام نصب‌کننده" : SizeKind=="MeasuredFolder" ? "اندازه‌گیری پوشه" : "نامشخص";}}
 }
 public sealed class InstalledAppsResult {
  public List<InstalledAppRow> Apps {get;set;} public List<string> Warnings {get;set;} public DateTime ScannedUtc {get;set;}
  public int HiddenComponentCount {get;set;} public int DuplicateRegistrationCount {get;set;}
  public InstalledAppsResult(){Apps=new List<InstalledAppRow>();Warnings=new List<string>();ScannedUtc=DateTime.UtcNow;}
 }
 // Metadata only: executable paths, uninstall strings, repair commands, and URLs are intentionally absent.
 public sealed class InstalledAppRecord {
  public string DisplayName {get;set;} public string Version {get;set;} public string Publisher {get;set;}
  public string InstallLocation {get;set;} public string Source {get;set;} public string Architecture {get;set;}
  public string RegistrationKey {get;set;} public string Hive {get;set;} public object EstimatedSize {get;set;}
  public RegistryValueKind EstimatedSizeKind {get;set;} public bool SystemComponent {get;set;}
  public string ParentKeyName {get;set;} public string ReleaseType {get;set;}
  public bool IsFramework {get;set;} public bool IsResourcePackage {get;set;}
 }
 public static class InstalledAppsScanner {
  private const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
  private const int MaxRecords=50000;
  private const int MaxStoreChars=4*1024*1024;
  // This immutable command contains no registry, file, or UI data. It enumerates only the current user.
  private const string StoreScript="$ErrorActionPreference='Stop'; [Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); $ProgressPreference='SilentlyContinue'; $module=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\\v1.0\\Modules\\Appx\\Appx.psd1'; Import-Module -Name $module -ErrorAction Stop; $packages=@(Appx\\Get-AppxPackage -ErrorAction Stop | Select-Object Name,@{Name='Version';Expression={$_.Version.ToString()}},Publisher,InstallLocation,PackageFullName,@{Name='Architecture';Expression={$_.Architecture.ToString()}},IsFramework,IsResourcePackage); ConvertTo-Json -InputObject $packages -Depth 3 -Compress";

  public static InstalledAppsResult Scan(CancellationToken token,Action<string> progress) {
   token.ThrowIfCancellationRequested();var records=new List<InstalledAppRecord>();var warnings=new List<string>();
   RegistryHive[] hives={RegistryHive.LocalMachine,RegistryHive.CurrentUser};
   RegistryView[] views=Environment.Is64BitOperatingSystem ? new[]{RegistryView.Registry64,RegistryView.Registry32} : new[]{RegistryView.Registry32};
   foreach(var hive in hives)foreach(var view in views) {
    token.ThrowIfCancellationRequested();if(progress!=null)progress("خواندن فهرست برنامه‌ها: "+(hive==RegistryHive.LocalMachine ? "HKLM" : "HKCU")+" / "+(view==RegistryView.Registry64 ? "64" : "32"));
    ReadRegistry(hive,view,records,warnings,token);
   }
   token.ThrowIfCancellationRequested();if(progress!=null)progress("خواندن بسته‌های Microsoft Store کاربر فعلی؛ بدون تغییر سیستم");
   try {records.AddRange(ParseStoreJson(ReadStoreJson(token),token));}
   catch(OperationCanceledException){throw;}
   catch(Exception e) {if(!IsReadFailure(e) && !(e is System.ComponentModel.Win32Exception) && !(e is InvalidOperationException) && !(e is TimeoutException) && !(e is AggregateException))throw;warnings.Add("فهرست Microsoft Store کامل نشد ("+e.GetType().Name+"). برنامه‌های Registry همچنان نمایش داده می‌شوند؛ هیچ دسترسی مدیریتی یا تغییر سیستمی انجام نشد.");}
   var result=ConvertRecords(records,token);result.Warnings.InsertRange(0,warnings);result.ScannedUtc=DateTime.UtcNow;
   return result;
  }
  private static void ReadRegistry(RegistryHive hive,RegistryView view,List<InstalledAppRecord> records,List<string> warnings,CancellationToken token) {
   string scope=hive==RegistryHive.LocalMachine ? "HKLM" : "HKCU";
   try {
    using(var root=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=root.OpenSubKey(UninstallKey,false)) {
     if(uninstall==null)return;int read=0;
     foreach(string name in uninstall.GetSubKeyNames()) {
      token.ThrowIfCancellationRequested();if(++read>10000 || records.Count>=MaxRecords){warnings.Add("برای جلوگیری از اسکن نامحدود، تعداد ثبت‌های "+scope+" محدود شد؛ فهرست ممکن است ناقص باشد.");break;}
      try {using(var key=uninstall.OpenSubKey(name,false)) {
       if(key==null)continue;object estimate=key.GetValue("EstimatedSize",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
       records.Add(new InstalledAppRecord{DisplayName=RegistryText(key,"DisplayName"),Version=RegistryText(key,"DisplayVersion"),Publisher=RegistryText(key,"Publisher"),InstallLocation=RegistryText(key,"InstallLocation"),RegistrationKey=Clean(name,1024),Hive=scope,Source="Registry",Architecture=view==RegistryView.Registry64 ? "64-bit registry" : "32-bit registry",EstimatedSize=estimate,EstimatedSizeKind=estimate==null ? RegistryValueKind.Unknown : key.GetValueKind("EstimatedSize"),SystemComponent=IsOne(key.GetValue("SystemComponent")),ParentKeyName=RegistryText(key,"ParentKeyName"),ReleaseType=RegistryText(key,"ReleaseType")});
      }}catch(Exception e){if(!IsReadFailure(e))throw;warnings.Add("یک ثبت برنامه در "+scope+" قابل خواندن نبود ("+e.GetType().Name+").");}
     }
    }
   }catch(Exception e){if(!IsReadFailure(e) && !(e is PlatformNotSupportedException))throw;warnings.Add("نمای "+scope+" / "+view+" قابل خواندن نبود ("+e.GetType().Name+").");}
  }
  private static string RegistryText(RegistryKey key,string name){object value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);return value is string ? Clean((string)value,name=="InstallLocation" ? 4096 : 1024) : "";}
  private static bool IsOne(object value){return (value is int && (int)value==1) || (value is uint && (uint)value==1) || (value is string && (string)value=="1");}
  public static long? ParseEstimatedSize(object value,RegistryValueKind kind) {
   if(kind!=RegistryValueKind.DWord || value==null)return null;ulong kib;
   if(value is int)kib=unchecked((uint)(int)value);
   else if(value is uint)kib=(uint)value;
   else if(value is long && (long)value>=0 && (long)value<=UInt32.MaxValue)kib=(ulong)(long)value;
   else return null;
   return kib==0 ? (long?)null : checked((long)kib*1024L);
  }
  public static InstalledAppsResult ConvertRecords(IEnumerable<InstalledAppRecord> records,CancellationToken token) {
   token.ThrowIfCancellationRequested();if(records==null)throw new ArgumentNullException("records");var result=new InstalledAppsResult();var seen=new Dictionary<string,InstalledAppRow>(StringComparer.OrdinalIgnoreCase);int count=0;
   foreach(var record in records) {
    token.ThrowIfCancellationRequested();if(++count>MaxRecords)throw new InvalidDataException("Installed app metadata exceeds the bounded record limit.");if(record==null)continue;
    if(record.SystemComponent || !String.IsNullOrWhiteSpace(record.ParentKeyName) || record.IsFramework || record.IsResourcePackage || IsUpdate(record.ReleaseType)){result.HiddenComponentCount++;continue;}
    string name=Clean(record.DisplayName,1024);if(String.IsNullOrWhiteSpace(name))continue;
    string version=Clean(record.Version,1024),publisher=Clean(record.Publisher,1024),location=Clean(record.InstallLocation,4096),source=Clean(record.Source,1024);if(source!="Microsoft Store")source="Registry";
    string registration=Clean(record.RegistrationKey,1024),hive=Clean(record.Hive,32);string identity=source+"\n"+hive+"\n"+registration+"\n"+name+"\n"+version+"\n"+publisher+"\n"+location;
    InstalledAppRow prior;
    if(seen.TryGetValue(identity,out prior)) {
     result.DuplicateRegistrationCount++;string architecture=Clean(record.Architecture,128);if(!prior.Architecture.Split('/').Any(x=>x.Trim().Equals(architecture,StringComparison.OrdinalIgnoreCase)))prior.Architecture+=" / "+architecture;
     if(!prior.SizeBytes.HasValue){var size=ParseEstimatedSize(record.EstimatedSize,record.EstimatedSizeKind);if(size.HasValue){prior.SizeBytes=size;prior.SizeKind="Estimated";prior.SizeNote=EstimatedNote;}}
     continue;
    }
    long? estimated=source=="Registry" ? ParseEstimatedSize(record.EstimatedSize,record.EstimatedSizeKind) : null;string reason;
    var row=new InstalledAppRow{Name=name,Version=version,Publisher=publisher,InstallLocation=location,Source=source+(source=="Registry" ? " · "+hive : ""),Architecture=Clean(record.Architecture,128),SizeBytes=estimated,SizeKind=estimated.HasValue ? "Estimated" : "Unknown",SizeNote=estimated.HasValue ? EstimatedNote : "اندازهٔ معتبر در اطلاعات نصب ثبت نشده است؛ نامشخص به معنی صفر نیست.",Identifier=Hash(identity)};
    row.CanMeasure=CanMeasureLocation(location,out reason);row.MeasureBlockReason=reason;result.Apps.Add(row);seen.Add(identity,row);
   }
   if(result.HiddenComponentCount>0)result.Warnings.Add(result.HiddenComponentCount.ToString(CultureInfo.InvariantCulture)+" مؤلفهٔ سیستمی، چارچوب Store، بستهٔ منبع یا فرزندِ به‌روزرسانی از فهرست اصلی پنهان شده است؛ چیزی حذف نشد.");
   if(result.DuplicateRegistrationCount>0)result.Warnings.Add(result.DuplicateRegistrationCount.ToString(CultureInfo.InvariantCulture)+" ثبت یکسان در نماهای Registry ادغام شد؛ نسخه‌های متفاوت حفظ شدند.");
   WarnOverlaps(result,token);result.Apps=result.Apps.OrderByDescending(x=>x.SizeBytes.HasValue).ThenByDescending(x=>x.SizeBytes.GetValueOrDefault()).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();return result;
  }
  private const string EstimatedNote="حجم تخمینی گزارش‌شده توسط نصب‌کننده (EstimatedSize × 1024). فایل‌های مشترک، وابستگی‌ها و داده‌های کاربر ممکن است داخل آن نباشند یا دوباره شمرده شوند؛ حجم دقیق اشغال‌شدهٔ دیسک نیست.";
  private static bool IsUpdate(string value){string text=Clean(value,128);return text.Equals("Update",StringComparison.OrdinalIgnoreCase) || text.Equals("Hotfix",StringComparison.OrdinalIgnoreCase) || text.Equals("Security Update",StringComparison.OrdinalIgnoreCase) || text.Equals("Update Rollup",StringComparison.OrdinalIgnoreCase) || text.Equals("Service Pack",StringComparison.OrdinalIgnoreCase);}
  private static string Clean(string text,int limit){if(text==null)return "";var b=new StringBuilder(Math.Min(text.Length,limit));foreach(char c in text){if(b.Length>=limit)break;if(!Char.IsControl(c))b.Append(c);}return b.ToString().Trim();}
  private static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToUpperInvariant()))).Replace("-","").ToLowerInvariant();}
  private static bool Within(string candidate,string parent){return candidate.Equals(parent,StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
  private static void WarnOverlaps(InstalledAppsResult result,CancellationToken token) {
   var paths=new Dictionary<string,List<InstalledAppRow>>(StringComparer.OrdinalIgnoreCase);foreach(var row in result.Apps){token.ThrowIfCancellationRequested();try{if(Path.IsPathRooted(row.InstallLocation) && !String.IsNullOrWhiteSpace(row.InstallLocation)){string path=Path.GetFullPath(row.InstallLocation).TrimEnd(Path.DirectorySeparatorChar);List<InstalledAppRow> group;if(!paths.TryGetValue(path,out group)){group=new List<InstalledAppRow>();paths.Add(path,group);}group.Add(row);}}catch(ArgumentException){}catch(NotSupportedException){}catch(PathTooLongException){}}
   // Group identical paths before checking ancestors: mirrored/shared roots must not make this O(n squared).
   var overlappingPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var pair in paths){token.ThrowIfCancellationRequested();if(pair.Value.Count>1)overlappingPaths.Add(pair.Key);string parent=Path.GetDirectoryName(pair.Key);while(!String.IsNullOrEmpty(parent)){token.ThrowIfCancellationRequested();if(paths.ContainsKey(parent)){overlappingPaths.Add(parent);overlappingPaths.Add(pair.Key);}string next=Path.GetDirectoryName(parent);if(next==parent)break;parent=next;}}
   int overlapping=0;foreach(string path in overlappingPaths)foreach(var row in paths[path]){row.SizeNote+=" هم‌پوشانی با مسیر نصب برنامهٔ دیگری وجود دارد؛ این اندازه‌ها را با هم جمع نکنید.";overlapping++;}
   if(overlapping>0)result.Warnings.Add("در "+overlapping.ToString(CultureInfo.InvariantCulture)+" ثبت برنامه هم‌پوشانی مسیر نصب وجود دارد؛ مجموع اندازه‌ها، فضای یکتای دیسک نیست.");
  }
  public static bool CanMeasureLocation(string location,out string reason) {
   reason="";try {
    if(String.IsNullOrWhiteSpace(location) || location.Length>4096 || location.Length<3 || !Char.IsLetter(location[0]) || location[1]!=':' || (location[2]!='\\' && location[2]!='/') || !Path.IsPathRooted(location) || location.StartsWith(@"\\",StringComparison.Ordinal) || location.IndexOf('%')>=0){reason="مسیر نصب خالی، نسبی، شبکه‌ای یا دارای متغیر حل‌نشده است.";return false;}
    string full=Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);string driveRoot=Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar);
    if(full.Length==0 || full.Equals(driveRoot,StringComparison.OrdinalIgnoreCase) || full.IndexOf(':',2)>=0){reason="اندازه‌گیری ریشهٔ درایو یا مسیر غیرعادی مجاز نیست.";return false;}
    string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    if(!String.IsNullOrEmpty(windows) && Within(full,Path.GetFullPath(windows).TrimEnd(Path.DirectorySeparatorChar))){reason="مسیر داخل Windows یا فایل‌های سیستمی است؛ اندازه‌گیری نشد.";return false;}
    foreach(string systemFolder in new[]{"$Recycle.Bin","Recovery","System Volume Information","Windows.old","Config.Msi"})if(Within(full,Path.Combine(Path.GetPathRoot(full),systemFolder))){reason="مسیر متعلق به ذخیره‌سازی/ریکاوری سیستمی است؛ اندازه‌گیری نشد.";return false;}
    var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps")};
    foreach(string root in roots)if(!String.IsNullOrWhiteSpace(root) && full.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)){reason="مسیر یک ریشهٔ مشترک یا پوشهٔ شخصی عمومی است؛ اندازه‌گیری نشد.";return false;}
    string userRoot=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(!String.IsNullOrEmpty(userRoot) && full.Equals(Path.GetDirectoryName(userRoot),StringComparison.OrdinalIgnoreCase)){reason="مسیر ریشهٔ پروفایل‌های کاربران است؛ اندازه‌گیری نشد.";return false;}
    foreach(string common in new[]{Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86)})if(!String.IsNullOrWhiteSpace(common) && Within(full,Path.GetFullPath(common).TrimEnd(Path.DirectorySeparatorChar))){reason="مسیر متعلق به مؤلفه‌های مشترک برنامه‌هاست؛ نسبت‌دادن حجم به یک برنامه امن نیست.";return false;}
    string codex=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");if(Within(full,codex)){reason="مسیرهای Codex برای این اندازه‌گیری محافظت شده‌اند.";return false;}
    if(!Directory.Exists(full)){reason="پوشهٔ نصب در دسترس نیست؛ هیچ دسترسی یا مالکیتی تغییر نمی‌کند.";return false;}
    DirectoryInfo node=new DirectoryInfo(full);while(node!=null){if((node.Attributes&FileAttributes.ReparsePoint)!=0){reason="مسیر یا یکی از والدهای آن لینک/junction است؛ دنبال نمی‌شود.";return false;}node=node.Parent;}
    return true;
   }catch(Exception e){if(!IsReadFailure(e))throw;reason="مسیر نصب بدون تغییر دسترسی قابل اعتبارسنجی نیست ("+e.GetType().Name+").";return false;}
  }
  public static InstalledAppRow Measure(InstalledAppRow row,CancellationToken token){return Measure(row,token,100000,20000);}
  // The overload exposes smaller budgets to deterministic fixture tests; it never widens production limits.
  public static InstalledAppRow Measure(InstalledAppRow row,CancellationToken token,int maxEntries,int maxMilliseconds) {
   token.ThrowIfCancellationRequested();if(row==null)throw new ArgumentNullException("row");if(maxEntries<1 || maxEntries>100000 || maxMilliseconds<100 || maxMilliseconds>20000)throw new ArgumentOutOfRangeException("budget");
   var copy=Clone(row);string reason;if(!CanMeasureLocation(row.InstallLocation,out reason)){copy.CanMeasure=false;copy.MeasureBlockReason=reason;copy.SizeNote+=" اندازه‌گیری نشد: "+reason;return copy;}
   string root=Path.GetFullPath(row.InstallLocation).TrimEnd(Path.DirectorySeparatorChar);var clock=Stopwatch.StartNew();var stack=new Stack<KeyValuePair<string,int>>();stack.Push(new KeyValuePair<string,int>(root,0));long bytes=0;int entries=0;string incomplete=null;
   try {
    while(stack.Count>0) {
     token.ThrowIfCancellationRequested();var item=stack.Pop();if(clock.ElapsedMilliseconds>maxMilliseconds){incomplete="محدودیت زمان 20 ثانیه یا بودجهٔ انتخاب‌شده";break;}if(item.Value>64){incomplete="محدودیت عمق 64 پوشه";break;}
     if(!CanMeasureLocation(item.Key,out reason)){incomplete="مسیر تغییر کرده، لینک یا غیرقابل دسترس: "+reason;break;}
     foreach(string child in Directory.EnumerateFileSystemEntries(item.Key)) {
      token.ThrowIfCancellationRequested();if(++entries>maxEntries){incomplete="محدودیت تعداد ورودی‌ها ("+maxEntries.ToString(CultureInfo.InvariantCulture)+")";break;}if(clock.ElapsedMilliseconds>maxMilliseconds){incomplete="محدودیت زمان اندازه‌گیری";break;}
      string full=Path.GetFullPath(child);if(!Within(full,root)){incomplete="خروج مسیر از پوشهٔ نصب";break;}
      FileAttributes attributes=File.GetAttributes(full);if((attributes&FileAttributes.ReparsePoint)!=0){incomplete="لینک/junction داخل پوشه؛ برای جلوگیری از دوباره‌شماری دنبال نشد";break;}
      if((attributes&FileAttributes.Directory)!=0)stack.Push(new KeyValuePair<string,int>(full,item.Value+1));else bytes=checked(bytes+new FileInfo(full).Length);
     }
     if(incomplete!=null)break;
    }
   }catch(OperationCanceledException){throw;}catch(Exception e){if(!IsReadFailure(e) && !(e is OverflowException))throw;incomplete="خطا/عدم دسترسی ("+e.GetType().Name+")";}
   if(incomplete!=null){copy.SizeNote+=" اندازه‌گیری نشد/ناقص: "+incomplete+"؛ "+Format.Bytes(bytes)+" طول فایل تا توقف دیده شد و به‌عنوان حجم کامل نمایش داده نمی‌شود.";return copy;}
   if(!CanMeasureLocation(root,out reason)){copy.SizeNote+=" اندازه‌گیری نشد: مسیر در پایان اعتبارسنجی قابل تأیید نبود.";return copy;}
   copy.SizeBytes=bytes;copy.SizeKind="MeasuredFolder";copy.SizeNote="جمع طول فایل‌های قابل مشاهدهٔ پوشهٔ نصب، بدون دنبال کردن لینک‌ها؛ حجم تخصیص‌یافتهٔ واقعی دیسک نیست. داده‌های خارج از مسیر نصب (از جمله AppData)، وابستگی‌های بیرونی، فشرده‌سازی و hardlinkها لحاظ نشده‌اند. فایل‌ها حین اسکن ممکن است توسط برنامهٔ دیگری تغییر کنند."+((row.SizeNote ?? "").Contains("هم‌پوشانی") ? " هم‌پوشانی مسیر نصب وجود دارد؛ با اندازه‌های دیگر جمع نشود." : "");return copy;
  }
  private static InstalledAppRow Clone(InstalledAppRow row){return new InstalledAppRow{Name=row.Name,Version=row.Version,Publisher=row.Publisher,InstallLocation=row.InstallLocation,Source=row.Source,Architecture=row.Architecture,SizeBytes=row.SizeBytes,SizeKind=row.SizeKind,SizeNote=row.SizeNote ?? "",Identifier=row.Identifier,CanMeasure=row.CanMeasure,MeasureBlockReason=row.MeasureBlockReason};}
  private static bool IsReadFailure(Exception e){return e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is ArgumentException || e is NotSupportedException;}
  public static List<InstalledAppRecord> ParseStoreJson(string json,CancellationToken token) {
   token.ThrowIfCancellationRequested();if(json==null)throw new ArgumentNullException("json");if(json.Length>MaxStoreChars)throw new InvalidDataException("Microsoft Store metadata exceeds the bounded output limit.");if(String.IsNullOrWhiteSpace(json))return new List<InstalledAppRecord>();
   var serializer=new JavaScriptSerializer{MaxJsonLength=MaxStoreChars,RecursionLimit=10};object parsed=serializer.DeserializeObject(json);var list=new List<InstalledAppRecord>();
   if(parsed is Dictionary<string,object>)AddStoreRecord((Dictionary<string,object>)parsed,list);
   else if(parsed is object[])foreach(object value in (object[])parsed){token.ThrowIfCancellationRequested();if(list.Count>=10000)throw new InvalidDataException("Microsoft Store package count exceeds limit.");var record=value as Dictionary<string,object>;if(record==null)throw new ArgumentException("Unexpected Microsoft Store package metadata shape.");AddStoreRecord(record,list);}
   else throw new ArgumentException("Unexpected Microsoft Store metadata shape.");return list;
  }
  private static void AddStoreRecord(Dictionary<string,object> value,List<InstalledAppRecord> list){list.Add(new InstalledAppRecord{DisplayName=StoreText(value,"Name",1024),Version=StoreText(value,"Version",1024),Publisher=StoreText(value,"Publisher",1024),InstallLocation=StoreText(value,"InstallLocation",4096),RegistrationKey=StoreText(value,"PackageFullName",1024),Source="Microsoft Store",Hive="CurrentUser",Architecture=StoreText(value,"Architecture",128),IsFramework=StoreBool(value,"IsFramework"),IsResourcePackage=StoreBool(value,"IsResourcePackage"),EstimatedSizeKind=RegistryValueKind.Unknown});}
  private static string StoreText(Dictionary<string,object> value,string key,int limit){object data;return value.TryGetValue(key,out data) && data is string ? Clean((string)data,limit) : "";}
  private static bool StoreBool(Dictionary<string,object> value,string key){object data;return value.TryGetValue(key,out data) && data is bool && (bool)data;}
  private static string ReadStoreJson(CancellationToken token) {
   string executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");if(!File.Exists(executable))throw new FileNotFoundException("Windows PowerShell is unavailable.");
   var start=new ProcessStartInfo(executable,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(StoreScript))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
   start.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\Modules");
   using(var process=new Process{StartInfo=start}) {
    bool started=false;try {
     token.ThrowIfCancellationRequested();process.Start();started=true;var output=Task.Run(()=>ReadBounded(process.StandardOutput,MaxStoreChars));var error=Task.Run(()=>ReadBounded(process.StandardError,65536));var clock=Stopwatch.StartNew();
     while(!process.WaitForExit(100)) {token.ThrowIfCancellationRequested();if(output.IsFaulted || error.IsFaulted)throw new InvalidDataException("Microsoft Store output exceeded its safe limit.");if(clock.ElapsedMilliseconds>20000)throw new TimeoutException("Microsoft Store scan reached its timeout.");}
     token.ThrowIfCancellationRequested();if(!Task.WaitAll(new Task[]{output,error},3000))throw new TimeoutException("Microsoft Store output did not complete.");if(process.ExitCode!=0)throw new InvalidOperationException("Microsoft Store query returned an error.");return output.Result;
    }finally{if(started)try{if(!process.HasExited){process.Kill();process.WaitForExit(2000);}}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
   }
  }
  private static string ReadBounded(StreamReader reader,int limit){var text=new StringBuilder();char[] buffer=new char[4096];int read;while((read=reader.Read(buffer,0,buffer.Length))>0){if(text.Length>limit-read)throw new InvalidDataException("Metadata output too large.");text.Append(buffer,0,read);}return text.ToString();}
 }
}
