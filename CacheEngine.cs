using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text;

namespace ClearGuard {
 public sealed class CandidateSpec {
  public string Path {get;set;} public string Category {get;set;} public string App {get;set;} public string Effect {get;set;} public bool ReportOnly {get;set;} public string Allowed {get;set;} public bool Extensionless {get;set;}
  public bool Accepts(string file){string ext=System.IO.Path.GetExtension(file);return (Extensionless&&String.IsNullOrEmpty(ext))||(" "+Allowed+" ").IndexOf(" "+ext.ToLowerInvariant()+" ",StringComparison.Ordinal)>=0;}
 }
 public static class CacheCandidates {
  private static void Add(List<CandidateSpec> list,string path,string app,string effect,string allowed,bool extensionless,bool reportOnly){list.Add(new CandidateSpec{Path=System.IO.Path.GetFullPath(path),App=app,Category="Cache",Effect=effect,Allowed=allowed,Extensionless=extensionless,ReportOnly=reportOnly});}
  public static List<CandidateSpec> Get(ScanContext ctx){
   var list=new List<CandidateSpec>();string local=ctx.LocalAppData,user=ctx.UserRoot;
   Add(list,Path.Combine(user,".gradle","wrapper"),"Gradle","توزیع Gradle هنگام Build بعدی دانلود می‌شود؛ فایل‌های سورس و رسانه حفظ می‌شوند",".zip .jar .dll .so .exe .lck .lock .ok .bin",false,false);
   Add(list,Path.Combine(user,".gradle","daemon"),"Gradle","لاگ و وضعیت Daemon دوباره ساخته می‌شود",".log .bin .lock .lck .tmp",false,false);
   Add(list,Path.Combine(user,".gradle",".tmp"),"Gradle","فایل موقت Gradle دوباره ساخته می‌شود",".tmp .bin .dat .lock .lck .jar .zip",true,false);
   Add(list,Path.Combine(user,".gradle","caches"),"Gradle","طبق درخواست، Gradle caches دست‌نخورده می‌ماند؛ امکان وجود PNG/GIF","",false,true);
   string google=Path.Combine(local,"Google");if(Directory.Exists(google)&&!SafetyPolicy.HasReparseAncestor(google)){try{foreach(string studio in Directory.GetDirectories(google,"AndroidStudio*"))foreach(string leaf in new[]{"index","caches","log","lint","tmp","gmaven.index","maven.google"})Add(list,Path.Combine(studio,leaf),"Android Studio","Index و Cache در بازشدن بعدی دوباره ساخته می‌شوند؛ LocalHistory حفظ می‌شود",".log .tmp .dat .db .bin .idx .index .len .values .keys .hash .lock .tab .i .f .keystream .names .storage .mark .at .record .meta .version .properties .json .xml",true,false);}catch{}}
   string[] updaters={"xyz.chatboxapp.app-updater","oblivion-desktop-updater","bluestacks-services-updater","4ebur.net-updater","igap-web-updater","namava-offline-play-updater"};string[] apps={"Chatbox","Oblivion","BlueStacks","4ebur","iGap","Namava"};for(int i=0;i<updaters.Length;i++)Add(list,Path.Combine(local,updaters[i]),apps[i],"فقط بستهٔ Update دوباره دانلود می‌شود؛ برنامه حذف نمی‌شود",".exe .msi .msp .nupkg .zip .blockmap .tmp .log .json .yml .yaml .sha256",false,false);
   Add(list,Path.Combine(local,"node-gyp","Cache"),"node-gyp","Headers و بسته‌های Node در Build بعدی دانلود می‌شوند؛ سورس محافظت می‌شود",".lib .dll .exe .zip .tar .gz .dat .json .txt .tmp .log",false,false);
   Add(list,Path.Combine(local,"CrashDumps"),"CrashDumps","فقط Dump خرابی؛ امکان تحلیل خرابی قبلی از دست می‌رود",".dmp .mdmp .hdmp",false,false);
   Add(list,Path.Combine(local,"D3DSCache"),"Direct3D","Shader cache ساخته می‌شود؛ اجرای اول بازی ممکن است کندتر باشد",".bin .dat .cache",true,false);
   Add(list,Path.Combine(user,".android","cache"),"Android","Cache دانلود Android دوباره ساخته می‌شود؛ SDK و AVD حفظ می‌شوند",".bin .dat .tmp .xml .json .properties .zip .jar",true,false);
   Add(list,Path.Combine(local,"npm-cache"),"npm","بسته‌های npm از شبکه دوباره دریافت می‌شوند؛ compressed ناشناخته حفظ می‌شود",".log .tmp .json .dat",true,false);
   Add(list,Path.Combine(local,"pip","Cache"),"pip","بسته‌های Python در نصب بعدی دوباره دانلود می‌شوند",".body .json .dat .tmp .whl",true,false);
   Add(list,Path.Combine(local,"go-build"),"Go","فایل‌های Build Go دوباره کامپایل می‌شوند",".a .d .tmp .log .txt",true,false);
   Add(list,Path.Combine(user,"go","pkg","mod","cache","download"),"Go modules","بسته‌های Go دوباره دانلود می‌شوند؛ سورس و رسانه حفظ می‌شوند",".zip .mod .info .lock .tmp",false,false);
   Add(list,Path.Combine(user,".m2","repository"),"Maven","فقط گزارش: artifact محلی و وابستگی شبکه به‌صورت قطعی قابل تفکیک نیست","",false,true);
   Add(list,Path.Combine(user,".codex","cache"),"Codex","فقط گزارش؛ Codex cache در حال اجرا حذف نمی‌شود","",false,true);
   Add(list,Path.Combine(user,".codex",".tmp"),"Codex","فقط گزارش؛ Codex tmp و runtimeها حذف نمی‌شوند","",false,true);
   string temp=Path.Combine(local,"Temp");if(Directory.Exists(temp)&&!SafetyPolicy.HasReparseAncestor(temp)){try{foreach(string runtime in Directory.GetDirectories(temp,"codex-runtime-install-*"))Add(list,runtime,"Codex","فقط گزارش؛ runtime نصب Codex در حال اجرا حفظ می‌شود","",false,true);}catch{}}
   Add(list,Path.Combine(user,".android","avd"),"Android Emulator / AVD","فقط گزارش؛ دستگاه مجازی و Snapshot طبق درخواست حذف نمی‌شوند","",false,true);
   Add(list,Path.Combine(local,"Google","Chrome","User Data","OptimizationGuideOnDeviceModel"),"Chrome AI model","فقط گزارش؛ مدل هوش مصنوعی Chrome به‌صورت خودکار حذف نمی‌شود","",false,true);
   string chrome=Path.Combine(local,"Google","Chrome","User Data");if(Directory.Exists(chrome)&&!SafetyPolicy.HasReparseAncestor(chrome)){try{foreach(string profile in Directory.GetDirectories(chrome)){string leaf=Path.GetFileName(profile);if(leaf=="Default"||leaf.StartsWith("Profile ",StringComparison.OrdinalIgnoreCase))foreach(string sub in new[]{"Cache","Code Cache","GPUCache",Path.Combine("Service Worker","CacheStorage")})Add(list,Path.Combine(profile,sub),"Chrome","فقط Cache دوباره دانلود می‌شود؛ Cookies، Password، History و Bookmark حفظ می‌شوند",".bin .dat .tmp",true,false);}}catch{}}
   return list;
  }
  public static CandidateSpec Match(ScanContext ctx,string path){foreach(CandidateSpec spec in Get(ctx))if(SafetyPolicy.IsWithin(path,spec.Path))return spec;return null;}
 }
 public static class CacheScanner {
  public static ScanResult Scan(ScanContext ctx,CancellationToken token,Action<string> progress){
   ctx.ProtectedFolders.Refresh();
   var result=new ScanResult{Kind="Cache",Root=ctx.UserRoot,FreeBefore=Format.FreeC()};var safety=new SafetyPolicy(ctx);
   foreach(CandidateSpec spec in CacheCandidates.Get(ctx)){token.ThrowIfCancellationRequested();if(!Directory.Exists(spec.Path))continue;if(progress!=null)progress(spec.App+" — "+spec.Path);
    var row=new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileName(spec.Path),Path=spec.Path,Category="Cache",App=spec.App,Effect=spec.Effect};
    string protection;if(safety.IsProtectedOperationPath(spec.Path,true,out protection)){row.BlockReason=protection;row.Status="محافظت‌شده";row.ProtectedCount=1;row.Eligible=false;row.SafeBytes=0;row.Selected=false;result.Rows.Add(row);continue;}
    string processReason=null;bool allowed=!spec.ReportOnly&&ProcessGuard.Check(spec.App,ctx,out processReason);if(spec.ReportOnly)processReason="فقط گزارش؛ خارج از پاک‌سازی خودکار";else if(allowed)processReason=null;
    if(SafetyPolicy.HasReparseAncestor(spec.Path)){row.BlockReason="Junction یا پیوند؛ پیمایش نشد";row.Status="محافظت‌شده";result.Rows.Add(row);continue;}
    var stack=new Stack<string>();stack.Push(spec.Path);int seen=0;
    while(stack.Count>0){token.ThrowIfCancellationRequested();string dir=stack.Pop();string[] dirs,files;try{dirs=Directory.GetDirectories(dir);files=Directory.GetFiles(dir);}catch(Exception ex){result.Warnings.Add(dir+" — "+ex.GetType().Name);row.ProtectedCount++;continue;}
     foreach(string d in dirs){if(SafetyPolicy.HasReparseAncestor(d)||Path.GetFileName(d).Equals("LocalHistory",StringComparison.OrdinalIgnoreCase)){row.ProtectedCount++;continue;}stack.Push(d);}
     foreach(string file in files){token.ThrowIfCancellationRequested();if(++seen>500000){row.ProtectedCount++;result.Warnings.Add(spec.Path+" — محدودیت تعداد فایل، بررسی ناقص");stack.Clear();break;}
      try{var info=new FileInfo(file);row.FileCount++;row.Bytes+=info.Length;string reason;if(!allowed||!spec.Accepts(file)||SafetyPolicy.HasProjectAncestor(file,spec.Path)||safety.IsProtectedFile(file,out reason)){row.ProtectedCount++;continue;}string hash=SafetyPolicy.HashFile(file);info.Refresh();row.Files.Add(new FileRecord{Path=file,Bytes=info.Length,ModifiedTicks=info.LastWriteTimeUtc.Ticks,Hash=hash});row.SafeBytes+=info.Length;}catch(Exception ex){row.ProtectedCount++;result.Warnings.Add(file+" — "+ex.GetType().Name);}
     }
    }
    row.Eligible=allowed&&row.Files.Count>0;row.BlockReason=processReason;row.Status=spec.ReportOnly?"فقط گزارش":!allowed?"برنامه باز / بررسی نامطمئن":row.Eligible?(row.ProtectedCount>0?"انتخابی؛ فایل‌های محافظت‌شده باقی می‌مانند":"قابل بازسازی"):"فایل قابل حذف تأیید نشد";row.Selected=false;result.Rows.Add(row);
   }
   result.Rows=result.Rows.OrderByDescending(r=>r.Bytes).ToList();result.CompletedUtc=DateTime.UtcNow;return result;
  }
 }
 public static class CleanupEngine {
  public static bool ValidateCandidate(ScanContext ctx,ScanRow row,FileRecord file,out string reason){
   reason=null;if(row==null||file==null||String.IsNullOrEmpty(file.Hash)||row.Files==null||!row.Files.Any(f=>f.Path==file.Path&&f.Hash==file.Hash)){reason="Manifest بررسی‌شده وجود ندارد";return false;}
   ctx.ProtectedFolders.Refresh();if(new SafetyPolicy(ctx).IsProtectedOperationPath(row.Path,true,out reason)||new SafetyPolicy(ctx).IsProtectedOperationPath(file.Path,false,out reason))return false;
   try{if(!String.Equals(Path.GetPathRoot(Path.GetFullPath(file.Path)),"C:\\",StringComparison.OrdinalIgnoreCase)){reason="حذف فقط روی درایو C مجاز است";return false;}}catch{reason="مسیر معتبر نیست";return false;}
   CandidateSpec spec=CacheCandidates.Match(ctx,file.Path);if(spec==null||spec.ReportOnly||!SafetyPolicy.IsWithin(file.Path,row.Path)||!String.Equals(spec.Path,Path.GetFullPath(row.Path),StringComparison.OrdinalIgnoreCase)){reason="خارج از فهرست مسیرهای مجاز";return false;}
   if(!spec.Accepts(file.Path)){reason="نوع فایل در این Cache شناخته‌شده نیست";return false;}
   if(SafetyPolicy.HasProjectAncestor(file.Path,spec.Path)){reason="پوشهٔ پروژه داخل Cache پیدا شد؛ حفظ می‌شود";return false;}
   if(!ProcessGuard.Check(spec.App,ctx,out reason))return false;
   if(new SafetyPolicy(ctx).IsProtectedFile(file.Path,out reason))return false;
   try{var info=new FileInfo(file.Path);if(!info.Exists||info.Length!=file.Bytes||info.LastWriteTimeUtc.Ticks!=file.ModifiedTicks||SafetyPolicy.HashFile(file.Path)!=file.Hash){reason="فایل پس از اسکن تغییر کرده یا ناپدید شده";return false;}using(var exclusive=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.None)){}return true;}catch(Exception ex){reason="قفل، دسترسی یا بررسی ناموفق: "+ex.GetType().Name;return false;}
  }
  public static OperationReport Run(ScanContext ctx,List<ScanRow> selected,bool permanent,CancellationToken token,Action<string> progress){
   var report=new OperationReport{Kind="Cache cleanup",Root=ctx.UserRoot,FreeBefore=Format.FreeC(),Permanent=permanent};string journal=Path.Combine(ctx.ReportDirectory,"operation-"+report.Id+".json");SaveJournal(journal,report);
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   try{foreach(ScanRow row in selected??new List<ScanRow>()){if(!row.Eligible){report.Entries.Add(new OperationEntry{OriginalPath=row.Path,Status="Skipped",Detail="مسیر قابل حذف نیست"});SaveJournal(journal,report);continue;}foreach(FileRecord file in row.Files){if(token.IsCancellationRequested){report.Entries.Add(new OperationEntry{Status="Cancelled",Detail="عملیات با درخواست کاربر متوقف شد"});break;}if(!seen.Add(file.Path))continue;
     string reason;if(!ValidateCandidate(ctx,row,file,out reason)){report.Entries.Add(new OperationEntry{OriginalPath=file.Path,Bytes=file.Bytes,Hash=file.Hash,Status="Skipped",Detail=reason});SaveJournal(journal,report);continue;}
     var intent=new OperationEntry{OriginalPath=file.Path,Bytes=file.Bytes,Hash=file.Hash,Status="Pending",Detail="ثبت قصد عملیات قبل از حذف"};report.Entries.Add(intent);SaveJournal(journal,report);if(progress!=null)progress(file.Path);
     try{if(!ValidateCandidate(ctx,row,file,out reason)){intent.Status="Skipped";intent.Detail=reason;}else{
      // Hold a read handle that permits Windows recycle/rename but denies all
      // writers from the final checksum until the operation has completed.
      using(var hold=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
       if(hold.Length!=file.Bytes||SafetyPolicy.HashStream(hold)!=file.Hash)throw new IOException("محتوا پیش از عملیات تغییر کرده است");
       ctx.ProtectedFolders.Refresh();if(new SafetyPolicy(ctx).IsProtectedOperationPath(row.Path,true,out reason)||new SafetyPolicy(ctx).IsProtectedOperationPath(file.Path,false,out reason))throw new IOException(reason);
       if(permanent){File.Delete(file.Path);if(File.Exists(file.Path))throw new IOException("فایل باقی مانده است");intent.Status="Deleted";intent.Detail="Cache تأییدشده حذف دائمی شد";report.ProcessedBytes+=file.Bytes;}else{OperationEntry receipt=RecycleService.Recycle(file.Path);intent.DestinationPath=receipt.DestinationPath;intent.RecycleMetadata=receipt.RecycleMetadata;intent.Status=receipt.Status;intent.Detail=receipt.Detail;if(receipt.Status=="Recycled")report.ProcessedBytes+=file.Bytes;}
      }
     }}
     catch(Exception ex){intent.Status="Skipped";intent.Detail=ex.GetType().Name+": "+ex.Message;}
     SaveJournal(journal,report);
    }if(token.IsCancellationRequested)break;}}finally{report.CompletedUtc=DateTime.UtcNow;report.FreeAfter=Format.FreeC();SaveJournal(journal,report);}return report;
  }
  public static void SaveJournal(string path,OperationReport report){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".writing";File.WriteAllText(tmp,Format.Json(report),new UTF8Encoding(false));if(File.Exists(path))ReplaceJournalWithRetry(tmp,path,delegate(string source,string destination){File.Replace(source,destination,null);},delegate(int milliseconds){Thread.Sleep(milliseconds);});else File.Move(tmp,path);}
  internal static void ReplaceJournalWithRetry(string temporary,string target,Action<string,string> replace,Action<int> delay) {
   int[] pauses={25,50,100,200,200};
   for(int attempt=0;;attempt++) {
    try{replace(temporary,target);return;}
    catch(IOException error) {
     int code=error.HResult&0xffff;
     // Retry only known Windows replacement/share/lock failures. No destructive fallback:
     // both existing paths must remain available, and the same atomic operation is repeated.
     if((error.HResult&unchecked((int)0xffff0000))!=unchecked((int)0x80070000) || (code!=1175 && code!=32 && code!=33) || attempt>=pauses.Length || !File.Exists(temporary) || !File.Exists(target))throw;
     delay(pauses[attempt]);
     if(!File.Exists(temporary) || !File.Exists(target))throw;
    }
   }
  }
 }
 public static class InventoryScanner {
  public static string IdentifyOwner(string file){string p=file.ToLowerInvariant();if(p.EndsWith(".hprof")||p.Contains("androidstudio"))return "Android Studio";if(p.Contains("\\.codex\\")||p.Contains("codex-runtime")||p.Contains("codex-primary-runtime"))return "Codex — محافظت‌شده";if(p.Contains("\\.android\\avd\\"))return "Android Emulator / AVD — محافظت‌شده";if(p.Contains("\\android\\sdk\\"))return "Android SDK / NDK — محافظت‌شده";if(p.Contains("\\google\\chrome\\")&&p.Contains("optimizationguide"))return "Chrome AI model — فقط بررسی";if(p.Contains("\\google\\chrome\\"))return "Chrome";if(p.Contains("\\.gradle\\"))return "Gradle";if(p.Contains("\\.m2\\"))return "Maven";if(p.Contains("\\downloads\\"))return "Downloads — شخصی و محافظت‌شده";if(p.Contains("\\windows\\"))return "Windows — سیستمی و محافظت‌شده";if(p.Contains("\\program files"))return "برنامهٔ نصب‌شده — محافظت‌شده";if(p.Contains("\\programdata\\"))return "ProgramData — محافظت‌شده";return "فایل کاربر / برنامه";}
  private static void TrackLargest(List<FileInfo> largest,FileInfo file){if(largest.Count<100){largest.Add(file);return;}int min=0;for(int i=1;i<largest.Count;i++)if(largest[i].Length<largest[min].Length)min=i;if(file.Length>largest[min].Length)largest[min]=file;}
  public static ScanResult Scan(string root,CancellationToken token,Action<string> progress){
   var result=new ScanResult{Kind="Inventory",Root=Path.GetFullPath(root),FreeBefore=Format.FreeC()};if(!Directory.Exists(result.Root)||SafetyPolicy.HasReparseAncestor(result.Root))throw new IOException("مسیر وجود ندارد یا پیوند است");
   var largest=new List<FileInfo>();string[] children=Directory.GetDirectories(result.Root);foreach(string top in children){token.ThrowIfCancellationRequested();var row=new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileName(top),Path=top,App=IdentifyOwner(top),Category="Inventory",Eligible=false,Status="فقط گزارش؛ حذف مجاز نیست",Effect="حجم صرفاً برای تصمیم کاربر؛ هیچ حذف خودکاری ندارد"};if(SafetyPolicy.HasReparseAncestor(top)){row.BlockReason="Junction یا symlink؛ پیمایش نشد";result.Rows.Add(row);continue;}var stack=new Stack<string>();stack.Push(top);while(stack.Count>0){token.ThrowIfCancellationRequested();string dir=stack.Pop();if(progress!=null)progress(dir);try{foreach(string d in Directory.GetDirectories(dir))if(!SafetyPolicy.HasReparseAncestor(d))stack.Push(d);foreach(string file in Directory.GetFiles(dir)){token.ThrowIfCancellationRequested();try{if(SafetyPolicy.HasReparseAncestor(file))continue;var f=new FileInfo(file);row.Bytes+=f.Length;row.FileCount++;TrackLargest(largest,f);}catch{row.ProtectedCount++;}}}catch(Exception ex){row.ProtectedCount++;if(result.Warnings.Count<2000)result.Warnings.Add(dir+" — "+ex.GetType().Name);}}result.Rows.Add(row);}
   foreach(string f in Directory.GetFiles(result.Root)){token.ThrowIfCancellationRequested();try{if(SafetyPolicy.HasReparseAncestor(f))continue;var info=new FileInfo(f);TrackLargest(largest,info);result.Rows.Add(new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=info.Name,Path=f,Category="Inventory",App=IdentifyOwner(f),Bytes=info.Length,FileCount=1,Eligible=false,Status="فقط گزارش",Effect="هیچ حذف خودکاری ندارد"});}catch{}}
   foreach(FileInfo f in largest.OrderByDescending(f=>f.Length))result.Rows.Add(new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=f.Name,Path=f.FullName,Category="LargeFile",App=IdentifyOwner(f.FullName),Bytes=f.Length,FileCount=1,Eligible=false,Status="فایل حجیم؛ فقط بررسی",Effect=f.Extension.Equals(".hprof",StringComparison.OrdinalIgnoreCase)?"Heap dump برای تحلیل خطا؛ حذف تنها با تصمیم صریح کاربر، خارج از این پاک‌سازی":"مسیر کامل فایل حجیم؛ هیچ حذف خودکاری ندارد"});
   result.Rows=result.Rows.OrderByDescending(r=>r.Bytes).ToList();result.CompletedUtc=DateTime.UtcNow;return result;
  }
 }
 public static class DuplicateScanner {
  private static string HashWithCancel(string file,CancellationToken token){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=System.Security.Cryptography.SHA256.Create()){byte[] buffer=new byte[1024*1024];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,n,null,0);}sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();}}
  public static ScanResult Scan(string root,CancellationToken token,Action<string> progress){var result=new ScanResult{Kind="Duplicate audit",Root=Path.GetFullPath(root),FreeBefore=Format.FreeC()};if(!Directory.Exists(result.Root)||SafetyPolicy.HasReparseAncestor(result.Root))throw new IOException("مسیر معتبر و غیرپیوندی لازم است");var sizes=new Dictionary<long,List<FileInfo>>();var stack=new Stack<string>();stack.Push(result.Root);
   while(stack.Count>0){token.ThrowIfCancellationRequested();string dir=stack.Pop();if(progress!=null)progress("اندازه‌گیری — "+dir);try{foreach(string sub in Directory.GetDirectories(dir))if(!SafetyPolicy.HasReparseAncestor(sub))stack.Push(sub);foreach(string file in Directory.GetFiles(dir)){try{if(SafetyPolicy.HasReparseAncestor(file))continue;var info=new FileInfo(file);if(info.Length<1024*1024)continue;List<FileInfo> group;if(!sizes.TryGetValue(info.Length,out group)){group=new List<FileInfo>();sizes.Add(info.Length,group);}group.Add(info);}catch{}}}catch(Exception ex){if(result.Warnings.Count<2000)result.Warnings.Add(dir+" — "+ex.GetType().Name);}}
   foreach(var bySize in sizes.Where(g=>g.Value.Count>1)){var hashes=new Dictionary<string,List<FileInfo>>(StringComparer.Ordinal);foreach(FileInfo f in bySize.Value){token.ThrowIfCancellationRequested();if(progress!=null)progress("SHA-256 — "+f.FullName);try{long ticks=f.LastWriteTimeUtc.Ticks;string hash=HashWithCancel(f.FullName,token);f.Refresh();if(f.Length!=bySize.Key||f.LastWriteTimeUtc.Ticks!=ticks){result.Warnings.Add(f.FullName+" — فایل در حین اسکن تغییر کرد");continue;}List<FileInfo> group;if(!hashes.TryGetValue(hash,out group)){group=new List<FileInfo>();hashes.Add(hash,group);}group.Add(f);}catch(OperationCanceledException){throw;}catch(Exception ex){if(result.Warnings.Count<2000)result.Warnings.Add(f.FullName+" — "+ex.GetType().Name);}}
    foreach(var identical in hashes.Where(g=>g.Value.Count>1)){List<FileInfo> files=identical.Value.OrderBy(f=>f.FullName.Length).ThenBy(f=>f.FullName,StringComparer.OrdinalIgnoreCase).ToList();string keeper=files[0].FullName;foreach(FileInfo f in files)result.Rows.Add(new ScanRow{Id=Guid.NewGuid().ToString("N"),Name=f.Name,Path=f.FullName,Category="ExactDuplicate",App=InventoryScanner.IdentifyOwner(f.FullName),Bytes=f.Length,FileCount=1,Hash=identical.Key,Keeper=keeper,Eligible=false,Status="همسان بایت‌به‌بایت؛ فقط گزارش",Effect="SHA-256 و اندازه برابر است؛ رسانه، Downloads و فایل شخصی همچنان حذف نمی‌شوند"});}
   }
   result.Rows=result.Rows.OrderByDescending(r=>r.Bytes).ToList();result.CompletedUtc=DateTime.UtcNow;return result;
  }
 }
}
