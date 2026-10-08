using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Web.Script.Serialization;

namespace ClearGuard {
 // This is an additive deny-list, never an authorization to remove a file.
 public sealed class ProtectedFolderStore {
  private const string Owner="ClearGuard.ProtectedFolders";
  private const int MaximumBytes=65536,MaximumFolders=256;
  private readonly object gate=new object();
  private readonly string directory;
  private List<string> folders=new List<string>();
  private string lastText;
  private bool knownMissing;
  private bool initialized;
  private string loadError;
  public string SettingsFile {get;private set;}
  public string LoadError {get{lock(gate)return loadError;}}
  public bool CanModify {get{lock(gate)return String.IsNullOrEmpty(loadError);}}
  public ReadOnlyCollection<string> Folders {get{lock(gate)return new List<string>(folders).AsReadOnly();}}
  public ProtectedFolderStore(string settingsDirectory) {
   string reason,canonical;
   if(!CanonicalLocalPath(settingsDirectory,false,out canonical,out reason)){directory=settingsDirectory;SettingsFile="";loadError="مسیر تنظیمات معتبر نیست: "+reason;return;}
   directory=canonical;SettingsFile=Path.Combine(directory,"protected-folders.json");Refresh();
  }
  public void Refresh(){lock(gate){if(String.IsNullOrEmpty(SettingsFile))return;try{bool missing;string text;List<string> loaded=ReadSettings(out missing,out text);if(missing&&initialized&&!knownMissing)throw new IOException("فایل تنظیمات قبلاً وجود داشته و اکنون ناپدید شده است؛ نسخهٔ پشتیبان را بررسی کنید.");folders=loaded;knownMissing=missing;lastText=text;initialized=true;loadError=null;}catch(Exception e){loadError="تنظیمات پوشه‌های محافظت‌شده قابل خواندن نیست؛ پاک‌سازی و انتقال متوقف است. "+e.Message;}}}
  public bool TryAdd(string path,out string reason) {
   lock(gate){string canonical;if(!CanonicalLocalPath(path,true,out canonical,out reason))return false;Refresh();if(!CanModify){reason=loadError;return false;}if(folders.Contains(canonical,StringComparer.OrdinalIgnoreCase)){reason="این پوشه قبلاً در فهرست محافظت‌شده است.";return false;}if(folders.Count>=MaximumFolders){reason="حداکثر ۲۵۶ پوشهٔ سفارشی قابل ثبت است.";return false;}var next=new List<string>(folders);next.Add(canonical);return Save(next,out reason);}
  }
  public bool TryRemove(string path,out string reason) {
   lock(gate){string canonical;if(!CanonicalLocalPath(path,false,out canonical,out reason))return false;Refresh();if(!CanModify){reason=loadError;return false;}var next=folders.Where(f=>!String.Equals(f,canonical,StringComparison.OrdinalIgnoreCase)).ToList();if(next.Count==folders.Count){reason="فقط یک ورودی سفارشی موجود قابل حذف است؛ حفاظت‌های ثابت قابل حذف نیستند.";return false;}return Save(next,out reason);}
  }
  public bool IsProtected(string path,bool includeDescendants,out string reason) {
   lock(gate){reason=null;if(!CanModify){reason=loadError;return true;}string canonical;if(!CanonicalLocalPath(path,false,out canonical,out reason))return true;
    if(Overlaps(canonical,directory,includeDescendants)){reason="مسیر تنظیمات ClearGuard همیشه محافظت می‌شود.";return true;}
    foreach(string folder in folders)if(Overlaps(canonical,folder,includeDescendants)){reason="پوشهٔ همیشه محافظت‌شدهٔ سفارشی: "+folder;return true;}
    return false;
   }
  }
  private static bool Overlaps(string path,string root,bool descendants){return SafetyPolicy.IsWithin(path,root)||(descendants&&SafetyPolicy.IsWithin(root,path));}
  private List<string> ReadSettings(out bool missing,out string text) {
   missing=false;text=null;if(File.Exists(directory)||UnsafeAncestor(directory))throw new IOException("مسیر تنظیمات پیوندی یا غیرقابل بررسی است.");if(Directory.Exists(SettingsFile)||UnsafeAncestor(SettingsFile))throw new IOException("فایل تنظیمات پیوندی یا نامعتبر است.");
   try{using(var stream=new FileStream(SettingsFile,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length>MaximumBytes)throw new InvalidDataException("فایل تنظیمات بیش از حد مجاز است.");using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true)){text=reader.ReadToEnd();}}}
   catch(FileNotFoundException){EnsureNewMissingStore();missing=true;return new List<string>();}catch(DirectoryNotFoundException){EnsureNewMissingStore();missing=true;return new List<string>();}
   return Parse(text);
  }
  private void EnsureNewMissingStore(){if(!initialized&&Directory.Exists(directory)&&Directory.GetFileSystemEntries(directory).Any(path=>Path.GetFileName(path).StartsWith("protected-folders.",StringComparison.OrdinalIgnoreCase)))throw new IOException("نشانهٔ تنظیمات قبلی وجود دارد اما فایل اصلی نیست؛ تنظیمات جدید ساخته نمی‌شود.");}
  private static List<string> Parse(string text) {
   if(String.IsNullOrWhiteSpace(text))throw new InvalidDataException("تنظیمات خالی یا خراب است.");
   var serializer=new JavaScriptSerializer{MaxJsonLength=MaximumBytes,RecursionLimit=8};var obj=serializer.DeserializeObject(text) as Dictionary<string,object>;ValidateRootKeys(text,serializer);object owner,schema,items;
   if(obj==null||obj.Count!=3||!obj.TryGetValue("Owner",out owner)||!(owner is string)||(string)owner!=Owner||!obj.TryGetValue("Schema",out schema)||!(schema is int)||(int)schema!=1||!obj.TryGetValue("Folders",out items)||!(items is object[]))throw new InvalidDataException("قالب یا نسخهٔ تنظیمات ناشناخته است؛ بازنویسی نمی‌شود.");
   var result=new List<string>();var input=(object[])items;if(input.Length>MaximumFolders)throw new InvalidDataException("تعداد ورودی‌های تنظیمات بیش از حد مجاز است.");
   foreach(object entry in input){string canonical,reason;if(!(entry is string)||!CanonicalLocalPath((string)entry,false,out canonical,out reason)||!String.Equals(canonical,(string)entry,StringComparison.OrdinalIgnoreCase)||result.Contains(canonical,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("مسیر غیرمعتبر، پیوندی یا تکراری در تنظیمات است.");result.Add(canonical);}return result;
  }
  private static void ValidateRootKeys(string text,JavaScriptSerializer serializer) {
   // JavaScriptSerializer otherwise silently takes the last repeated JSON key.
   var keys=new HashSet<string>(StringComparer.Ordinal);int depth=0;
   for(int i=0;i<text.Length;i++) {
    char character=text[i];if(character=='{'||character=='['){depth++;continue;}if(character=='}'||character==']'){depth--;continue;}if(character!='"')continue;
    int start=i;for(i++;i<text.Length;i++){if(text[i]=='\\'){i++;continue;}if(text[i]=='"')break;}
    int next=i+1;while(next<text.Length&&Char.IsWhiteSpace(text[next]))next++;
    if(depth==1&&next<text.Length&&text[next]==':'&&!keys.Add(serializer.Deserialize<string>(text.Substring(start,i-start+1))))throw new InvalidDataException("کلید تکراری در تنظیمات مجاز نیست.");
   }
  }
  private bool Save(List<string> next,out string reason) {
   reason=null;string temporary=null;bool temporaryOwned=false;
   try {
    if(UnsafeAncestor(directory))throw new IOException("مسیر تنظیمات پیوندی یا غیرقابل بررسی است.");Directory.CreateDirectory(directory);if(UnsafeAncestor(directory))throw new IOException("مسیر تنظیمات تغییر کرده است.");
    string lockPath=Path.Combine(directory,"protected-folders.lock");if(UnsafeAncestor(lockPath)||Directory.Exists(lockPath))throw new IOException("قفل تنظیمات معتبر نیست.");
    using(var writeLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     bool missing;string current;ReadSettings(out missing,out current);if(missing!=knownMissing||!String.Equals(current,lastText,StringComparison.Ordinal))throw new IOException("تنظیمات هم‌زمان تغییر کرده است؛ دوباره بارگذاری کنید.");
     string text=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"Owner",Owner},{"Schema",1},{"Folders",next.ToArray()}});if(Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new IOException("حجم تنظیمات بیش از حد مجاز است.");Parse(text);
     temporary=Path.Combine(directory,"protected-folders.tmp-"+Guid.NewGuid().ToString("N"));using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){temporaryOwned=true;byte[] bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
     if(UnsafeAncestor(SettingsFile)||UnsafeAncestor(directory))throw new IOException("مسیر تنظیمات پیش از ذخیره تغییر کرده است.");
     bool latestMissing;string latest;ReadSettings(out latestMissing,out latest);if(latestMissing!=missing||!String.Equals(latest,current,StringComparison.Ordinal))throw new IOException("فایل تنظیمات پیش از جایگزینی تغییر کرده است؛ بازنویسی متوقف شد.");
     if(missing)File.Move(temporary,SettingsFile);else File.Replace(temporary,SettingsFile,Path.Combine(directory,"protected-folders.backup-"+Guid.NewGuid().ToString("N")+".json"));temporary=null;
     folders=new List<string>(next);lastText=text;knownMissing=false;initialized=true;loadError=null;
    }
    return true;
   }catch(Exception e){reason="تنظیمات ذخیره نشد؛ فهرست قبلی حفظ شده است. "+e.Message;return false;}
   finally{if(temporary!=null&&temporaryOwned)try{if(!UnsafeAncestor(temporary))File.Delete(temporary);}catch{}}
  }
  internal static bool CanonicalLocalPath(string input,bool requireFolder,out string canonical,out string reason) {
   canonical=null;reason="مسیر باید یک پوشهٔ محلی با مسیر کامل باشد.";
   try {
    if(String.IsNullOrWhiteSpace(input)||input.Length>32760||!String.Equals(input,input.Trim(),StringComparison.Ordinal)||input.Length<3||!Char.IsLetter(input[0])||input[1]!=':'||(input[2]!='\\'&&input[2]!='/')||input.StartsWith("\\",StringComparison.Ordinal)||input.IndexOf(':',2)>=0||input.IndexOfAny(new[]{'*','?','%','"','<','>','|','\0'})>=0||input.Contains("${")||input.IndexOfAny(new[]{'\r','\n','\t'})>=0)return false;
    // Reject even legitimate tilde names: Windows 8.3 aliases cannot safely be
    // compared lexically, especially after a protected folder has disappeared.
    string normalized=input.Replace('/','\\');foreach(string part in normalized.Substring(3).Split('\\')){if(part.Length==0)continue;if(part.IndexOf('~')>=0){reason="مسیر دارای ~ یا نام کوتاه 8.3 مجاز نیست؛ مسیر بلند بدون ~ را انتخاب کنید.";return false;}if(part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.Any(Char.IsControl))return false;string stem=part.Split('.')[0];if(new[]{"CON","PRN","AUX","NUL","CLOCK$"}.Contains(stem,StringComparer.OrdinalIgnoreCase)||(stem.Length==4&&(stem.StartsWith("COM",StringComparison.OrdinalIgnoreCase)||stem.StartsWith("LPT",StringComparison.OrdinalIgnoreCase))&&"123456789".Contains(stem[3])))return false;}
    canonical=Path.GetFullPath(normalized);if(canonical.Length>3)canonical=canonical.TrimEnd('\\');if(UnsafeAncestor(canonical)){reason="پیوند، Junction یا مسیر غیرقابل بررسی مجاز نیست.";canonical=null;return false;}
    var drive=new DriveInfo(Path.GetPathRoot(canonical));if(drive.DriveType==DriveType.Network){reason="پوشهٔ شبکه مجاز نیست.";canonical=null;return false;}
    if(requireFolder&&!Directory.Exists(canonical)){reason="برای افزودن، پوشه باید اکنون وجود داشته باشد و قابل بررسی باشد.";canonical=null;return false;}reason=null;return true;
   }catch{canonical=null;return false;}
  }
  // File.Exists suppresses access errors. Probe attributes directly so inaccessible
  // or dangling linked ancestors do not silently look like safe missing folders.
  internal static bool UnsafeAncestor(string path) {
   try{string current=Path.GetFullPath(path);while(!String.IsNullOrEmpty(current)){try{FileAttributes attributes=File.GetAttributes(current);if((attributes&FileAttributes.ReparsePoint)!=0)return true;}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}string parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;}return false;}catch{return true;}
  }
 }
}
