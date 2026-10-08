using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace ClearGuard {
 public sealed class SpaceDashboardOptions {
  public int MaxEntries {get;set;} public int MaxMilliseconds {get;set;} public int MaxDepth {get;set;} public int MaxCategories {get;set;}
  public SpaceDashboardOptions(){MaxEntries=200000;MaxMilliseconds=60000;MaxDepth=64;MaxCategories=4096;}
 }
 public sealed class SpaceDashboardRow {
  public string CategoryKey {get;set;} public string Name {get;set;} public string Path {get;set;}
  public long ObservedBytes {get;set;} public int FileCount {get;set;} public bool IsComplete {get;set;} public int ErrorCount {get;set;}
  public string Status {get;set;} public string Note {get;set;} public double Ratio {get;set;}
  public long? DeltaBytes {get;set;} public string ChangeKind {get;set;}
  public string SizeDisplay {get{return IsComplete ? SpaceDashboardService.Bytes(ObservedBytes) : "ناقص / نامشخص؛ مشاهده‌شده: "+SpaceDashboardService.Bytes(ObservedBytes);}}
  public string DeltaDisplay {get{return !DeltaBytes.HasValue ? "قابل مقایسه نیست" : (DeltaBytes.Value>0 ? "+" : DeltaBytes.Value<0 ? "−" : "")+SpaceDashboardService.Bytes(DeltaBytes.Value<0 ? -DeltaBytes.Value : DeltaBytes.Value);}}
 }
 public sealed class SpaceSnapshot {
  public int SchemaVersion {get;set;} public string SnapshotId {get;set;} public DateTime CreatedUtc {get;set;}
  public string RootPath {get;set;} public string PolicyVersion {get;set;} public string PolicyKey {get;set;} public string ScopeKey {get;set;}
  public string ExcludedSnapshotDirectory {get;set;} public int MaxEntries {get;set;} public int MaxMilliseconds {get;set;} public int MaxDepth {get;set;} public int MaxCategories {get;set;}
  public bool IsComplete {get;set;} public string Status {get;set;} public long TotalBytes {get;set;} public int FileCount {get;set;} public int SeenEntries {get;set;}
  public List<SpaceDashboardRow> Rows {get;set;} public List<string> Warnings {get;set;}
  public SpaceSnapshot(){Rows=new List<SpaceDashboardRow>();Warnings=new List<string>();}
  public string TotalDisplay {get{return IsComplete ? SpaceDashboardService.Bytes(TotalBytes) : "ناقص؛ مشاهده‌شده: "+SpaceDashboardService.Bytes(TotalBytes);}}
 }
 public sealed class SpaceDashboardComparison {
  public bool IsComparable {get;set;} public string Status {get;set;} public string Reason {get;set;} public string PreviousSnapshotPath {get;set;}
  public List<SpaceDashboardRow> Rows {get;set;}
  public SpaceDashboardComparison(){Rows=new List<SpaceDashboardRow>();}
 }
 // A filesystem boundary permits deterministic fixture-only access/time failures, never production mutation.
 internal class SpaceDashboardFileSystem {
  public virtual IEnumerable<string> EnumerateEntries(string path){return Directory.EnumerateFileSystemEntries(path);}
  public virtual FileAttributes GetAttributes(string path){return File.GetAttributes(path);}
  public virtual long GetLength(string path){return new FileInfo(path).Length;}
  public virtual DriveType GetDriveType(string path){return new DriveInfo(System.IO.Path.GetPathRoot(path)).DriveType;}
  public virtual void SnapshotCheckpoint(string phase){}
 }
 public sealed class SpaceDashboardService {
  public const int MaxSnapshotBytes=4*1024*1024;
  public const string PolicyVersion="logical-path-bytes-local-no-reparse-no-tilde-v3";
  private const int MaxHistoryFiles=128;
  private const string RootFilesKey="root-files",DirectoryPrefix="directory:";
  private readonly string snapshotDirectory;
  private readonly SpaceDashboardFileSystem fileSystem;
  public string SnapshotDirectory {get{return snapshotDirectory;}}
  public SpaceDashboardService(string snapshotDirectory):this(snapshotDirectory,new SpaceDashboardFileSystem()){}
  internal SpaceDashboardService(string snapshotDirectory,SpaceDashboardFileSystem fileSystem){this.fileSystem=fileSystem ?? throwNullFileSystem();this.snapshotDirectory=Canonical(snapshotDirectory);RequireLocalDrive(this.snapshotDirectory,true);}
  private static SpaceDashboardFileSystem throwNullFileSystem(){throw new ArgumentNullException("fileSystem");}
  public SpaceSnapshot Scan(string rootPath,CancellationToken token){return Scan(rootPath,token,new SpaceDashboardOptions());}
  public SpaceSnapshot Scan(string rootPath,CancellationToken token,SpaceDashboardOptions options) {
   if(options==null)throw new ArgumentNullException("options");ValidateOptions(options);string root=Canonical(rootPath);RequireLocalDrive(root,false);try{RequirePlainDirectory(root,false);}catch(InvalidDataException e){throw new ArgumentException("Selected scan root or ancestor cannot be a link/junction.",e);}
   if(Within(root,snapshotDirectory))throw new ArgumentException("The owned snapshot directory cannot be a scan root.");
   var result=new SpaceSnapshot{SchemaVersion=1,SnapshotId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow,RootPath=root,PolicyVersion=PolicyVersion,ExcludedSnapshotDirectory=snapshotDirectory,MaxEntries=options.MaxEntries,MaxMilliseconds=options.MaxMilliseconds,MaxDepth=options.MaxDepth,MaxCategories=options.MaxCategories,IsComplete=true,Status="Complete"};
   result.PolicyKey=Policy(result);var clock=Stopwatch.StartNew();var state=new ScanState(result,options,clock,token);var rootFiles=NewRow(RootFilesKey,"فایل‌های مستقیم ریشه",root);result.Rows.Add(rootFiles);
   Warn(result,"جمع طول منطقی ورودی‌های فایل فقط در ریشهٔ انتخاب‌شده است؛ فضای تخصیص‌یافته/استفاده‌شدهٔ کل درایو نیست. hardlinkها بر حسب مسیر حساب می‌شوند؛ فایل‌ها ممکن است حین اسکن تغییر کنند.");
   if(Within(snapshotDirectory,root))Warn(result,"پوشهٔ ذخیرهٔ snapshot خود برنامه از این محدوده مستثنا است تا تاریخچه باعث رشد مصنوعی نشود.");
   var folders=new List<SpaceDashboardRow>();
   try {
    foreach(string entry in fileSystem.EnumerateEntries(root)) {
     if(!CheckBudget(state))break;if(!TakeEntry(state))break;string full;
     try{full=Canonical(entry);}catch(Exception e){if(!ReadFailure(e))throw;Mark(rootFiles,"مسیر ورودی مبهم یا نامعتبر رد شد ("+e.GetType().Name+").",true);Warn(result,"نام بعضی دسته‌های سطح اول ممکن است مشاهده نشده باشد.");continue;}
     if(!IsImmediateChild(full,root)){Mark(rootFiles,"ورودی خارج از مرز ریشه رد شد.",true);continue;}
     FileAttributes attributes;
     try{attributes=fileSystem.GetAttributes(full);}catch(Exception e){if(!ReadFailure(e))throw;Mark(rootFiles,"خطای خواندن مشخصات ورودی ("+e.GetType().Name+").",true);continue;}
     bool directory=(attributes&FileAttributes.Directory)!=0;
     if(directory) {
      if((attributes&FileAttributes.ReparsePoint)==0 && full.Equals(snapshotDirectory,StringComparison.OrdinalIgnoreCase))continue;
      if(result.Rows.Count>=options.MaxCategories){Stop(state,"محدودیت تعداد دسته‌های سطح اول.",false);break;}
      var row=NewRow(DirectoryPrefix+System.IO.Path.GetFileName(full),Clean(System.IO.Path.GetFileName(full),4096),full);
      if((attributes&FileAttributes.ReparsePoint)!=0){row.Status="SkippedLink";Mark(row,"لینک/junction دنبال نشد؛ اندازهٔ مقصد نامشخص است.",true);result.Rows.Add(row);continue;}
      result.Rows.Add(row);folders.Add(row);
     }else if((attributes&FileAttributes.ReparsePoint)!=0)Mark(rootFiles,"ورودی فایلِ لینک‌شده دنبال نشد.",true);
     else AddFile(full,rootFiles,state);
    }
   }catch(Exception e){if(!ReadFailure(e))throw;Mark(rootFiles,"خواندن ورودی‌های ریشه کامل نشد ("+e.GetType().Name+").",true);Warn(result,"نام بعضی دسته‌های سطح اول ممکن است مشاهده نشده باشد.");}
   if(state.Stopped)Mark(rootFiles,state.StopReason,false);
   foreach(var row in folders) {
    if(state.Stopped){Mark(row,state.StopReason,false);continue;}
    ScanFolder(row,state);
   }
   if(!state.Stopped && !CheckBudget(state))Mark(rootFiles,state.StopReason,false);
   foreach(var row in result.Rows){result.TotalBytes=checked(result.TotalBytes+row.ObservedBytes);result.FileCount=checked(result.FileCount+row.FileCount);}
   result.IsComplete=!state.Stopped && result.Rows.All(x=>x.IsComplete);result.Status=state.Cancelled ? "Cancelled" : result.IsComplete ? "Complete" : "Partial";
   if(!result.IsComplete){Warn(result,"اسکن ناقص است؛ جمع مشاهده‌شده حجم کامل نیست و اختلاف دقیق نمایش داده نمی‌شود.");Warn(result,"برای مقایسه، پوشهٔ کوچک‌تری بدون لینک یا محدودیت دسترسی انتخاب کنید.");}
   result.ScopeKey=Scope(result);SetRatios(result.Rows);return result;
  }
  private void ScanFolder(SpaceDashboardRow row,ScanState state) {
   var stack=new Stack<KeyValuePair<string,int>>();stack.Push(new KeyValuePair<string,int>(row.Path,1));
   while(stack.Count>0 && !state.Stopped) {
    if(!CheckBudget(state))break;var item=stack.Pop();
    if(item.Value>state.Options.MaxDepth){Mark(row,"محدودیت عمق پیمایش؛ زیرپوشه کامل دیده نشد.",true);continue;}
    try {
     RequirePlainDirectory(item.Key,false);
     foreach(string entry in fileSystem.EnumerateEntries(item.Key)) {
      if(!CheckBudget(state) || !TakeEntry(state))break;string full;
      try{full=Canonical(entry);}catch(Exception e){if(!ReadFailure(e))throw;Mark(row,"مسیر ورودی مبهم یا نامعتبر رد شد ("+e.GetType().Name+").",true);continue;}
      if(!IsImmediateChild(full,item.Key) || !Within(full,row.Path) || !Within(full,state.Result.RootPath)){Mark(row,"ورودی خارج از مرز دسته رد شد.",true);continue;}
      try {
       FileAttributes attributes=fileSystem.GetAttributes(full);
       if((attributes&FileAttributes.ReparsePoint)!=0){Mark(row,"لینک/junction داخل دسته دنبال نشد؛ مقدار کامل نامشخص است.",true);continue;}
       if(full.Equals(snapshotDirectory,StringComparison.OrdinalIgnoreCase))continue;
       if((attributes&FileAttributes.Directory)!=0)stack.Push(new KeyValuePair<string,int>(full,item.Value+1));else AddFile(full,row,state);
      }catch(Exception e){if(!ReadFailure(e))throw;Mark(row,"خطای مشخصات ورودی ("+e.GetType().Name+").",true);}
     }
    }catch(Exception e){if(!ReadFailure(e))throw;Mark(row,"پوشه کامل خوانده نشد ("+e.GetType().Name+").",true);}
   }
   if(state.Stopped)Mark(row,state.StopReason,false);
  }
  private void AddFile(string path,SpaceDashboardRow row,ScanState state) {
   try {
    // Recheck the entire plain ancestor chain immediately before reading metadata; never open content.
    RequirePlainDirectory(System.IO.Path.GetDirectoryName(path),false);FileAttributes attributes=fileSystem.GetAttributes(path);
    if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0){Mark(row,"نوع ورودی حین اسکن تغییر کرد؛ دنبال نشد.",true);return;}
    long length=fileSystem.GetLength(path);if(length<0)throw new InvalidDataException("Negative length.");row.ObservedBytes=checked(row.ObservedBytes+length);row.FileCount=checked(row.FileCount+1);
   }catch(Exception e){if(!ReadFailure(e) && !(e is OverflowException))throw;Mark(row,"طول فایل قابل تأیید نبود ("+e.GetType().Name+").",true);}
  }
  private sealed class ScanState {
   public readonly SpaceSnapshot Result;public readonly SpaceDashboardOptions Options;public readonly Stopwatch Clock;public readonly CancellationToken Token;
   public bool Stopped,Cancelled;public string StopReason;
   public ScanState(SpaceSnapshot result,SpaceDashboardOptions options,Stopwatch clock,CancellationToken token){Result=result;Options=options;Clock=clock;Token=token;}
  }
  private static bool CheckBudget(ScanState state){if(state.Stopped)return false;if(state.Token.IsCancellationRequested){Stop(state,"اسکن به درخواست کاربر لغو شد.",true);return false;}if(state.Clock.ElapsedMilliseconds>=state.Options.MaxMilliseconds){Stop(state,"محدودیت زمان پیمایش.",false);return false;}return true;}
  private static bool TakeEntry(ScanState state){if(state.Result.SeenEntries>=state.Options.MaxEntries){Stop(state,"محدودیت تعداد ورودی‌های پیمایش.",false);return false;}state.Result.SeenEntries++;return true;}
  private static void Stop(ScanState state,string reason,bool cancelled){state.Stopped=true;state.Cancelled=cancelled;state.StopReason=reason;Warn(state.Result,reason);}
  private static SpaceDashboardRow NewRow(string key,string name,string path){return new SpaceDashboardRow{CategoryKey=key,Name=name,Path=path,IsComplete=true,Status="Complete",Note="طول منطقی فایل‌ها؛ فقط خواندنی.",ChangeKind="NotCompared"};}
  private static void Mark(SpaceDashboardRow row,string note,bool error){row.IsComplete=false;if(row.Status!="SkippedLink")row.Status="Partial";if(error && row.ErrorCount<Int32.MaxValue)row.ErrorCount++;if(row.Note.Length<1800)row.Note=Clean(row.Note+" "+note,2048);}
  private static void Warn(SpaceSnapshot result,string note){if(result.Warnings.Count<32 && !result.Warnings.Contains(note))result.Warnings.Add(Clean(note,2048));}
  private static void SetRatios(List<SpaceDashboardRow> rows){long max=rows.Count==0 ? 0 : rows.Max(x=>x.ObservedBytes);foreach(var row in rows)row.Ratio=max==0 ? 0 : Math.Min(100,100.0*row.ObservedBytes/max);}
  public SpaceDashboardComparison Compare(SpaceSnapshot current,SpaceSnapshot previous) {
   ValidateSnapshot(current);if(previous!=null)ValidateSnapshot(previous);var result=new SpaceDashboardComparison();result.Rows=current.Rows.Select(Clone).ToList();
   if(previous==null){result.Status="NoPrevious";result.Reason="snapshot قبلی معتبر برای این ریشه و سیاست موجود نیست؛ اختلاف نامشخص است.";return result;}
   if(!current.IsComplete || !previous.IsComplete){result.Status="Incomplete";result.Reason="یک یا هر دو اسکن ناقص‌اند؛ اختلاف دقیق قابل مقایسه نیست.";return result;}
   if(!current.ScopeKey.Equals(previous.ScopeKey,StringComparison.Ordinal) || !current.RootPath.Equals(previous.RootPath,StringComparison.OrdinalIgnoreCase) || current.PolicyKey!=previous.PolicyKey){result.Status="DifferentScope";result.Reason="ریشه، سیاست پیمایش، محدودهٔ استثنا یا محدودیت‌ها متفاوت‌اند؛ قابل مقایسه نیست.";return result;}
   result.IsComparable=true;result.Status="Comparable";result.Reason="اختلاف طول منطقی فقط بین دو پیمایش کاملِ همان محدوده است؛ رشد/کاهش به معنی فضای قابل پاک‌سازی نیست.";
   var prior=previous.Rows.ToDictionary(x=>x.CategoryKey,StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var row in result.Rows){SpaceDashboardRow old;seen.Add(row.CategoryKey);if(prior.TryGetValue(row.CategoryKey,out old)){row.DeltaBytes=row.ObservedBytes-old.ObservedBytes;row.ChangeKind=row.DeltaBytes==0 ? "Unchanged" : "Changed";}else{row.DeltaBytes=row.ObservedBytes;row.ChangeKind="New";}}
   foreach(var old in previous.Rows)if(!seen.Contains(old.CategoryKey)){var missing=Clone(old);missing.ObservedBytes=0;missing.FileCount=0;missing.Ratio=0;missing.DeltaBytes=-old.ObservedBytes;missing.ChangeKind="Missing";missing.Note="دسته در اسکن کامل فعلی وجود ندارد؛ برنامه چیزی حذف نکرده است.";result.Rows.Add(missing);}
   return result;
  }
  public SpaceDashboardComparison CompareWithLatest(SpaceSnapshot current) {
   ValidateSnapshot(current);SpaceSnapshot latest=null;string latestPath=null;bool invalid=false;
   try {
    RequirePlainDirectory(snapshotDirectory,true);int candidates=0;
    foreach(string path in Directory.EnumerateFiles(snapshotDirectory,"snapshot-*.json",SearchOption.TopDirectoryOnly)) {
     if(++candidates>MaxHistoryFiles){var limited=Compare(current,null);limited.Status="HistoryLimit";limited.Reason="بیش از 128 snapshot در تاریخچه است؛ مقایسهٔ خودکار متوقف شد و تاریخچه بدون تغییر باقی ماند.";return limited;}
     try{var candidate=LoadSnapshot(path);if(candidate.SnapshotId==current.SnapshotId || !candidate.RootPath.Equals(current.RootPath,StringComparison.OrdinalIgnoreCase) || candidate.PolicyKey!=current.PolicyKey)continue;if(latest==null || candidate.CreatedUtc>latest.CreatedUtc || (candidate.CreatedUtc==latest.CreatedUtc && String.CompareOrdinal(candidate.SnapshotId,latest.SnapshotId)>0)){latest=candidate;latestPath=path;}}catch(Exception e){if(!ReadFailure(e))throw;invalid=true;}
    }
   }catch(DirectoryNotFoundException){}catch(Exception e){if(!ReadFailure(e))throw;invalid=true;}
   var result=Compare(current,invalid ? null : latest);result.PreviousSnapshotPath=invalid ? null : latestPath;if(invalid){result.Status="InvalidHistory";result.Reason="تاریخچهٔ خراب یا غیرقابل دسترس باعث شد اختلاف خودکار نامشخص بماند؛ تاریخچه بدون تغییر باقی ماند.";}return result;
  }
  public string SaveSnapshot(SpaceSnapshot snapshot){return SaveSnapshot(snapshot,CancellationToken.None);}
  public string SaveSnapshot(SpaceSnapshot snapshot,CancellationToken token) {
   SaveCheckpoint("BeforeSerialize",token);ValidateSnapshot(snapshot);if(snapshot.Status=="Cancelled")throw new InvalidDataException("Cancelled snapshots are not persisted.");
   if(!snapshot.ExcludedSnapshotDirectory.Equals(snapshotDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Snapshot belongs to a different owned store.");
   string json=Serializer().Serialize(ToData(snapshot));byte[] bytes=new UTF8Encoding(false).GetBytes(json);if(bytes.Length>MaxSnapshotBytes)throw new InvalidDataException("Snapshot exceeds bounded JSON size.");
   SaveCheckpoint("BeforeStage",token);RequireLocalDrive(snapshotDirectory,true);RequirePlainDirectory(snapshotDirectory,true);Directory.CreateDirectory(snapshotDirectory);RequirePlainDirectory(snapshotDirectory,false);
   string path=System.IO.Path.Combine(snapshotDirectory,"snapshot-"+snapshot.SnapshotId+".json");RequireOwnedPath(path);
   if(File.Exists(path) || Directory.Exists(path))throw new IOException("Snapshot already exists; it is never overwritten.");
   string stage=System.IO.Path.Combine(snapshotDirectory,".snapshot-pending-"+snapshot.SnapshotId+"-"+Guid.NewGuid().ToString("N")+".tmp");bool created=false,committed=false;
   try {
    token.ThrowIfCancellationRequested();using(var stream=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
     created=true;for(int offset=0;offset<bytes.Length;){SaveCheckpoint("BeforeWrite",token);int count=Math.Min(65536,bytes.Length-offset);stream.Write(bytes,offset,count);offset+=count;}
     SaveCheckpoint("BeforeFlush",token);stream.Flush(true);token.ThrowIfCancellationRequested();
    }
    SaveCheckpoint("BeforeCommit",token);RequirePlainDirectory(snapshotDirectory,false);RequireOwnedPath(path);token.ThrowIfCancellationRequested();
    // Same-directory rename is the commit point and never overwrites an existing final snapshot.
    File.Move(stage,path);committed=true;fileSystem.SnapshotCheckpoint("Committed");return path;
   }finally {
    // Only this invocation's new private staging file is cleaned. Existing snapshots/history are never deleted.
    if(created && !committed)try{RequirePlainDirectory(snapshotDirectory,false);if(File.Exists(stage) && (File.GetAttributes(stage)&(FileAttributes.ReparsePoint|FileAttributes.Directory))==0)File.Delete(stage);}catch(Exception e){if(!ReadFailure(e))throw;}
   }
  }
  private void SaveCheckpoint(string phase,CancellationToken token){token.ThrowIfCancellationRequested();fileSystem.SnapshotCheckpoint(phase);token.ThrowIfCancellationRequested();}
  public SpaceSnapshot LoadSnapshot(string path) {
   string full=RequireOwnedPath(path);RequirePlainDirectory(snapshotDirectory,false);if((File.GetAttributes(full)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new InvalidDataException("Snapshot file cannot be a link or directory.");
   byte[] bytes;
   using(var stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length<2 || stream.Length>MaxSnapshotBytes)throw new InvalidDataException("Snapshot input exceeds bounded size.");bytes=new byte[(int)stream.Length];int count=0,read;while(count<bytes.Length && (read=stream.Read(bytes,count,bytes.Length-count))>0)count+=read;if(count!=bytes.Length || stream.ReadByte()!=-1)throw new InvalidDataException("Snapshot changed while reading.");}
   try {
    string json=new UTF8Encoding(false,true).GetString(bytes);UpdateChecker.ValidateStrictJson(json);
    var snapshot=FromData(Serializer().DeserializeObject(json));ValidateSnapshot(snapshot);
    if(System.IO.Path.GetFileName(full)!="snapshot-"+snapshot.SnapshotId+".json")throw new InvalidDataException("Snapshot ID does not match its owned filename.");
    if(!snapshot.ExcludedSnapshotDirectory.Equals(snapshotDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Snapshot belongs to a different store.");return snapshot;
   }catch(Exception e){if(e is InvalidDataException)throw;if(e is ArgumentException || e is FormatException || e is OverflowException || e is DecoderFallbackException || e is InvalidOperationException)throw new InvalidDataException("Invalid bounded snapshot schema.",e);throw;}
  }
  private string RequireOwnedPath(string path){string full;try{full=Canonical(path);}catch(ArgumentException e){throw new InvalidDataException("Invalid snapshot path.",e);}if(!IsImmediateChild(full,snapshotDirectory) || !System.IO.Path.GetFileName(full).StartsWith("snapshot-",StringComparison.Ordinal) || !full.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Snapshot path is outside the explicitly owned store.");return full;}
  private static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=MaxSnapshotBytes,RecursionLimit=12};}
  private static object ToData(SpaceSnapshot s){return new Dictionary<string,object>{{"SchemaVersion",s.SchemaVersion},{"SnapshotId",s.SnapshotId},{"CreatedUtc",s.CreatedUtc.ToString("o",CultureInfo.InvariantCulture)},{"RootPath",s.RootPath},{"PolicyVersion",s.PolicyVersion},{"PolicyKey",s.PolicyKey},{"ScopeKey",s.ScopeKey},{"ExcludedSnapshotDirectory",s.ExcludedSnapshotDirectory},{"MaxEntries",s.MaxEntries},{"MaxMilliseconds",s.MaxMilliseconds},{"MaxDepth",s.MaxDepth},{"MaxCategories",s.MaxCategories},{"IsComplete",s.IsComplete},{"Status",s.Status},{"TotalBytes",s.TotalBytes},{"FileCount",s.FileCount},{"SeenEntries",s.SeenEntries},{"Warnings",s.Warnings},{"Rows",s.Rows.Select(r=>new Dictionary<string,object>{{"CategoryKey",r.CategoryKey},{"Name",r.Name},{"Path",r.Path},{"ObservedBytes",r.ObservedBytes},{"FileCount",r.FileCount},{"IsComplete",r.IsComplete},{"ErrorCount",r.ErrorCount},{"Status",r.Status},{"Note",r.Note}}).ToArray()}};}
  private static SpaceSnapshot FromData(object value) {
   var d=Data(value,"SchemaVersion SnapshotId CreatedUtc RootPath PolicyVersion PolicyKey ScopeKey ExcludedSnapshotDirectory MaxEntries MaxMilliseconds MaxDepth MaxCategories IsComplete Status TotalBytes FileCount SeenEntries Warnings Rows");
   var s=new SpaceSnapshot{SchemaVersion=Int(d,"SchemaVersion"),SnapshotId=Text(d,"SnapshotId",32),CreatedUtc=DateTime.ParseExact(Text(d,"CreatedUtc",40),"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),RootPath=Text(d,"RootPath",4096),PolicyVersion=Text(d,"PolicyVersion",80),PolicyKey=Text(d,"PolicyKey",64),ScopeKey=Text(d,"ScopeKey",64),ExcludedSnapshotDirectory=Text(d,"ExcludedSnapshotDirectory",4096),MaxEntries=Int(d,"MaxEntries"),MaxMilliseconds=Int(d,"MaxMilliseconds"),MaxDepth=Int(d,"MaxDepth"),MaxCategories=Int(d,"MaxCategories"),IsComplete=Bool(d,"IsComplete"),Status=Text(d,"Status",20),TotalBytes=Long(d,"TotalBytes"),FileCount=Int(d,"FileCount"),SeenEntries=Int(d,"SeenEntries")};
   object[] warnings=Array(d,"Warnings",32);foreach(object warning in warnings){if(!(warning is string) || ((string)warning).Length>2048)throw new InvalidDataException("Invalid warning.");s.Warnings.Add((string)warning);}
   foreach(object row in Array(d,"Rows",4096)){var r=Data(row,"CategoryKey Name Path ObservedBytes FileCount IsComplete ErrorCount Status Note");s.Rows.Add(new SpaceDashboardRow{CategoryKey=Text(r,"CategoryKey",4106),Name=Text(r,"Name",4096),Path=Text(r,"Path",4096),ObservedBytes=Long(r,"ObservedBytes"),FileCount=Int(r,"FileCount"),IsComplete=Bool(r,"IsComplete"),ErrorCount=Int(r,"ErrorCount"),Status=Text(r,"Status",20),Note=Text(r,"Note",2048),ChangeKind="NotCompared"});}SetRatios(s.Rows);return s;
  }
  private static Dictionary<string,object> Data(object value,string keys){var d=value as Dictionary<string,object>;string[] expected=keys.Split(' ');if(d==null || d.Count!=expected.Length || expected.Any(k=>!d.ContainsKey(k)))throw new InvalidDataException("Unexpected snapshot schema fields.");return d;}
  private static string Text(Dictionary<string,object> d,string key,int max){var value=d[key] as string;if(value==null || value.Length>max || value.Any(Char.IsControl))throw new InvalidDataException("Invalid bounded text field.");return value;}
  private static long Long(Dictionary<string,object> d,string key){object value=d[key];if(!(value is int) && !(value is long))throw new InvalidDataException("Expected exact integer.");return Convert.ToInt64(value,CultureInfo.InvariantCulture);}
  private static int Int(Dictionary<string,object> d,string key){return checked((int)Long(d,key));}
  private static bool Bool(Dictionary<string,object> d,string key){if(!(d[key] is bool))throw new InvalidDataException("Expected boolean.");return (bool)d[key];}
  private static object[] Array(Dictionary<string,object> d,string key,int max){var value=d[key] as object[];if(value==null || value.Length>max)throw new InvalidDataException("Invalid bounded array.");return value;}
  private static void ValidateSnapshot(SpaceSnapshot s) {
   if(s==null)throw new ArgumentNullException("snapshot");try {
    if(s.SchemaVersion!=1 || s.PolicyVersion!=PolicyVersion || s.SnapshotId==null || s.SnapshotId.Length!=32 || s.SnapshotId.Any(c=>!((c>='0' && c<='9') || (c>='a' && c<='f'))) || s.CreatedUtc.Kind!=DateTimeKind.Utc || s.CreatedUtc.Year<2000 || s.CreatedUtc>DateTime.UtcNow.AddMinutes(5))throw new InvalidDataException("Invalid snapshot identity/version/date.");
    if(Canonical(s.RootPath)!=s.RootPath || Canonical(s.ExcludedSnapshotDirectory)!=s.ExcludedSnapshotDirectory || Within(s.RootPath,s.ExcludedSnapshotDirectory))throw new InvalidDataException("Invalid canonical root/store.");
    ValidateOptions(new SpaceDashboardOptions{MaxEntries=s.MaxEntries,MaxMilliseconds=s.MaxMilliseconds,MaxDepth=s.MaxDepth,MaxCategories=s.MaxCategories});
    if(s.PolicyKey!=Policy(s) || s.ScopeKey!=Scope(s) || (s.Status!="Complete" && s.Status!="Partial" && s.Status!="Cancelled") || s.IsComplete!=(s.Status=="Complete"))throw new InvalidDataException("Invalid scope/policy/completion claim.");
    if(s.Rows==null || s.Rows.Count<1 || s.Rows.Count>s.MaxCategories || s.Warnings==null || s.Warnings.Count>32 || s.Warnings.Any(w=>w==null || w.Length>2048 || w.Any(Char.IsControl)) || s.TotalBytes<0 || s.FileCount<0 || s.SeenEntries<0 || s.SeenEntries>s.MaxEntries || s.FileCount>s.SeenEntries)throw new InvalidDataException("Snapshot aggregates exceed limits.");
    var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;int files=0;int rootRows=0;
    foreach(var row in s.Rows) {
     if(row==null || row.CategoryKey==null || !keys.Add(row.CategoryKey) || row.Name==null || row.Name.Length>4096 || row.Name.Any(Char.IsControl) || row.Path==null || row.Path.Length>4096 || row.Note==null || row.Note.Length>2048 || row.Note.Any(Char.IsControl) || row.ObservedBytes<0 || row.FileCount<0 || row.FileCount>s.MaxEntries || (row.FileCount==0 && row.ObservedBytes!=0) || row.ErrorCount<0 || row.ErrorCount>s.MaxEntries+1 || (row.IsComplete && row.ErrorCount!=0) || (row.Status!="Complete" && row.Status!="Partial" && row.Status!="SkippedLink") || row.IsComplete!=(row.Status=="Complete"))throw new InvalidDataException("Invalid bounded category.");
     if(row.CategoryKey==RootFilesKey){rootRows++;if(row.Path!=s.RootPath || row.Name!="فایل‌های مستقیم ریشه")throw new InvalidDataException("Invalid root-file category.");}
     else {if(!row.CategoryKey.StartsWith(DirectoryPrefix,StringComparison.Ordinal) || !IsImmediateChild(row.Path,s.RootPath) || row.CategoryKey!=DirectoryPrefix+System.IO.Path.GetFileName(row.Path) || row.Name!=Clean(System.IO.Path.GetFileName(row.Path),4096) || row.Path.Equals(s.ExcludedSnapshotDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Category is outside one-level root boundary.");}
     if(Canonical(row.Path)!=row.Path)throw new InvalidDataException("Category path is not canonical.");bytes=checked(bytes+row.ObservedBytes);files=checked(files+row.FileCount);
    }
    if(rootRows!=1 || bytes!=s.TotalBytes || files!=s.FileCount || (s.IsComplete && s.Rows.Any(x=>!x.IsComplete)))throw new InvalidDataException("Snapshot totals/completeness are inconsistent.");
   }catch(Exception e){if(e is InvalidDataException)throw;if(e is ArgumentException || e is OverflowException || e is NotSupportedException)throw new InvalidDataException("Invalid bounded snapshot.",e);throw;}
  }
  private static void ValidateOptions(SpaceDashboardOptions options){if(options.MaxEntries<1 || options.MaxEntries>500000 || options.MaxMilliseconds<1 || options.MaxMilliseconds>60000 || options.MaxDepth<1 || options.MaxDepth>64 || options.MaxCategories<1 || options.MaxCategories>4096)throw new ArgumentOutOfRangeException("options","Dashboard budgets must remain within production upper bounds.");}
  private static string Policy(SpaceSnapshot s){return Hash(PolicyVersion+"\n"+s.MaxEntries.ToString(CultureInfo.InvariantCulture)+"\n"+s.MaxMilliseconds.ToString(CultureInfo.InvariantCulture)+"\n"+s.MaxDepth.ToString(CultureInfo.InvariantCulture)+"\n"+s.MaxCategories.ToString(CultureInfo.InvariantCulture)+"\n"+s.ExcludedSnapshotDirectory.ToUpperInvariant());}
  private static string Scope(SpaceSnapshot s){return Hash(s.RootPath.ToUpperInvariant()+"\n"+s.PolicyKey+"\n"+(s.IsComplete ? "Complete" : "Incomplete"));}
  private static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  private static SpaceDashboardRow Clone(SpaceDashboardRow r){return new SpaceDashboardRow{CategoryKey=r.CategoryKey,Name=r.Name,Path=r.Path,ObservedBytes=r.ObservedBytes,FileCount=r.FileCount,IsComplete=r.IsComplete,ErrorCount=r.ErrorCount,Status=r.Status,Note=r.Note,Ratio=r.Ratio,ChangeKind="NotCompared"};}
  private static string Canonical(string path){if(String.IsNullOrWhiteSpace(path) || path.Length>4096 || path.Any(Char.IsControl) || path.Length<3 || !Char.IsLetter(path[0]) || path[1]!=':' || (path[2]!='\\' && path[2]!='/') || path.StartsWith(@"\\",StringComparison.Ordinal) || path.IndexOf(':',2)>=0 || path.IndexOf('%')>=0)throw new ArgumentException("Choose an absolute local directory without unresolved variables or streams.");foreach(string component in path.Substring(3).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries)){if(component.IndexOf('~')>=0)throw new ArgumentException("Tilde-bearing components and 8.3 short-path aliases are conservatively refused.");if(component!="." && component!=".." && (component.EndsWith(".",StringComparison.Ordinal) || component.EndsWith(" ",StringComparison.Ordinal)))throw new ArgumentException("Ambiguous trailing-dot/space path aliases are refused.");}string full=System.IO.Path.GetFullPath(path).Replace(System.IO.Path.AltDirectorySeparatorChar,System.IO.Path.DirectorySeparatorChar);string drive=System.IO.Path.GetPathRoot(full);if(!LocalDriveType(new DriveInfo(drive).DriveType))throw new ArgumentException("Network, unavailable and non-local drives are refused.");return full.Length==drive.Length ? drive : full.TrimEnd(System.IO.Path.DirectorySeparatorChar);}
  private static bool LocalDriveType(DriveType type){return type==DriveType.Fixed || type==DriveType.Removable || type==DriveType.Ram;}
  private void RequireLocalDrive(string path,bool fixedStore){DriveType type=fileSystem.GetDriveType(path);if(fixedStore ? type!=DriveType.Fixed : !LocalDriveType(type))throw new ArgumentException(fixedStore ? "The owned snapshot store must be on a local fixed drive." : "The scan root must be on a local fixed, removable or RAM drive.");}
  private static bool Within(string path,string root){return path.Equals(root,StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
  private static bool IsImmediateChild(string path,string root){return !path.Equals(root,StringComparison.OrdinalIgnoreCase) && String.Equals(System.IO.Path.GetDirectoryName(path),root,StringComparison.OrdinalIgnoreCase);}
  private static void RequirePlainDirectory(string path,bool allowMissing) {
   try {
    var node=new DirectoryInfo(path);bool first=true;
    while(node!=null){if(node.Exists){if((node.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Directory or ancestor is a link/junction; it is never followed.");}else if(!allowMissing || !first && node.Parent==null)throw new DirectoryNotFoundException("Directory is not available.");first=false;node=node.Parent;}
    if(!allowMissing && !Directory.Exists(path))throw new DirectoryNotFoundException("Directory is not available.");
   }catch(Exception e){if(e is InvalidDataException)throw;if(!ReadFailure(e))throw;if(allowMissing)throw new InvalidDataException("Owned directory cannot be safely validated.",e);throw new ArgumentException("Selected directory is unavailable or contains a linked ancestor.",e);}
  }
  private static bool ReadFailure(Exception e){return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is ArgumentException || e is NotSupportedException;}
  private static string Clean(string text,int limit){if(text==null)return "";var builder=new StringBuilder(Math.Min(text.Length,limit));foreach(char c in text){if(builder.Length>=limit)break;if(!Char.IsControl(c))builder.Append(c);}return builder.ToString();}
  public static string Bytes(long value){string[] units={"B","KiB","MiB","GiB","TiB","PiB","EiB"};double size=value;int unit=0;while(size>=1024 && unit<units.Length-1){size/=1024;unit++;}return size.ToString(unit==0 ? "0" : "0.##",CultureInfo.InvariantCulture)+" "+units[unit];}
 }
}
