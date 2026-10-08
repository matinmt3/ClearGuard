using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Management;

namespace ClearGuard {
 public sealed class SafetyPolicy {
  private readonly ScanContext context;
  private static readonly HashSet<string> Media = new HashSet<string>((".jpg .jpeg .jpe .jfif .png .apng .webp .heic .heif .avif .gif .bmp .dib .tif .tiff .ico .cur .svg .svgz .psd .psb .ai .eps .raw .dng .cr2 .cr3 .nef .nrw .arw .rw2 .orf .raf .pef .srw .exr .hdr .jxl .jp2 .j2k .jpf .jpx .jpm .pcx .tga .dds .ktx .ktx2 .qoi .mp4 .mkv .mov .avi .m4v .3gp .3g2 .webm .wmv .asf .flv .f4v .mpg .mpeg .mpe .m2v .mts .m2ts .vob .ogv .ogg .rm .rmvb .divx .mxf .mjpg .mjpeg .yuv .mp3 .wav .aac .flac .m4a .opus .wma .aiff .aif .mid .midi").Split(' '),StringComparer.OrdinalIgnoreCase);
  private static readonly HashSet<string> Personal = new HashSet<string>((".apk .aab .apks .xapk .ipa .keystore .jks .pem .key .p12 .pfx .kdbx .doc .docx .xls .xlsx .ppt .pptx .pdf .odt .ods .odp .rtf .one .pst .ost .sqlite-wal .sqlite-shm .lnk .url .ps1 .bat .cmd .sh .java .kt .kts .cs .fs .vb .py .pyw .js .jsx .ts .tsx .go .rs .c .cc .cpp .cxx .h .hpp .swift .dart .php .rb .vue .svelte .html .htm .css .scss .less .sql .ipynb .sln .csproj .vcxproj .gradle").Split(' '),StringComparer.OrdinalIgnoreCase);
  private static readonly HashSet<string> ZipTypes = new HashSet<string>(new[]{".zip",".jar",".aar",".nupkg",".vsix"},StringComparer.OrdinalIgnoreCase);
  private static readonly HashSet<string> Source = new HashSet<string>((".ps1 .bat .cmd .sh .java .kt .kts .cs .fs .vb .py .pyw .js .jsx .ts .tsx .go .rs .c .cc .cpp .cxx .h .hpp .swift .dart .php .rb .vue .svelte .html .htm .css .scss .less .sql .ipynb .sln .csproj .vcxproj .gradle").Split(' '),StringComparer.OrdinalIgnoreCase);
  public SafetyPolicy(ScanContext ctx){if(ctx==null)throw new ArgumentNullException("ctx");context=ctx;}
  public static bool IsMediaExtension(string extension){return Media.Contains(extension.StartsWith(".")?extension:"."+extension);}
  public static bool IsWithin(string path,string root){
   if(String.IsNullOrWhiteSpace(path)||String.IsNullOrWhiteSpace(root))return false;
   try{string p=Path.GetFullPath(path).TrimEnd('\\','/'),r=Path.GetFullPath(root).TrimEnd('\\','/');return String.Equals(p,r,StringComparison.OrdinalIgnoreCase)||p.StartsWith(r+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}catch{return false;}
  }
  public static bool HasReparseAncestor(string path){
   return ProtectedFolderStore.UnsafeAncestor(path);
  }
  public static bool HasProjectAncestor(string path,string cacheRoot){
   try{string dir=Directory.Exists(path)?Path.GetFullPath(path):Path.GetDirectoryName(Path.GetFullPath(path));while(IsWithin(dir,cacheRoot)){
    if(Directory.Exists(Path.Combine(dir,".git"))||File.Exists(Path.Combine(dir,".git"))||File.Exists(Path.Combine(dir,"package.json"))||File.Exists(Path.Combine(dir,"settings.gradle"))||File.Exists(Path.Combine(dir,"settings.gradle.kts"))||File.Exists(Path.Combine(dir,"build.gradle"))||File.Exists(Path.Combine(dir,"build.gradle.kts"))||File.Exists(Path.Combine(dir,"Cargo.toml"))||File.Exists(Path.Combine(dir,"pyproject.toml"))||File.Exists(Path.Combine(dir,"go.mod"))||Directory.GetFiles(dir,"*.csproj").Length>0)return true;
    if(String.Equals(dir,Path.GetFullPath(cacheRoot).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))break;dir=Path.GetDirectoryName(dir);
   }return false;}catch{return true;}
  }
  public bool IsProtectedFile(string path,out string reason){
   reason=null;
   if(IsProtectedOperationPath(path,false,out reason))return true;
   if(HasReparseAncestor(path)){reason="پیوند، Junction یا مسیر غیرقابل بررسی";return true;}
   if(!IsWithin(path,context.UserRoot)&&!IsWithin(path,context.LocalAppData)){reason="خارج از مسیر کاربر؛ ویندوز و برنامه‌های نصب‌شده محافظت می‌شوند";return true;}
   string[] personalRoots={context.Desktop,context.Documents,context.Downloads,Path.Combine(context.UserRoot,"Pictures"),Path.Combine(context.UserRoot,"Videos"),Path.Combine(context.UserRoot,"Music"),Path.Combine(context.UserRoot,"AndroidStudioProjects"),Path.Combine(context.UserRoot,".codex"),Path.Combine(context.UserRoot,".ssh"),Path.Combine(context.UserRoot,".aws"),Path.Combine(context.UserRoot,".android","avd"),Path.Combine(context.LocalAppData,"Android","Sdk")};
   foreach(string root in personalRoots)if(IsWithin(path,root)){reason="مسیر شخصی یا محافظت‌شده";return true;}
   string full=Path.GetFullPath(path);
   if(full.IndexOf("\\LocalHistory\\",StringComparison.OrdinalIgnoreCase)>=0||full.EndsWith("\\LocalHistory",StringComparison.OrdinalIgnoreCase)||full.IndexOf("symexvpn",StringComparison.OrdinalIgnoreCase)>=0||full.IndexOf("proxyx",StringComparison.OrdinalIgnoreCase)>=0||full.IndexOf("codex-runtime",StringComparison.OrdinalIgnoreCase)>=0||full.IndexOf("codex-primary-runtime",StringComparison.OrdinalIgnoreCase)>=0){reason="تاریخچهٔ محلی، Codex یا پروژهٔ اصلی محافظت‌شده";return true;}
   return IsProtectedContent(path,out reason);
  }
  // Source cleanup/organizer have intentionally narrower built-in permissions
  // than cache cleanup; custom protections and app artifacts still deny all actions.
  public bool IsProtectedOperationPath(string path,bool includeDescendants,out string reason){
   if(context.ProtectedFolders.IsProtected(path,includeDescendants,out reason))return true;
   foreach(string root in new[]{Path.Combine(context.LocalAppData,"ClearGuard"),context.ReportDirectory})if(!String.IsNullOrWhiteSpace(root)&&(IsWithin(path,root)||(includeDescendants&&IsWithin(root,path)))){reason="تنظیمات، گزارش‌ها و خروجی‌های ClearGuard همیشه محافظت می‌شوند.";return true;}
   return false;
  }
  public static bool IsProtectedContent(string path,out string reason){return IsProtectedContent(path,out reason,false);}
  public static bool IsProtectedDuplicateContent(string path,out string reason){return IsProtectedContent(path,out reason,true);}
  public static bool IsProtectedContent(string path,out string reason,bool allowSource){
   reason=null;
   try{
    if(!File.Exists(path)){reason="فایل وجود ندارد یا فایل عادی نیست";return true;}
    if(HasReparseAncestor(path)){reason="پیوند یا Junction";return true;}
    string ext=Path.GetExtension(path);
    if(Media.Contains(ext)){reason="عکس، فیلم یا رسانهٔ محافظت‌شده";return true;}
    if(Personal.Contains(ext)&&!(allowSource&&Source.Contains(ext))){reason="فایل شخصی، سورس، APK یا کلید";return true;}
    if(Path.GetFileName(path).Equals(".env",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).StartsWith(".env.",StringComparison.OrdinalIgnoreCase)){reason="تنظیمات حساس پروژه";return true;}
    using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
     byte[] head=new byte[4096];int read=stream.Read(head,0,head.Length);
     if(MediaSignature(head,read)){reason="امضای رسانه حتی با پسوند پنهان";return true;}
     bool zip=read>=4&&head[0]==0x50&&head[1]==0x4b&&(head[2]==3||head[2]==5||head[2]==7);
     if(zip||ZipTypes.Contains(ext)){stream.Position=0;return ArchiveProtected(stream,0,allowSource,new ArchiveBudget(),out reason);}
     if(UnknownCompressed(ext,head,read)){reason="آرشیو ناشناخته؛ محتوا قابل اثبات نیست";return true;}
     stream.Position=0;if(ContainsEmbeddedMedia(stream,null,0)){reason="امضای رسانه داخل Cache یا فایل بسته‌بندی‌شده";return true;}
    }
    return false;
   }catch(Exception ex){reason="بررسی محتوا ممکن نیست: "+ex.GetType().Name;return true;}
  }
  private static bool UnknownCompressed(string ext,byte[] h,int n){return new[]{".7z",".rar",".tar",".gz",".tgz",".bz2",".xz",".zst",".lz",".lzma",".cab",".iso"}.Contains(ext,StringComparer.OrdinalIgnoreCase)||(n>=2&&h[0]==0x1f&&h[1]==0x8b)||(n>=3&&h[0]==0x37&&h[1]==0x7a&&h[2]==0xbc)||(n>=4&&h[0]==0x52&&h[1]==0x61&&h[2]==0x72&&h[3]==0x21)||(n>=3&&h[0]==0x42&&h[1]==0x5a&&h[2]==0x68)||(n>=6&&h[0]==0xfd&&h[1]==0x37&&h[2]==0x7a&&h[3]==0x58)||(n>=4&&h[0]==0x28&&h[1]==0xb5&&h[2]==0x2f&&h[3]==0xfd);}
  private static bool ReliableMagic(byte[] b,int n){
   for(int i=0;i<=n-4;i++){
    switch(b[i]){
     case 0x89:if(i+8<=n&&b[i+1]==0x50&&b[i+2]==0x4e&&b[i+3]==0x47&&b[i+4]==13&&b[i+5]==10&&b[i+6]==26&&b[i+7]==10)return true;break;
     case 0xff:if(b[i+1]==0xd8&&b[i+2]==0xff)return true;break;
     case 0x47:if(i+6<=n&&b[i+1]==0x49&&b[i+2]==0x46&&b[i+3]==0x38&&(b[i+4]==0x39||b[i+4]==0x37)&&b[i+5]==0x61)return true;break;
     case 0x1a:if(b[i+1]==0x45&&b[i+2]==0xdf&&b[i+3]==0xa3)return true;break;
     case 0x49:if(b[i+1]==0x49&&b[i+2]==0x2a&&b[i+3]==0)return true;break;
     case 0x4d:if(b[i+1]==0x4d&&b[i+2]==0&&b[i+3]==0x2a)return true;break;
     case 0x52:if(i+12<=n&&b[i+1]==0x49&&b[i+2]==0x46&&b[i+3]==0x46&&((b[i+8]==0x57&&b[i+9]==0x45&&b[i+10]==0x42&&b[i+11]==0x50)||(b[i+8]==0x41&&b[i+9]==0x56&&b[i+10]==0x49)))return true;break;
     case 0x66:if(b[i+1]==0x74&&b[i+2]==0x79&&b[i+3]==0x70)return true;break;
     case 0x3c:if((b[i+1]==0x73||b[i+1]==0x53)&&(b[i+2]==0x76||b[i+2]==0x56)&&(b[i+3]==0x67||b[i+3]==0x47))return true;break;
    }
   }return false;
  }
  private static bool ContainsEmbeddedMedia(Stream stream,byte[] prefix,int prefixLength,long maxBytes=long.MaxValue,ArchiveBudget budget=null){
   byte[] buffer=new byte[65536+32];int carry=0;if(prefix!=null&&prefixLength>0){if(ReliableMagic(prefix,prefixLength))return true;carry=Math.Min(32,prefixLength);Buffer.BlockCopy(prefix,prefixLength-carry,buffer,0,carry);}int read;
   ObserveRead(budget,prefixLength);long consumed=prefixLength;while((read=stream.Read(buffer,carry,65536))>0){consumed+=read;if(consumed>maxBytes)throw new IOException("Archive inspection work limit exceeded");ObserveRead(budget,read);int total=carry+read;if(ReliableMagic(buffer,total))return true;carry=Math.Min(32,total);Buffer.BlockCopy(buffer,total-carry,buffer,0,carry);}return false;
  }
  private static bool MediaSignature(byte[] b,int n){
   if(n>=3&&b[0]==0xff&&b[1]==0xd8&&b[2]==0xff)return true;
   if(n>=8&&b[0]==0x89&&b[1]==0x50&&b[2]==0x4e&&b[3]==0x47)return true;
   if(n>=6&&b[0]==0x47&&b[1]==0x49&&b[2]==0x46&&b[3]==0x38)return true;
   if(n>=2&&b[0]==0x42&&b[1]==0x4d)return true;
   if(n>=4&&((b[0]==0x49&&b[1]==0x49&&b[2]==0x2a&&b[3]==0)||(b[0]==0x4d&&b[1]==0x4d&&b[2]==0&&b[3]==0x2a)))return true;
   if(n>=4&&b[0]==0&&b[1]==0&&(b[2]==1||b[2]==2)&&b[3]==0)return true;
   if(n>=4&&b[0]==0x1a&&b[1]==0x45&&b[2]==0xdf&&b[3]==0xa3)return true;
   if(n>=12&&Encoding.ASCII.GetString(b,0,4)=="RIFF")return true;
   if(n>=12&&Encoding.ASCII.GetString(b,4,4)=="ftyp")return true;
   if(n>=4&&(Encoding.ASCII.GetString(b,0,4)=="8BPS"||Encoding.ASCII.GetString(b,0,4)=="fLaC"||Encoding.ASCII.GetString(b,0,4)=="OggS"||Encoding.ASCII.GetString(b,0,3)=="FLV"||Encoding.ASCII.GetString(b,0,3)=="ID3"))return true;
   if(n>=4&&b[0]==0&&b[1]==0&&b[2]==1&&(b[3]==0xba||b[3]==0xb3))return true;
   if(n>=16&&b[0]==0x30&&b[1]==0x26&&b[2]==0xb2&&b[3]==0x75)return true;
   string text=Encoding.UTF8.GetString(b,0,n);if(text.IndexOf("<svg",StringComparison.OrdinalIgnoreCase)>=0)return true;
   return false;
  }
  private sealed class ArchiveBudget { public long Expanded; public long ActualRead; }
  private static void ObserveRead(ArchiveBudget budget,long n){if(budget==null)return;if(budget.ActualRead>2L*1024*1024*1024-n)throw new IOException("Expanded archive read budget exceeded");budget.ActualRead+=n;}
  private static bool ArchiveProtected(Stream stream,int depth,bool allowSource,ArchiveBudget budget,out string reason){
   reason=null;
   if(depth>3){reason="آرشیو تو در تو عمیق؛ نیازمند بررسی";return true;}
   // Preserve ZIP comments/trailing payloads: ZipArchive ignores them, but they may hold unique data.
   try {if(!stream.CanSeek||stream.Length<22){reason="ساختار آرشیو قابل اثبات نیست";return true;}long saved=stream.Position;stream.Position=stream.Length-22;byte[] footer=new byte[22];int total=0,n;while(total<22&&(n=stream.Read(footer,total,22-total))>0)total+=n;stream.Position=saved;if(total!=22||footer[0]!=0x50||footer[1]!=0x4b||footer[2]!=0x05||footer[3]!=0x06||footer[20]!=0||footer[21]!=0){reason="آرشیو دارای توضیح، دادهٔ اضافه یا انتهای نامطمئن؛ حفظ می‌شود";return true;}}catch{reason="پایان آرشیو قابل بررسی نیست";return true;}
   try{using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,true)){
    if(zip.Entries.Count>100000){reason="آرشیو بسیار بزرگ؛ بررسی دستی";return true;}
    foreach(ZipArchiveEntry e in zip.Entries){if(String.IsNullOrEmpty(e.Name))continue;if(e.Length>256L*1024*1024||e.Length<0||budget.Expanded>2L*1024*1024*1024-e.Length){reason="حجم بازشدهٔ آرشیو بیش از حد بررسی امن است";return true;}budget.Expanded+=e.Length;string ext=Path.GetExtension(e.FullName);
     if(Media.Contains(ext)||(Personal.Contains(ext)&&!(allowSource&&Source.Contains(ext)))||e.Name.Equals(".env",StringComparison.OrdinalIgnoreCase)||e.Name.StartsWith(".env.",StringComparison.OrdinalIgnoreCase)||e.FullName.IndexOf("LocalHistory/",StringComparison.OrdinalIgnoreCase)>=0||e.FullName.IndexOf("LocalHistory\\",StringComparison.OrdinalIgnoreCase)>=0){reason="آرشیو حاوی رسانه، سورس، APK یا فایل محافظت‌شده";return true;}
     using(Stream entry=e.Open()){byte[] h=new byte[4096];int n=entry.Read(h,0,h.Length);if(MediaSignature(h,n)){reason="آرشیو حاوی امضای رسانه";return true;}
      if(ZipTypes.Contains(ext)||(n>=4&&h[0]==0x50&&h[1]==0x4b&&(h[2]==3||h[2]==5||h[2]==7))){if(e.Length>64*1024*1024){reason="آرشیو داخلی حجیم؛ بررسی دستی";return true;}using(var ms=new MemoryStream()){ObserveRead(budget,n);ms.Write(h,0,n);byte[] buffer=new byte[32768];int chunk;while((chunk=entry.Read(buffer,0,buffer.Length))>0){if(ms.Length+chunk>64*1024*1024){reason="آرشیو داخلی بیش از حد بررسی";return true;}ObserveRead(budget,chunk);ms.Write(buffer,0,chunk);}ms.Position=0;if(ArchiveProtected(ms,depth+1,allowSource,budget,out reason))return true;}}
      if(UnknownCompressed(ext,h,n)){reason="فشردهٔ داخلی غیرقابل بررسی";return true;}
      if(ContainsEmbeddedMedia(entry,h,n,256L*1024*1024,budget)){reason="رسانهٔ داخلی در فایل بسته‌بندی‌شدهٔ آرشیو";return true;}
     }
    }
   }return false;}catch{reason="آرشیو خراب یا ناشناخته";return true;}
  }
  public static string HashFile(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){return HashStream(stream);}}
  public static string HashStream(Stream stream){using(var sha=SHA256.Create()){return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}}
  public static string HashTree(string path){
   if(File.Exists(path))return HashFile(path);
   if(!Directory.Exists(path)||HasReparseAncestor(path))throw new IOException("Invalid or linked directory");
   var files=new List<string>();var dirs=new List<string>();var stack=new Stack<string>();stack.Push(path);while(stack.Count>0){string d=stack.Pop();foreach(string child in Directory.GetDirectories(d)){if(HasReparseAncestor(child))throw new IOException("Linked descendant");dirs.Add(child);stack.Push(child);}foreach(string f in Directory.GetFiles(d)){if(HasReparseAncestor(f))throw new IOException("Linked descendant");files.Add(f);}}
   files.Sort(StringComparer.OrdinalIgnoreCase);dirs.Sort(StringComparer.OrdinalIgnoreCase);var text=new StringBuilder();foreach(string d in dirs)text.Append("D|").Append(d.Substring(path.TrimEnd('\\').Length).ToLowerInvariant()).Append('\n');foreach(string f in files)text.Append("F|").Append(f.Substring(path.TrimEnd('\\').Length).ToLowerInvariant()).Append('|').Append(new FileInfo(f).Length).Append('|').Append(HashFile(f)).Append('\n');using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
  }
 }
 public static class ProcessGuard {
  public static bool Check(string app,ScanContext ctx,out string reason){
   reason=null;string group=app??"";
   try{
    Process[] all=Process.GetProcesses();
    foreach(Process p in all){string name;try{name=p.ProcessName.ToLowerInvariant();}catch{continue;}finally{p.Dispose();}
     if(group=="Android Studio"&&(name=="studio"||name=="studio64")){reason="Android Studio باز است؛ ابتدا آن را ببندید";return false;}
     if(group=="Chrome"&&name=="chrome"){reason="Chrome باز است؛ ابتدا آن را ببندید";return false;}
     if(group=="Gradle"&&(name=="studio"||name=="studio64")){reason="Android Studio باز است؛ احتمال Build فعال";return false;}
     string match=group.ToLowerInvariant();if(new[]{"Chatbox","Oblivion","BlueStacks","4ebur","iGap","Namava"}.Contains(group)&&name.IndexOf(match,StringComparison.OrdinalIgnoreCase)>=0){reason=group+" در حال اجراست";return false;}
     if(group=="BlueStacks"&&new[]{"hd-player","hd-agent","bstksvc","hd-updaterservice","hd-multiinstancemanager","bluestacksservices"}.Contains(name)){reason="فرایند BlueStacks فعال است";return false;}
    }
    if(group=="Gradle"||group=="Android Studio"){
     using(var search=new ManagementObjectSearcher("SELECT Name,CommandLine FROM Win32_Process WHERE Name='java.exe' OR Name='javaw.exe' OR Name='studio.exe' OR Name='studio64.exe'"))using(var objects=search.Get())foreach(ManagementObject item in objects){string command=Convert.ToString(item["CommandLine"]);if(String.IsNullOrWhiteSpace(command)){reason="فرایند Java قابل تشخیص نیست؛ حذف متوقف شد";return false;}if(group=="Gradle"&&(command.IndexOf("gradle",StringComparison.OrdinalIgnoreCase)>=0)){reason="فرایند Gradle فعال است؛ حتی Daemon برای احتیاط حفظ می‌شود";return false;}if(group=="Android Studio"&&command.IndexOf("AndroidStudio",StringComparison.OrdinalIgnoreCase)>=0){reason="فرایند Android Studio فعال است";return false;}}
    }
    if(new[]{"Chatbox","Oblivion","BlueStacks","4ebur","iGap","Namava"}.Contains(group)){
     using(var search=new ManagementObjectSearcher("SELECT Name,CommandLine FROM Win32_Process WHERE Name='Update.exe' OR Name='Squirrel.exe'"))using(var objects=search.Get())foreach(ManagementObject item in objects){string command=Convert.ToString(item["CommandLine"]);if(String.IsNullOrWhiteSpace(command)||command.IndexOf(group,StringComparison.OrdinalIgnoreCase)>=0){reason="Updater فعال یا نامشخص؛ Cache حفظ می‌شود";return false;}}
    }
    return true;
   }catch(Exception ex){reason="وضعیت فرایندها قابل بررسی نیست: "+ex.GetType().Name;return false;}
  }
 }
}
