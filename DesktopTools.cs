using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Threading;

namespace ClearGuard {
 public static class DesktopScanner {
  internal static readonly HashSet<string> CodeExtensions=new HashSet<string>(new[]{".cs",".fs",".vb",".java",".kt",".kts",".py",".pyw",".js",".jsx",".ts",".tsx",".go",".rs",".cpp",".c",".h",".hpp",".swift",".dart",".php",".rb",".sh",".ps1",".bat",".cmd",".sln",".csproj",".vcxproj",".vbproj"},StringComparer.OrdinalIgnoreCase);
  internal static readonly HashSet<string> Markers=new HashSet<string>(new[]{"package.json","build.gradle","build.gradle.kts","settings.gradle","settings.gradle.kts","pom.xml","go.mod","cargo.toml","pyproject.toml","requirements.txt","composer.json","pubspec.yaml","androidmanifest.xml"},StringComparer.OrdinalIgnoreCase);
  private sealed class ProjectProof { public bool Source; public bool Safe=true; public string Reason=""; public string Fingerprint=""; public long Bytes; public List<FileRecord> Files=new List<FileRecord>(); public string Version=""; public int VersionRank=99; }
  public static ScanResult Scan(ScanContext ctx,CancellationToken token,Action<string> progress) {
   var result=new ScanResult{Kind="sources",Root=ctx.Desktop,FreeBefore=Format.FreeC()};
   if(!Directory.Exists(ctx.Desktop)){result.Warnings.Add("دسکتاپ پیدا نشد.");return result;}
   var roots=new List<string>(); var archives=new List<string>();
   try { Gather(ctx.Desktop,0,roots,archives,token,result.Warnings); } catch(OperationCanceledException){throw;} catch(Exception e){result.Warnings.Add(e.Message);}
   foreach(string path in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
    token.ThrowIfCancellationRequested(); if(progress!=null)progress("بررسی سورس: "+Path.GetFileName(path));
    ProjectProof proof=ProveFolder(path,token); if(!proof.Source)continue;
    if(IsUnscopedSourceChild(path)){proof.Safe=false;proof.Reason="پوشهٔ src/app بدون شناسهٔ مستقل پروژه؛ فایل‌های یکتای والد ممکن است بیرون از محدودهٔ تطبیق باشند. فقط گزارش، بدون حذف.";}
    result.Rows.Add(ToRow(path,proof,false));
   }
   foreach(string path in archives.Distinct(StringComparer.OrdinalIgnoreCase)) {
    token.ThrowIfCancellationRequested();if(progress!=null)progress("بررسی آرشیو: "+Path.GetFileName(path));
    ProjectProof proof=ProveArchive(path,token); if(!proof.Source)continue;
    result.Rows.Add(ToRow(path,proof,true));
   }
   foreach(var group in result.Rows.Where(r=>r.Fingerprint!=null&&r.Fingerprint.Length>0&&r.ProtectedCount==0).GroupBy(r=>(Directory.Exists(r.Path)?"folder:":"zip:")+r.Fingerprint,StringComparer.Ordinal)) {
    var members=group.OrderBy(r=>LooksBackup(r.Path)?1:0).ThenByDescending(r=>VersionNumber(r.Path)).ThenBy(r=>r.Path.Length).ThenBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).ToList();
    if(members.Count<2)continue;ScanRow keeper=members[0];keeper.Status="نسخهٔ نگه‌داری‌شده";keeper.Effect="محتوای دقیقاً یکسان با نسخه‌های اضافی؛ این نسخه حفظ می‌شود.";
    foreach(ScanRow row in members.Skip(1)) {
     row.Keeper=keeper.Path;row.Selected=false;
     if(Directory.Exists(row.Path)){row.Eligible=false;row.SafeBytes=0;row.Status="تکراریِ پوشه؛ فقط گزارش";row.BlockReason="برای حفظ قفل ضد تغییر فایل‌ها، بازیافت پوشهٔ سورس در این نسخه غیرفعال است؛ Windows Shell با این قفل‌ها انتقال پوشه را رد می‌کند.";row.Effect="تطبیق کامل محتوا گزارش می‌شود، اما پوشه و نسخهٔ نگه‌داری‌شده دست‌نخورده باقی می‌مانند. بازیافت امن فعلاً فقط برای ZIP سورس ارائه می‌شود.";continue;}
     row.Eligible=true;row.SafeBytes=row.Bytes;row.Status="تکراریِ اثبات‌شده؛ نیازمند انتخاب";
     if(File.Exists(keeper.Path)){row.KeeperHash=keeper.Hash;row.KeeperBytes=keeper.Bytes;row.KeeperModifiedTicks=File.GetLastWriteTimeUtc(keeper.Path).Ticks;}
     row.Effect="تمام فایل‌ها و مسیرهای نسبی با نسخهٔ نگه‌داری‌شده برابرند. فقط با انتخاب شما به سطل بازیافت فرستاده می‌شود؛ تاریخ یا شمارهٔ نسخه مبنای حذف نیست.";
    }
   }
   result.Rows=result.Rows.OrderByDescending(r=>r.Bytes).ToList();result.CompletedUtc=DateTime.UtcNow;return result;
  }
  private static bool IsUnscopedSourceChild(string path) {
   string leaf=Path.GetFileName(path);if(!leaf.Equals("src",StringComparison.OrdinalIgnoreCase)&&!leaf.Equals("app",StringComparison.OrdinalIgnoreCase))return false;
   try {
    // Code alone in a conventional child folder does not prove the whole project boundary.
    // A real project manifest at that child root makes its scope explicit; age/name never does.
    if(Directory.Exists(Path.Combine(path,".git"))||File.Exists(Path.Combine(path,".git")))return false;
    return !Directory.GetFiles(path).Any(file=>Markers.Contains(Path.GetFileName(file))||new[]{".sln",".csproj",".vbproj",".vcxproj"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase));
   }catch{return true;}
  }
  private static void Gather(string path,int depth,List<string> roots,List<string> archives,CancellationToken token,List<string> warnings) {
   token.ThrowIfCancellationRequested();if(SafetyPolicy.HasReparseAncestor(path)){warnings.Add("پیوند دنبال نشد: "+path);return;}
   try {
    string[] files=Directory.GetFiles(path);string currentLeaf=Path.GetFileName(path);bool container=currentLeaf.Equals("دسکتاپ مرتب",StringComparison.OrdinalIgnoreCase)||Organizer.Categories.Contains(currentLeaf);bool source=!container&&(files.Any(p=>Markers.Contains(Path.GetFileName(p))||CodeExtensions.Contains(Path.GetExtension(p)))||Directory.Exists(Path.Combine(path,".git")));
    if(depth>0&&source){roots.Add(path);return;}
    foreach(string f in files)if(Path.GetExtension(f).Equals(".zip",StringComparison.OrdinalIgnoreCase))archives.Add(f);
    foreach(string d in Directory.GetDirectories(path)) {
     string leaf=Path.GetFileName(d); if(leaf.Equals(".git",StringComparison.OrdinalIgnoreCase)||leaf.Equals("node_modules",StringComparison.OrdinalIgnoreCase)||leaf.Equals(".gradle",StringComparison.OrdinalIgnoreCase))continue;
     if(depth<8)Gather(d,depth+1,roots,archives,token,warnings);
    }
   } catch(OperationCanceledException){throw;}catch(Exception e){warnings.Add(path+": "+e.Message);}
  }
  private static ScanRow ToRow(string path,ProjectProof p,bool archive) {
   string originalReason="";bool original=IsOriginal(path,out originalReason);
   var row=new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileName(path),Path=path,Category=archive?"آرشیو سورس":"پروژه / سورس",App="نسخه‌های دسکتاپ",Bytes=p.Bytes,FileCount=p.Files.Count,Files=p.Files,Version=String.IsNullOrEmpty(p.Version)?"نامشخص":p.Version,Fingerprint=p.Safe&&!original?p.Fingerprint:"",Eligible=false,Selected=false};
   if(archive&&p.Files.Count>0)row.Hash=p.Files[0].Hash;
   if(original||!p.Safe){row.ProtectedCount=1;row.Status="حفاظت‌شده";row.BlockReason=original?originalReason:p.Reason;row.Effect="دست‌نخورده باقی می‌ماند؛ "+row.BlockReason;}
   else{row.Status="منحصر‌به‌فرد / بررسی دستی";row.Effect="فایل یا شاخهٔ متفاوت خودکار حذف نمی‌شود؛ نسخه تنها با برابری کامل محتوا تکراری تشخیص داده می‌شود.";}
   return row;
  }
  internal static bool IsOriginal(string path,out string reason) {
   reason="";string normalized=Path.GetFullPath(path).Replace('/','\\');string[] parts=normalized.Split('\\');
   foreach(string p in parts)if(p.IndexOf("symex",StringComparison.OrdinalIgnoreCase)>=0||p.IndexOf("proxyx",StringComparison.OrdinalIgnoreCase)>=0){reason="پروژهٔ اصلی SYMEXVPN / proxyx طبق درخواست شما همیشه حفظ می‌شود.";return true;}
   return false;
  }
  private static ProjectProof ProveFolder(string root,CancellationToken token) {
   var proof=new ProjectProof{Version=ReadProjectVersion(root)};var signatures=new List<string>();var pending=new Stack<string>();pending.Push(root);string basePath=Path.GetFullPath(root).TrimEnd('\\')+"\\";
   string originalReason;if(IsOriginal(root,out originalReason)){proof.Safe=false;proof.Reason=originalReason;}
   int count=0;
   while(pending.Count>0) {
    token.ThrowIfCancellationRequested();string dir=pending.Pop();
    if(SafetyPolicy.HasReparseAncestor(dir)){proof.Safe=false;proof.Reason="پیوند / junction داخل مسیر؛ پیمایش نشد.";continue;}
    if(!String.Equals(Path.GetFullPath(dir).TrimEnd('\\'),Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))signatures.Add("dir:"+dir.Substring(basePath.Length).Replace('\\','/').ToLowerInvariant());
    try {
     foreach(string child in Directory.GetDirectories(dir))pending.Push(child);
     foreach(string file in Directory.GetFiles(dir)) {
      token.ThrowIfCancellationRequested();count++;if(count>80000){proof.Safe=false;proof.Reason="تعداد فایل بیش از حد محافظه‌کارانهٔ اسکن است؛ حذف مجاز نیست.";proof.Fingerprint="";return proof;}
      FileInfo fi=new FileInfo(file);proof.Bytes+=fi.Length;string ext=fi.Extension;string relative=file.Substring(basePath.Length).Replace('\\','/');
      if(CodeExtensions.Contains(ext)||Markers.Contains(fi.Name))proof.Source=true;
      var record=new FileRecord{Path=file,Bytes=fi.Length,ModifiedTicks=fi.LastWriteTimeUtc.Ticks};proof.Files.Add(record);
      if(proof.Safe){string protection="";if(SafetyPolicy.IsProtectedContent(file,out protection,true)||IsSensitive(file)||!RecognizedDuplicateFile(file)){proof.Safe=false;if(proof.Reason.Length==0)proof.Reason=!String.IsNullOrEmpty(protection)?protection:"APK، کلید، دادهٔ شخصی یا نوع فایل ناشناخته داخل پروژه؛ حفظ می‌شود.";}}
      if(fi.Length>1024L*1024*1024){proof.Safe=false;proof.Reason="فایل بیش از ۱ گیگابایت؛ برای اثبات تکراری بودن بررسی دستی لازم است.";}
      if(proof.Safe){string hash=SafetyPolicy.HashFile(file);record.Hash=hash;signatures.Add(relative.ToLowerInvariant()+"|"+fi.Length+"|"+hash);}
      if(Markers.Contains(fi.Name)&&fi.Length<128*1024){string metadata=File.ReadAllText(file);if(proof.Version.Length==0)proof.Version=ReadVersion(metadata);if(ProtectedIdentity(metadata)){proof.Safe=false;proof.Reason="شناسهٔ SYMEXVPN / proxyx داخل پروژه شناسایی شد؛ نسخهٔ اصلی حفظ می‌شود.";}}
     }
    }catch(OperationCanceledException){throw;}catch(Exception e){proof.Safe=false;proof.Reason="اسکن کامل نشد: "+e.Message;}
   }
   if(Directory.Exists(Path.Combine(root,".git")))proof.Source=true;
   if(proof.Safe&&proof.Files.Count>0)proof.Fingerprint=HashText(String.Join("\n",signatures.OrderBy(x=>x,StringComparer.Ordinal).ToArray()));return proof;
  }
  private static ProjectProof ProveArchive(string path,CancellationToken token) {
   var proof=new ProjectProof();FileInfo fi=new FileInfo(path);proof.Bytes=fi.Length;string[] archiveSuffix={".rar",".7z",".tar",".gz",".bz2",".xz",".iso",".vhd",".vhdx"};
   try {
    string originalReason;if(IsOriginal(path,out originalReason)){proof.Safe=false;proof.Source=true;proof.Reason=originalReason;proof.Files.Add(new FileRecord{Path=path,Bytes=fi.Length,ModifiedTicks=fi.LastWriteTimeUtc.Ticks});return proof;}
    if(fi.Length>1024L*1024*1024){proof.Safe=false;proof.Source=true;proof.Reason="آرشیو بیش از ۱ گیگابایت؛ نیازمند بررسی دستی.";return proof;}
    string protection="";if(SafetyPolicy.IsProtectedContent(path,out protection,true)){proof.Safe=false;proof.Reason=protection;}
    var entries=new List<string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
    using(var zip=ZipFile.OpenRead(path)) {
     foreach(var entry in zip.Entries) {
      token.ThrowIfCancellationRequested();string n=entry.FullName.Replace('\\','/');bool directory=n.EndsWith("/");string clean=n.TrimEnd('/');
      if(n.StartsWith("/")||n.Split('/').Contains("..")||n.IndexOf(':')>=0||!names.Add(n)){proof.Safe=false;proof.Reason="مسیر نامعتبر یا نام تکراری داخل آرشیو.";}
      string[] segments=clean.Split('/');string part="";for(int k=0;k<segments.Length-(directory?0:1);k++){part=part.Length==0?segments[k]:part+"/"+segments[k];directories.Add(part.ToLowerInvariant());}if(directory)continue;
      string leaf=Path.GetFileName(n),ext=Path.GetExtension(n);
      if(CodeExtensions.Contains(ext)||Markers.Contains(leaf))proof.Source=true;
      string identityReason;if(SafetyPolicy.IsMediaExtension(ext)||IsSensitive(n)||IsOriginal(n,out identityReason)||archiveSuffix.Contains(ext,StringComparer.OrdinalIgnoreCase)||!RecognizedDuplicateFile(n)){proof.Safe=false;if(proof.Reason.Length==0)proof.Reason="پروژهٔ اصلی، رسانه، APK، دادهٔ حساس، نوع فایل یا آرشیو ناشناخته داخل ZIP؛ حفظ می‌شود.";}
      expanded+=entry.Length;if(entry.Length>256L*1024*1024||expanded>2L*1024*1024*1024||names.Count>80000){proof.Safe=false;proof.Reason="آرشیو بسیار حجیم / پیچیده؛ بررسی دستی لازم است.";return proof;}
      if(proof.Safe)using(Stream stream=entry.Open())using(var sha=SHA256.Create())entries.Add(n.ToLowerInvariant()+"|"+entry.Length+"|"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""));
      if(Markers.Contains(leaf)&&entry.Length<128*1024)using(var reader=new StreamReader(entry.Open())){string metadata=reader.ReadToEnd();int rank=ArchiveVersionRank(n);string version=ReadVersion(metadata);if(rank<proof.VersionRank&&!String.IsNullOrEmpty(version)){proof.Version=version;proof.VersionRank=rank;}if(ProtectedIdentity(metadata)){proof.Safe=false;proof.Reason="شناسهٔ SYMEXVPN / proxyx داخل آرشیو شناسایی شد؛ حفظ می‌شود.";}}
     }
    }
    proof.Files.Add(new FileRecord{Path=path,Bytes=fi.Length,ModifiedTicks=fi.LastWriteTimeUtc.Ticks,Hash=proof.Safe?SafetyPolicy.HashFile(path):null});
    entries.AddRange(directories.Select(d=>"dir:"+d));if(proof.Safe&&proof.Source&&entries.Count>0)proof.Fingerprint=HashText(String.Join("\n",entries.OrderBy(x=>x,StringComparer.Ordinal).ToArray()));
   }catch(OperationCanceledException){throw;}catch(Exception e){proof.Safe=false;proof.Source=true;proof.Reason="آرشیو قابل اثبات نیست: "+e.Message;}
   return proof;
  }
  internal static bool IsSensitive(string path) {
   string ext=Path.GetExtension(path);string leaf=Path.GetFileName(path);
   return new[]{".apk",".aab",".jks",".keystore",".pfx",".p12",".pem",".key",".hprof",".db",".sqlite",".sqlite3",".doc",".docx",".xls",".xlsx",".pdf",".ppt",".pptx"}.Contains(ext,StringComparer.OrdinalIgnoreCase)||leaf.Equals(".env",StringComparison.OrdinalIgnoreCase)||leaf.StartsWith(".env.",StringComparison.OrdinalIgnoreCase)||leaf.Equals("local.properties",StringComparison.OrdinalIgnoreCase)||leaf.Equals("gradle.properties",StringComparison.OrdinalIgnoreCase)||path.IndexOf("LocalHistory",StringComparison.OrdinalIgnoreCase)>=0;
  }
  private static bool RecognizedDuplicateFile(string path) {
   string ext=Path.GetExtension(path),leaf=Path.GetFileName(path);if(CodeExtensions.Contains(ext)||Markers.Contains(leaf))return true;
   if(new[]{".html",".htm",".css",".scss",".less",".vue",".svelte",".sql",".ipynb",".json",".xml",".yaml",".yml",".toml",".md",".markdown",".gradle",".properties",".lock",".pro",".gitignore",".gitattributes",".editorconfig",".sum"}.Contains(ext,StringComparer.OrdinalIgnoreCase))return true;
   return Regex.IsMatch(leaf,"^(?:readme|license|licence|notice|changelog|authors|contributors|copying)(?:\\.(?:txt|md))?$",RegexOptions.IgnoreCase)||leaf.Equals("CMakeLists.txt",StringComparison.OrdinalIgnoreCase)||leaf.Equals("Dockerfile",StringComparison.OrdinalIgnoreCase)||leaf.Equals("Makefile",StringComparison.OrdinalIgnoreCase);
  }
  private static string ReadVersion(string text) {
   Match android=Regex.Match(text,"versionName\\s*(?:=\\s*)?[\\\"']([^\\\"'\\r\\n]+)",RegexOptions.IgnoreCase);if(android.Success)return android.Groups[1].Value.Trim();
   Match match=Regex.Match(text,"(?:[\\\"']?version[\\\"']?\\s*[:=]\\s*[\\\"']|versionName\\s*[= ]\\s*[\\\"'])([^\\\"'\\r\\n]+)",RegexOptions.IgnoreCase);
   return match.Success?match.Groups[1].Value.Trim():"";
  }
  private static bool ProtectedIdentity(string text){return Regex.IsMatch(text,"(?:symexvpn|proxyx)",RegexOptions.IgnoreCase);}
  private static int ArchiveVersionRank(string path){path=path.Replace('\\','/');if(path.EndsWith("/app/build.gradle",StringComparison.OrdinalIgnoreCase)||path.EndsWith("/app/build.gradle.kts",StringComparison.OrdinalIgnoreCase))return 0;if(Path.GetFileName(path).Equals("package.json",StringComparison.OrdinalIgnoreCase)&&path.Split('/').Length<=2)return 1;return 99;}
  private static string ReadProjectVersion(string root){
   var bases=new List<string>{root};try{foreach(string child in Directory.GetDirectories(root)){string leaf=Path.GetFileName(child);if(leaf=="node_modules"||leaf==".git"||leaf==".gradle"||SafetyPolicy.HasReparseAncestor(child))continue;bases.Add(child);}}catch{}
   var versions=new HashSet<string>(StringComparer.Ordinal);foreach(string basePath in bases)foreach(string relative in new[]{"app\\build.gradle.kts","app\\build.gradle"}){string file=Path.Combine(basePath,relative);try{if(!File.Exists(file)||SafetyPolicy.HasReparseAncestor(file)||new FileInfo(file).Length>=128*1024)continue;string text=File.ReadAllText(file);Match match=Regex.Match(text,"versionName\\s*(?:=\\s*)?[\\\"']([^\\\"'\\r\\n]+)");if(match.Success)versions.Add(match.Groups[1].Value.Trim());}catch{}}
   if(versions.Count==1)return versions.First();if(versions.Count>1)return "چند نسخهٔ مستقل؛ بررسی دستی";
   try{string package=Path.Combine(root,"package.json");if(File.Exists(package)&&!SafetyPolicy.HasReparseAncestor(package)&&new FileInfo(package).Length<128*1024){string version=ReadVersion(File.ReadAllText(package));if(!String.IsNullOrEmpty(version))return version;}}catch{}
   return "نامشخص";
  }
  private static bool LooksBackup(string path){string name=Path.GetFileName(path);return Regex.IsMatch(name,"(?:copy|backup|old|کپی|قدیمی|نسخه|\\(\\d+\\))",RegexOptions.IgnoreCase);}
  private static long VersionNumber(string path){Match m=Regex.Match(Path.GetFileName(path),"(?:v|version[-_ ]?)(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?",RegexOptions.IgnoreCase);if(!m.Success)return 0;long a=0,b=0,c=0;long.TryParse(m.Groups[1].Value,out a);long.TryParse(m.Groups[2].Value,out b);long.TryParse(m.Groups[3].Value,out c);return Math.Min(a,9999)*100000000+Math.Min(b,9999)*10000+Math.Min(c,9999);}
  internal static string HashText(string s){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","");}
  internal static bool VerifySourceRow(ScanContext ctx,ScanRow row,CancellationToken token,out string reason) {
   reason="";if(row==null||!row.Eligible||String.IsNullOrEmpty(row.Keeper)||String.IsNullOrEmpty(row.Fingerprint)){reason="ردیف مجاز یا نسخهٔ نگه‌داری‌شده وجود ندارد.";return false;}
   string desktop=Path.GetFullPath(ctx.Desktop).TrimEnd('\\');
   if(!SafetyPolicy.IsWithin(row.Path,desktop)||!SafetyPolicy.IsWithin(row.Keeper,desktop)||String.Equals(Path.GetFullPath(row.Path),desktop,StringComparison.OrdinalIgnoreCase)||String.Equals(Path.GetFullPath(row.Path),Path.GetFullPath(row.Keeper),StringComparison.OrdinalIgnoreCase)){reason="مسیر خارج از دسکتاپ یا همان نسخهٔ اصلی است.";return false;}
   if(SafetyPolicy.HasReparseAncestor(row.Path)||SafetyPolicy.HasReparseAncestor(row.Keeper)){reason="مسیر پیوندی / junction اجازه ندارد.";return false;}
   if(IsOriginal(row.Path,out reason)||IsOriginal(row.Keeper,out reason))return false;
   bool folder=Directory.Exists(row.Path);if(folder){reason="بازیافت پوشهٔ سورس با حفظ قفل ضد تغییر، در این نسخه فقط گزارش است؛ حتی ردیف قدیمی یا دست‌کاری‌شده حذف نمی‌شود.";return false;}if(folder!=Directory.Exists(row.Keeper)){reason="نوع نسخه‌ها تغییر کرده است.";return false;}
   if(!folder&&(!File.Exists(row.Path)||!File.Exists(row.Keeper)||!Path.GetExtension(row.Path).Equals(".zip",StringComparison.OrdinalIgnoreCase)||!Path.GetExtension(row.Keeper).Equals(".zip",StringComparison.OrdinalIgnoreCase))){reason="آرشیو سورس معتبر پیدا نشد.";return false;}
   if(!folder){FileInfo keeperInfo=new FileInfo(row.Keeper);if(String.IsNullOrEmpty(row.KeeperHash)||keeperInfo.Length!=row.KeeperBytes||keeperInfo.LastWriteTimeUtc.Ticks!=row.KeeperModifiedTicks||SafetyPolicy.HashFile(row.Keeper)!=row.KeeperHash){reason="آرشیو نگه‌داری‌شده بعد از اسکن تغییر کرده است؛ دوباره اسکن کنید.";return false;}}
   ProjectProof current=folder?ProveFolder(row.Path,token):ProveArchive(row.Path,token);ProjectProof kept=folder?ProveFolder(row.Keeper,token):ProveArchive(row.Keeper,token);
   if(!current.Safe||!kept.Safe||!current.Source||!kept.Source||current.Fingerprint!=row.Fingerprint||kept.Fingerprint!=row.Fingerprint){reason="محتوا تغییر کرده، رسانه/دادهٔ حساس پیدا شده، یا تطابق کامل اثبات نشد.";return false;}
   foreach(FileRecord record in row.Files){if(!File.Exists(record.Path)){reason="فایل بعد از اسکن تغییر کرده است.";return false;}FileInfo f=new FileInfo(record.Path);if(f.Length!=record.Bytes||f.LastWriteTimeUtc.Ticks!=record.ModifiedTicks||SafetyPolicy.HashFile(record.Path)!=record.Hash){reason="اثر انگشت اسکن دیگر معتبر نیست؛ دوباره اسکن کنید.";return false;}}
   return true;
  }
 }
 public static class DesktopCleanup {
  public static OperationReport Run(ScanContext ctx,List<ScanRow> rows,CancellationToken token,Action<string> progress) {
   var report=new OperationReport{Kind="source-recycle",Root=ctx.Desktop,FreeBefore=Format.FreeC(),Permanent=false};
   var selected=(rows??new List<ScanRow>()).Where(r=>r.Selected).ToList();var paths=new HashSet<string>(selected.Select(r=>Path.GetFullPath(r.Path)),StringComparer.OrdinalIgnoreCase);
   foreach(ScanRow row in selected) {
    if(token.IsCancellationRequested){report.Entries.Add(new OperationEntry{Status="cancelled",Detail="عملیات متوقف شد؛ کارهای انجام‌شده در رسید حفظ شدند."});break;}if(progress!=null)progress("بازبینی نهایی: "+row.Name);string reason;
    if(paths.Contains(Path.GetFullPath(row.Keeper??row.Path))){report.Entries.Add(Skip(row,"نسخهٔ نگه‌داری‌شده هم انتخاب شده است؛ همهٔ نسخه‌ها حفظ شدند."));continue;}
    try {
     if(!DesktopScanner.VerifySourceRow(ctx,row,token,out reason)){report.Entries.Add(Skip(row,reason));continue;}
     var pending=new OperationEntry{OriginalPath=row.Path,Status="pending",Detail="بازبینی محتوا تأیید شد؛ انتقال به سطل بازیافت هنوز تکمیل نشده است.",Bytes=row.Bytes,Hash=Directory.Exists(row.Path)?SafetyPolicy.HashTree(row.Path):SafetyPolicy.HashFile(row.Path)};report.Entries.Add(pending);
     if(!DesktopJournal.Persist(ctx,report,out reason)){pending.Status="skipped";pending.Detail="رسید پیش از عملیات ذخیره نشد: "+reason;continue;}
     var locks=new List<FileStream>();
     try {
      foreach(FileRecord record in row.Files){if(SafetyPolicy.HasReparseAncestor(record.Path))throw new IOException("مسیر پس از بررسی پیوندی شد.");locks.Add(new FileStream(record.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete));}
      locks.AddRange(OpenKeeperLocks(row,token));
      if(!DesktopScanner.VerifySourceRow(ctx,row,token,out reason)){pending.Status="skipped";pending.Detail=reason;DesktopJournal.Persist(ctx,report,out reason);continue;}
      OperationEntry entry=RecycleService.Recycle(row.Path);report.Entries[report.Entries.Count-1]=entry;if(String.Equals(entry.Status,"recycled",StringComparison.OrdinalIgnoreCase)||String.Equals(entry.Status,"success",StringComparison.OrdinalIgnoreCase)){entry.Bytes=row.Bytes;report.ProcessedBytes+=row.Bytes;}DesktopJournal.Persist(ctx,report,out reason);
     } finally {foreach(FileStream fileLock in locks)fileLock.Dispose();}
    }catch(OperationCanceledException){report.Entries.Add(Skip(row,"عملیات لغو شد؛ این مسیر حذف نشد."));break;}catch(Exception e){report.Entries.Add(Skip(row,e.Message));}
   }
   report.FreeAfter=Format.FreeC();report.CompletedUtc=DateTime.UtcNow;string saveReason;DesktopJournal.Persist(ctx,report,out saveReason);return report;
  }
  internal static List<FileStream> OpenKeeperLocks(ScanRow row,CancellationToken token) {
   var locks=new List<FileStream>();
   try {
    token.ThrowIfCancellationRequested();if(row==null||String.IsNullOrEmpty(row.Keeper)||SafetyPolicy.HasReparseAncestor(row.Keeper))throw new IOException("نسخهٔ نگه‌داری‌شده معتبر و غیرپیوندی لازم است.");
    if(File.Exists(row.Keeper)) {
     var hold=new FileStream(row.Keeper,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(hold);
     if(hold.Length!=row.KeeperBytes||String.IsNullOrEmpty(row.KeeperHash)||SafetyPolicy.HashStream(hold)!=row.KeeperHash)throw new IOException("محتوای نسخهٔ نگه‌داری‌شده تغییر کرده است.");
    } else {
     if(!Directory.Exists(row.Keeper)||!Directory.Exists(row.Path)||row.Files==null||row.Files.Count==0)throw new IOException("پوشهٔ نگه‌داری‌شده قابل اثبات نیست.");
     string original=Path.GetFullPath(row.Path).TrimEnd('\\')+"\\";
     foreach(FileRecord record in row.Files) {
      token.ThrowIfCancellationRequested();
      if(!SafetyPolicy.IsWithin(record.Path,row.Path)||String.IsNullOrEmpty(record.Hash))throw new IOException("Manifest سورس کامل نیست.");
      string kept=Path.GetFullPath(Path.Combine(row.Keeper,Path.GetFullPath(record.Path).Substring(original.Length)));
      if(!SafetyPolicy.IsWithin(kept,row.Keeper)||SafetyPolicy.HasReparseAncestor(kept))throw new IOException("فایل نگه‌داری‌شده پیوندی یا خارج از محدوده است.");
      var hold=new FileStream(kept,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(hold);
      if(hold.Length!=record.Bytes||SafetyPolicy.HashStream(hold)!=record.Hash)throw new IOException("محتوای سورس نگه‌داری‌شده تغییر کرده است.");
     }
    }
    return locks;
   }catch{foreach(FileStream hold in locks)hold.Dispose();throw;}
  }
  private static OperationEntry Skip(ScanRow r,string why){return new OperationEntry{OriginalPath=r.Path,Status="skipped",Detail=why,Bytes=0};}
 }
 public static class Organizer {
  public static readonly string[] Categories={"پروژه‌ها","ابزار شبکه","آموزش","اسناد","تصاویر و ویدیو","ابزارها","میانبرها","متفرقه"};
  public static string TargetFor(ScanContext ctx,string file){return Path.Combine(ctx.Desktop,"دسکتاپ مرتب",Classify(file),Path.GetFileName(file));}
  private static string Classify(string path) {
   string ext=Path.GetExtension(path),name=Path.GetFileName(path);if(DesktopScanner.CodeExtensions.Contains(ext)||Directory.Exists(path))return Categories[0];
   if(ext.Equals(".lnk",StringComparison.OrdinalIgnoreCase)||ext.Equals(".url",StringComparison.OrdinalIgnoreCase))return Categories[6];
   if(SafetyPolicy.IsMediaExtension(ext))return Categories[4];
   if(Regex.IsMatch(name,"(?:vpn|proxy|vless|v2ray|clash|network|dns)",RegexOptions.IgnoreCase))return Categories[1];
   if(Regex.IsMatch(name,"(?:آموزش|درس|کتاب|course|lesson|tutorial)",RegexOptions.IgnoreCase))return Categories[2];
   if(new[]{".txt",".md",".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".csv",".rtf",".odt",".ods",".epub"}.Contains(ext,StringComparer.OrdinalIgnoreCase))return Categories[3];
   if(new[]{".exe",".msi",".msix",".apk",".aab",".bat",".cmd",".ps1"}.Contains(ext,StringComparer.OrdinalIgnoreCase))return Categories[5];return Categories[7];
  }
  public static ScanResult Preview(ScanContext ctx,CancellationToken token,Action<string> progress) {
   var result=new ScanResult{Kind="organizer",Root=ctx.Desktop,FreeBefore=Format.FreeC()};if(!Directory.Exists(ctx.Desktop))return result;
   foreach(string path in Directory.GetFileSystemEntries(ctx.Desktop)) {
    token.ThrowIfCancellationRequested();if(Path.GetFileName(path).Equals("دسکتاپ مرتب",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))continue;
    if(progress!=null)progress("پیش‌نمایش: "+Path.GetFileName(path));
    var row=new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileName(path),Path=path,Category=Classify(path),App="مرتب‌سازی دسکتاپ",Keeper=TargetFor(ctx,path),Selected=false,Eligible=false,Status="نیازمند انتخاب",Effect="انتقال بدون حذف، با رسید SHA-256 و امکان بازگردانی؛ مسیر مقصد: "+TargetFor(ctx,path)};
    string why;
    try {
     if(Directory.Exists(path)){row.Status="پوشه / پروژه حفظ می‌شود";row.BlockReason="برای حفظ مسیر پروژه‌ها، پوشه‌ها جابه‌جا نمی‌شوند.";row.Effect=row.BlockReason;}
     else {
      FileInfo f=new FileInfo(path);row.Bytes=f.Length;row.FileCount=1;row.Hash=SafetyPolicy.HashFile(path);row.Fingerprint=row.Hash;row.Files.Add(new FileRecord{Path=path,Bytes=f.Length,ModifiedTicks=f.LastWriteTimeUtc.Ticks,Hash=row.Hash});
      if(CanMove(ctx,path,row.Keeper,out why)){row.Eligible=true;row.SafeBytes=f.Length;if(SafetyPolicy.IsMediaExtension(f.Extension))row.Status="رسانه: انتقال اختیاری، هرگز حذف";}
      else{row.Status="حفظ مسیر فعلی";row.BlockReason=why;row.Effect=why;}
     }
    }catch(Exception e){row.Status="بررسی ناقص";row.BlockReason=e.Message;}
    result.Rows.Add(row);
   }
   result.Rows=result.Rows.OrderByDescending(r=>r.Bytes).ToList();result.CompletedUtc=DateTime.UtcNow;return result;
  }
  private static bool CanMove(ScanContext ctx,string source,string destination,out string reason) {
   reason="";string desktop=Path.GetFullPath(ctx.Desktop).TrimEnd('\\');
   if(!File.Exists(source)||Directory.Exists(source)){reason="فقط فایل مستقل مجاز است؛ پوشه/پروژه حرکت نمی‌کند.";return false;}
   if(!String.Equals(Path.GetDirectoryName(Path.GetFullPath(source)),desktop,StringComparison.OrdinalIgnoreCase)){reason="فقط فایل مستقل در سطح اصلی دسکتاپ مجاز است.";return false;}
   if(!String.Equals(Path.GetFullPath(destination),Path.GetFullPath(TargetFor(ctx,source)),StringComparison.OrdinalIgnoreCase)||!SafetyPolicy.IsWithin(destination,Path.Combine(desktop,"دسکتاپ مرتب"))){reason="مقصد خارج از برنامهٔ مرتب‌سازی است.";return false;}
   if(SafetyPolicy.HasReparseAncestor(source)||SafetyPolicy.HasReparseAncestor(destination)){reason="junction / پیوند اجازهٔ انتقال ندارد.";return false;}
   if(DesktopScanner.IsOriginal(source,out reason))return false;
   string ext=Path.GetExtension(source);if(DesktopScanner.CodeExtensions.Contains(ext)||new[]{".apk",".aab",".lnk",".url",".exe",".msi",".msix",".dll",".ini",".config",".json",".xml",".zip",".rar",".7z",".db",".sqlite",".jks",".keystore",".key",".pfx",".pem"}.Contains(ext,StringComparer.OrdinalIgnoreCase)||Path.GetFileName(source).StartsWith(".env",StringComparison.OrdinalIgnoreCase)){reason="کد، میانبر، APK، ابزار اجرایی، آرشیو یا فایل وابسته حفظ می‌شود تا مسیر برنامه خراب نشود.";return false;}
   if(!SafetyPolicy.IsMediaExtension(ext)&&!new[]{".txt",".md",".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".csv",".rtf",".odt",".ods",".epub"}.Contains(ext,StringComparer.OrdinalIgnoreCase)){reason="نوع فایل برای انتقال مستقل شناخته‌شده نیست؛ فقط در گزارش باقی می‌ماند.";return false;}
   if(File.Exists(destination)||Directory.Exists(destination)){reason="فایلی با همین نام در مقصد وجود دارد؛ هیچ جایگزینی انجام نمی‌شود.";return false;}
   return true;
  }
  public static OperationReport Apply(ScanContext ctx,List<ScanRow> rows,CancellationToken token,Action<string> progress) {
   var report=new OperationReport{Kind="organize-move",Root=ctx.Desktop,FreeBefore=Format.FreeC()};
   foreach(ScanRow row in (rows??new List<ScanRow>()).Where(r=>r.Selected)) {
    if(token.IsCancellationRequested){report.Entries.Add(new OperationEntry{Status="cancelled",Detail="عملیات متوقف شد؛ انتقال‌های قبلی در رسید ثبت شده‌اند."});break;}if(progress!=null)progress("انتقال با رسید: "+row.Name);string reason="";
    try {
     if(!row.Eligible||!CanMove(ctx,row.Path,row.Keeper,out reason)){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="skipped",Detail=row.Eligible?reason:"ردیف برای انتقال مجاز نیست."});continue;}
     if(row.Files.Count!=1||String.IsNullOrEmpty(row.Hash)){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="skipped",Detail="رسید اسکن معتبر نیست."});continue;}
     FileRecord old=row.Files[0];FileInfo current=new FileInfo(row.Path);
     if(current.Length!=old.Bytes||current.LastWriteTimeUtc.Ticks!=old.ModifiedTicks||SafetyPolicy.HashFile(row.Path)!=row.Hash){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="skipped",Detail="فایل بعد از پیش‌نمایش تغییر کرده است؛ دوباره اسکن کنید."});continue;}
     var receipt=new OperationEntry{OriginalPath=row.Path,DestinationPath=row.Keeper,Status="pending",Detail="انتقال هنوز تکمیل نشده است.",Bytes=row.Bytes,Hash=row.Hash};report.Entries.Add(receipt);
     if(!DesktopJournal.Persist(ctx,report,out reason)){receipt.Status="skipped";receipt.Detail="رسید پیش از انتقال ذخیره نشد: "+reason;continue;}
     string parent=Path.GetDirectoryName(row.Keeper);Directory.CreateDirectory(parent);
     if(SafetyPolicy.HasReparseAncestor(parent)){receipt.Status="skipped";receipt.Detail="مقصد پس از اسکن پیوندی شده است.";DesktopJournal.Persist(ctx,report,out reason);continue;}
     try {using(FileStream hold=HoldUnchangedMoveSource(row)) {
      // Deny writers through the final checksum and rename, while allowing our move.
      if(!CanMove(ctx,row.Path,row.Keeper,out reason))throw new IOException(reason);
      File.Move(row.Path,row.Keeper);string after=SafetyPolicy.HashFile(row.Keeper);
      receipt.Status=after==row.Hash?"moved":"verify-failed";receipt.Detail=after==row.Hash?"انتقال تأییدشده با SHA-256؛ قابل بازگردانی.":"فایل مقصد حفظ شد؛ تطابق پس از انتقال نیازمند بررسی است.";if(receipt.Status=="moved")report.ProcessedBytes+=row.Bytes;DesktopJournal.Persist(ctx,report,out reason);
     }}catch(Exception e){receipt.Status=File.Exists(row.Path)?"skipped":"verify-failed";receipt.Detail=e.Message;DesktopJournal.Persist(ctx,report,out reason);}
    }catch(OperationCanceledException){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="cancelled",Detail="لغو شد."});break;}catch(Exception e){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="skipped",Detail=e.Message});}
   }
   report.FreeAfter=Format.FreeC();report.CompletedUtc=DateTime.UtcNow;string saveReason;DesktopJournal.Persist(ctx,report,out saveReason);return report;
  }
  internal static FileStream HoldUnchangedMoveSource(ScanRow row) {
   if(row==null||row.Files==null||row.Files.Count!=1||String.IsNullOrEmpty(row.Hash)||SafetyPolicy.HasReparseAncestor(row.Path))throw new IOException("رسید اسکن یا مسیر انتقال معتبر نیست.");
   FileRecord record=row.Files[0];
   if(!String.Equals(Path.GetFullPath(record.Path),Path.GetFullPath(row.Path),StringComparison.OrdinalIgnoreCase))throw new IOException("مسیر Manifest با فایل انتقال برابر نیست.");
   var hold=new FileStream(row.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
   try {
    if(hold.Length!=record.Bytes||new FileInfo(row.Path).LastWriteTimeUtc.Ticks!=record.ModifiedTicks||SafetyPolicy.HashStream(hold)!=row.Hash)throw new IOException("فایل پس از پیش‌نمایش تغییر کرده است؛ دوباره اسکن کنید.");
    return hold;
   }catch{hold.Dispose();throw;}
  }
  public static OperationReport Restore(OperationReport original,CancellationToken token,Action<string> progress) {
   return Restore(ScanContext.Current(),original,token,progress);
  }
  public static OperationReport Restore(ScanContext ctx,OperationReport original,CancellationToken token,Action<string> progress) {
   var report=new OperationReport{Kind="organize-restore",Root=ctx.Desktop,FreeBefore=Format.FreeC()};
   if(original==null||original.Kind!="organize-move"){report.Entries.Add(new OperationEntry{Status="skipped",Detail="فقط رسید انتقال معتبر قابل بازگردانی است."});return report;}
   if(String.IsNullOrEmpty(original.Root)||!String.Equals(Path.GetFullPath(original.Root),Path.GetFullPath(ctx.Desktop),StringComparison.OrdinalIgnoreCase)){report.Entries.Add(new OperationEntry{Status="skipped",Detail="رسید متعلق به دسکتاپ جاری نیست."});return report;}
   foreach(OperationEntry entry in original.Entries.Where(e=>e.Status=="moved").Reverse()) {
    if(token.IsCancellationRequested){report.Entries.Add(new OperationEntry{Status="cancelled",Detail="بازگردانی متوقف شد؛ رسید کارهای انجام‌شده حفظ شده است."});break;}if(progress!=null)progress("بازگردانی: "+entry.OriginalPath);
    var restored=new OperationEntry{OriginalPath=entry.DestinationPath,DestinationPath=entry.OriginalPath,Bytes=entry.Bytes,Hash=entry.Hash};
    try {
     string source=Path.GetFullPath(entry.DestinationPath),target=Path.GetFullPath(entry.OriginalPath),desktop=Path.GetDirectoryName(target),folder=Path.Combine(desktop,"دسکتاپ مرتب");
     string[] rel=source.Substring(folder.TrimEnd('\\').Length).TrimStart('\\').Split('\\');
     if(!String.Equals(desktop,Path.GetFullPath(ctx.Desktop).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||!SafetyPolicy.IsWithin(source,folder)||rel.Length!=2||!Categories.Contains(rel[0])||!String.Equals(rel[1],Path.GetFileName(target),StringComparison.OrdinalIgnoreCase)||SafetyPolicy.HasReparseAncestor(source)||SafetyPolicy.HasReparseAncestor(target)){restored.Status="skipped";restored.Detail="مسیرهای رسید معتبر یا مستقل نیستند.";}
     else if(File.Exists(target)||Directory.Exists(target)){restored.Status="skipped";restored.Detail="مسیر اصلی اکنون وجود دارد؛ فایل موجود هرگز بازنویسی نمی‌شود.";}
     else if(!File.Exists(source)||String.IsNullOrEmpty(entry.Hash)||new FileInfo(source).Length!=entry.Bytes||SafetyPolicy.HashFile(source)!=entry.Hash){restored.Status="skipped";restored.Detail="فایل مقصد تغییر کرده یا موجود نیست؛ بازگردانی خودکار مجاز نیست.";}
     else{restored.Status="pending";restored.Detail="بازگردانی هنوز تکمیل نشده است.";report.Entries.Add(restored);string reason;if(!DesktopJournal.Persist(ctx,report,out reason)){restored.Status="skipped";restored.Detail="رسید ذخیره نشد: "+reason;continue;}File.Move(source,target);restored.Status="restored";restored.Detail="به مسیر اصلی بازگردانده شد؛ SHA-256 قبل از انتقال تأیید شد.";report.ProcessedBytes+=entry.Bytes;DesktopJournal.Persist(ctx,report,out reason);}
    }catch(Exception e){restored.Status="skipped";restored.Detail=e.Message;}if(!report.Entries.Contains(restored))report.Entries.Add(restored);
   }
   report.FreeAfter=Format.FreeC();report.CompletedUtc=DateTime.UtcNow;string saveReason;DesktopJournal.Persist(ctx,report,out saveReason);return report;
  }
 }
 internal static class DesktopJournal {
  public static bool Persist(ScanContext ctx,OperationReport report,out string reason) {
   reason="";try {
    if(ctx==null||String.IsNullOrEmpty(ctx.ReportDirectory)){reason="پوشهٔ گزارش مشخص نشده است.";return false;}
    string root=Path.GetFullPath(ctx.ReportDirectory);if(SafetyPolicy.HasReparseAncestor(root)){reason="پوشهٔ گزارش پیوندی است.";return false;}
    Directory.CreateDirectory(root);string path=Path.Combine(root,report.Id+".json"),temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary,Format.Json(report),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);return true;
   }catch(Exception e){reason=e.Message;return false;}
  }
 }
}
