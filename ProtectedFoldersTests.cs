using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.IO.Compression;

namespace ClearGuard {
 public sealed class ProtectedFoldersTestEntry { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
 public sealed class ProtectedFoldersTestReport {
  public int Passed {get;set;} public int Failed {get;set;} public int Skipped {get;set;}
  public bool RealUserFilesModified {get;set;}
  public List<ProtectedFoldersTestEntry> Tests {get;set;}
  public ProtectedFoldersTestReport(){Tests=new List<ProtectedFoldersTestEntry>();}
 }
 public static class ProtectedFoldersTests {
  public static ProtectedFoldersTestReport RunTests(string fixtureRoot) {
   string allowed=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests")),requested=Path.GetFullPath(fixtureRoot);
   if(!SafetyPolicy.IsWithin(requested,allowed)||SafetyPolicy.HasReparseAncestor(requested))throw new InvalidOperationException("Protection tests require isolated non-linked work\\ClearGuardTests fixtures.");
   string fixture=Path.Combine(requested,"ProtectedFolders-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
   var report=new ProtectedFoldersTestReport();int sequence=0;
   Func<ScanContext> context=delegate {string root=Path.Combine(fixture,(++sequence).ToString("D2"));var c=new ScanContext{UserRoot=root,LocalAppData=Path.Combine(root,"AppData","Local"),Desktop=Path.Combine(root,"Desktop"),Documents=Path.Combine(root,"Documents"),Downloads=Path.Combine(root,"Downloads"),ReportDirectory=Path.Combine(root,"Reports"),IsFixture=true};Directory.CreateDirectory(c.LocalAppData);Directory.CreateDirectory(c.Desktop);Directory.CreateDirectory(c.Documents);Directory.CreateDirectory(c.Downloads);return c;};
   Run(report,"Protected folders: add reload remove persist only app settings",delegate {
    var c=context();string folder=Path.Combine(c.UserRoot,"private");Directory.CreateDirectory(folder);string file=Put(folder,"keep.dat","private fixture");DateTime modified=File.GetLastWriteTimeUtc(file);string reason;
    Assert(c.ProtectedFolders.TryAdd(folder+"\\",out reason),reason);Assert(c.ProtectedFolders.Folders.Count==1,"Entry missing.");Assert(!Directory.EnumerateFileSystemEntries(folder).Any(p=>p!=file),"Adding protection wrote into protected folder.");
    var loaded=new ProtectedFolderStore(Path.GetDirectoryName(c.ProtectedFolders.SettingsFile));Assert(loaded.Folders.Count==1,"Protection did not survive reload.");Assert(File.ReadAllText(file)=="private fixture"&&File.GetLastWriteTimeUtc(file)==modified,"Protected file changed.");
    Assert(loaded.TryRemove(folder.ToUpperInvariant(),out reason),reason);Assert(new ProtectedFolderStore(Path.GetDirectoryName(loaded.SettingsFile)).Folders.Count==0,"Removal not persisted.");Assert(File.ReadAllText(file)=="private fixture","Removing custom setting changed user data.");
   });
   Run(report,"Protected folders: strict path validation and existing-folder add",delegate {
    var c=context();string missing=Path.Combine(c.UserRoot,"absent"),file=Put(c.UserRoot,"file.dat","fixture");string reason;
    foreach(string bad in new[]{"", "relative", "..\\relative", "C:relative", "\\root-relative", "\\\\server\\share", "\\\\?\\C:\\private", "\\\\.\\C:\\private", "%USERPROFILE%\\private", "C:\\private:stream", "C:\\wild*", "C:\\wild?", "C:\\foo\\..\\bar", "C:\\foo.\\bar", "C:\\CON\\bar",missing,file})Assert(!c.ProtectedFolders.TryAdd(bad,out reason)&&!String.IsNullOrEmpty(reason),"Invalid path accepted: "+bad);
    Assert(c.ProtectedFolders.Folders.Count==0&&!File.Exists(c.ProtectedFolders.SettingsFile),"Rejected inputs created settings.");
   });
   Run(report,"Protected folders: case-insensitive hierarchy rejects prefix siblings",delegate {
    var c=context();string root=Path.Combine(c.UserRoot,"private");Directory.CreateDirectory(root);Directory.CreateDirectory(root+"-sibling");string reason;Assert(c.ProtectedFolders.TryAdd(root,out reason),reason);
    Assert(c.ProtectedFolders.IsProtected(Path.Combine(root.ToUpperInvariant(),"child","keep.dat"),false,out reason),"Protected child escaped.");Assert(!c.ProtectedFolders.IsProtected(Path.Combine(root+"-sibling","safe.dat"),false,out reason),"Prefix sibling incorrectly protected.");Assert(c.ProtectedFolders.IsProtected(c.UserRoot,true,out reason),"Ancestor operation would encompass protected folder.");
    Assert(!c.ProtectedFolders.TryAdd(root.ToUpperInvariant(),out reason)&&c.ProtectedFolders.Folders.Count==1,"Duplicate path was added.");
   });
   Run(report,"Protected folders: vanished entries remain protected on load",delegate {
    var c=context();string root=Path.Combine(c.UserRoot,"vanishing");Directory.CreateDirectory(root);string reason;Assert(c.ProtectedFolders.TryAdd(root,out reason),reason);Directory.Delete(root);
    var loaded=new ProtectedFolderStore(Path.GetDirectoryName(c.ProtectedFolders.SettingsFile));Assert(String.IsNullOrEmpty(loaded.LoadError)&&loaded.Folders.Count==1,"Missing folder caused lost protection.");Assert(loaded.IsProtected(Path.Combine(root,"future.dat"),false,out reason),"Future child escaped stale prefix protection.");
   });
   Run(report,"Protected folders: unknown malformed and locked settings fail closed without overwrite",delegate {
    foreach(string bad in new[]{"{broken", "{\"Owner\":\"OtherApp\",\"Schema\":1,\"Folders\":[]}", "{\"Owner\":\"ClearGuard.ProtectedFolders\",\"Schema\":2,\"Folders\":[]}", "{\"Owner\":\"ClearGuard.ProtectedFolders\",\"Schema\":1,\"Folders\":[\"relative\"]}", "{\"Owner\":\"ClearGuard.ProtectedFolders\",\"Schema\":1,\"Folders\":[],\"Unexpected\":true}", "{\"Owner\":\"ClearGuard.ProtectedFolders\",\"Schema\":1,\"Folders\":[],\"Folders\":[]}",new string(' ',65537)}) {
     var c=context();Directory.CreateDirectory(Path.GetDirectoryName(c.ProtectedFolders.SettingsFile));File.WriteAllText(c.ProtectedFolders.SettingsFile,bad);c.ProtectedFolders.Refresh();string reason;Assert(!c.ProtectedFolders.CanModify&&!String.IsNullOrEmpty(c.ProtectedFolders.LoadError),"Invalid settings were treated as empty.");Assert(!c.ProtectedFolders.TryAdd(c.Documents,out reason)&&File.ReadAllText(c.ProtectedFolders.SettingsFile)==bad,"Invalid unknown file overwritten.");Assert(c.ProtectedFolders.IsProtected(Path.Combine(c.UserRoot,"anything"),false,out reason),"Invalid settings did not block operations.");
    }
    var locked=context();string why;Assert(locked.ProtectedFolders.TryAdd(locked.Documents,out why),why);using(var hold=new FileStream(locked.ProtectedFolders.SettingsFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){locked.ProtectedFolders.Refresh();Assert(!locked.ProtectedFolders.CanModify&&locked.ProtectedFolders.Folders.Count==1,"Locked settings lost prior entries or allowed operation.");Assert(!locked.ProtectedFolders.TryRemove(locked.Documents,out why),"Locked settings were rewritten.");}
   });
   Run(report,"Protected folders: stale cache row and nested overlap cannot bypass protection",delegate {
    var c=context();string cache=Path.Combine(c.LocalAppData,"node-gyp","Cache"),file=Put(cache,"safe.dat","SAFE GENERATED CACHE");var row=CacheScanner.Scan(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,cache));Assert(row.Eligible,"Initial cache fixture was not eligible.");string nested=Path.Combine(cache,"user-private");Directory.CreateDirectory(nested);string why;Assert(c.ProtectedFolders.TryAdd(nested,out why),why);
    string reason;Assert(!CleanupEngine.ValidateCandidate(c,row,row.Files[0],out reason),"Nested protection did not invalidate entire stale candidate.");row.Selected=true;var op=CleanupEngine.Run(c,new List<ScanRow>{row},true,CancellationToken.None,null);Assert(op.ProcessedBytes==0&&File.Exists(file),"Stale cache row deleted protected overlap.");
    var scan=CacheScanner.Scan(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,cache));Assert(!scan.Eligible&&scan.SafeBytes==0&&scan.Status=="محافظت‌شده"&&scan.Files.Count==0,"Protected cache row retained action bytes.");
   });
   Run(report,"Protected folders: source protected keeper and stale duplicate rows are blocked",delegate {
    var c=context();string first=Path.Combine(c.Desktop,"kept","project-v2.zip"),duplicate=Path.Combine(c.Desktop,"copy","project-copy.zip");Zip(first);File.Copy(first,Prepare(duplicate));var scan=DesktopScanner.Scan(c,CancellationToken.None,null);var row=scan.Rows.Single(r=>r.Eligible);Assert(!String.IsNullOrEmpty(row.Keeper),"Initial duplicate lacks keeper.");string protectedParent=Path.GetDirectoryName(row.Keeper),reason;Assert(c.ProtectedFolders.TryAdd(protectedParent,out reason),reason);row.Selected=true;
    Assert(!DesktopScanner.VerifySourceRow(c,row,CancellationToken.None,out reason),"Protected keeper remained eligible.");var op=DesktopCleanup.Run(c,new List<ScanRow>{row},CancellationToken.None,null);Assert(op.ProcessedBytes==0&&File.Exists(first)&&File.Exists(duplicate),"Protected source cleanup mutated archive.");
    var rescan=DesktopScanner.Scan(c,CancellationToken.None,null);Assert(rescan.Rows.All(r=>!r.Eligible)&&rescan.Rows.Any(r=>r.Status=="محافظت‌شده"),"Rescan used protected archive as keeper.");
   });
   Run(report,"Protected folders: folder candidate descendant protection is visible",delegate {
    var c=context();string project=Path.Combine(c.Desktop,"project"),nested=Path.Combine(project,"private");Directory.CreateDirectory(nested);Put(project,"main.cs","class GeneratedFixture {}");string reason;Assert(c.ProtectedFolders.TryAdd(nested,out reason),reason);var row=DesktopScanner.Scan(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,project));Assert(row.Status=="محافظت‌شده"&&!row.Eligible&&row.SafeBytes==0,"Protected descendant did not block source folder candidate.");
   });
   Run(report,"Protected folders: organizer stale source destination and restore are blocked",delegate {
    var c=context();string file=Put(c.Desktop,"readme.txt","GENERATED ORGANIZER FIXTURE");var row=Organizer.Preview(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,file));Assert(row.Eligible,"Initial organizer fixture was not eligible.");string reason;Assert(c.ProtectedFolders.TryAdd(c.Desktop,out reason),reason);row.Selected=true;var op=Organizer.Apply(c,new List<ScanRow>{row},CancellationToken.None,null);Assert(op.ProcessedBytes==0&&File.Exists(file)&&!File.Exists(row.Keeper),"Stale protected organizer source moved.");Assert(Organizer.Preview(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,file)).Status=="محافظت‌شده","Organizer protected state not visible.");
    var destinationContext=context();string second=Put(destinationContext.Desktop,"manual.txt","GENERATED ORGANIZER FIXTURE");var targetRow=Organizer.Preview(destinationContext,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,second));Directory.CreateDirectory(Path.GetDirectoryName(targetRow.Keeper));Assert(destinationContext.ProtectedFolders.TryAdd(Path.GetDirectoryName(targetRow.Keeper),out reason),reason);targetRow.Selected=true;Assert(Organizer.Apply(destinationContext,new List<ScanRow>{targetRow},CancellationToken.None,null).ProcessedBytes==0&&File.Exists(second),"Protected destination received move.");
    var restoreContext=context();string original=Path.Combine(restoreContext.Desktop,"restorable.txt"),moved=Organizer.TargetFor(restoreContext,original);Put(Path.GetDirectoryName(moved),Path.GetFileName(moved),"GENERATED RESTORE FIXTURE");var receipt=new OperationReport{Kind="organize-move",Root=restoreContext.Desktop};receipt.Entries.Add(new OperationEntry{OriginalPath=original,DestinationPath=moved,Bytes=new FileInfo(moved).Length,Hash=SafetyPolicy.HashFile(moved),Status="moved"});Assert(restoreContext.ProtectedFolders.TryAdd(restoreContext.Desktop,out reason),reason);Assert(Organizer.Restore(restoreContext,receipt,CancellationToken.None,null).ProcessedBytes==0&&!File.Exists(original)&&File.Exists(moved),"Protected restore source or target moved.");
   });
   Run(report,"Protected folders: settings corruption after scan blocks final cleanup",delegate {
    var c=context();string cache=Path.Combine(c.LocalAppData,"node-gyp","Cache"),file=Put(cache,"safe.dat","SAFE GENERATED CACHE");var row=CacheScanner.Scan(c,CancellationToken.None,null).Rows.Single(r=>Same(r.Path,cache));string reason;Assert(c.ProtectedFolders.TryAdd(c.Documents,out reason),reason);File.WriteAllText(c.ProtectedFolders.SettingsFile,"{broken");Assert(!CleanupEngine.ValidateCandidate(c,row,row.Files[0],out reason)&&File.Exists(file),"Stale settings snapshot bypassed corruption.");
   });
   Run(report,"Protected folders: missing previously loaded settings remain fail closed",delegate {
    var c=context();string folder=Path.Combine(c.UserRoot,"private");Directory.CreateDirectory(folder);string reason;Assert(c.ProtectedFolders.TryAdd(folder,out reason),reason);File.Delete(c.ProtectedFolders.SettingsFile);c.ProtectedFolders.Refresh();Assert(!c.ProtectedFolders.CanModify&&c.ProtectedFolders.Folders.Count==1,"Deleting known settings silently cleared protections.");Assert(c.ProtectedFolders.IsProtected(Path.Combine(c.UserRoot,"otherwise-safe.dat"),false,out reason),"Missing known settings enabled operations.");Assert(!c.ProtectedFolders.TryAdd(c.Documents,out reason)&&!File.Exists(c.ProtectedFolders.SettingsFile),"Missing known settings were silently recreated.");Assert(!new ProtectedFolderStore(Path.GetDirectoryName(c.ProtectedFolders.SettingsFile)).CanModify,"Restart treated missing prior settings as a new store.");
   });
   Run(report,"Protected folders: settings directory cannot be a regular file",delegate {
    var c=context();string directoryFile=Put(c.UserRoot,"settings-location","UNKNOWN USER FILE");var store=new ProtectedFolderStore(directoryFile);string reason;Assert(!store.CanModify&&!store.TryAdd(c.Documents,out reason),"File-shaped settings directory enabled operations.");Assert(File.ReadAllText(directoryFile)=="UNKNOWN USER FILE","Settings directory collision overwrote user file.");
   });
   Run(report,"Protected folders: junction settings and folder inputs are refused",delegate {
    var c=context();string target=Path.Combine(c.UserRoot,"target"),link=Path.Combine(c.UserRoot,"linked");Directory.CreateDirectory(target);Junction(link,target);string reason;Assert(!c.ProtectedFolders.TryAdd(link,out reason),"Junction folder was accepted.");Assert(!c.ProtectedFolders.TryAdd(Path.Combine(link,"missing"),out reason),"Missing child under junction accepted.");
    string settingsLink=Path.Combine(c.UserRoot,"settings-link");Junction(settingsLink,target);var store=new ProtectedFolderStore(settingsLink);Assert(!store.CanModify&&!String.IsNullOrEmpty(store.LoadError),"Linked settings directory enabled operations.");Assert(!store.TryAdd(c.Documents,out reason)&&!File.Exists(Path.Combine(target,"protected-folders.json")),"Settings wrote through junction.");
   });
   Run(report,"Protected folders: removing custom entry never weakens built-in protection",delegate {
    var c=context();string file=Put(c.Documents,"personal.dat","PERSONAL GENERATED FIXTURE"),reason;Assert(c.ProtectedFolders.TryAdd(c.Documents,out reason),reason);Assert(c.ProtectedFolders.TryRemove(c.Documents,out reason),reason);Assert(new SafetyPolicy(c).IsProtectedFile(file,out reason),"Custom removal removed built-in Documents protection.");Assert(!c.ProtectedFolders.TryRemove(c.Desktop,out reason),"Removal claimed to remove a built-in entry.");
   });
   Run(report,"Protected folders: tilde and short-path aliases fail closed",delegate {
    var c=context();string longFolder=Path.Combine(c.UserRoot,"Long Protected Folder");Directory.CreateDirectory(longFolder);string reason;Assert(c.ProtectedFolders.TryAdd(longFolder,out reason),reason);
    string alias=Path.Combine(c.UserRoot,"LONGPR~1","cache.dat"),canonical;Assert(!ProtectedFolderStore.CanonicalLocalPath("C:\\SHORT~1\\cache.dat",false,out canonical,out reason),"8.3-looking paths are not rejected deterministically.");Assert(c.ProtectedFolders.IsProtected(alias,false,out reason),"An 8.3-looking candidate path bypassed protection.");Assert(new SafetyPolicy(c).IsProtectedOperationPath(alias,false,out reason),"Operation policy accepted short-path alias.");
    string tildeFolder=Path.Combine(c.UserRoot,"legitimate~folder");Directory.CreateDirectory(tildeFolder);Assert(!c.ProtectedFolders.TryAdd(tildeFolder,out reason),"Ambiguous tilde-bearing folder was accepted.");Assert(!new ProtectedFolderStore(Path.Combine(tildeFolder,"settings")).CanModify,"Tilde-bearing settings path was accepted.");
    string invalid=Format.Json(new Dictionary<string,object>{{"Owner","ClearGuard.ProtectedFolders"},{"Schema",1},{"Folders",new[]{tildeFolder}}});File.WriteAllText(c.ProtectedFolders.SettingsFile,invalid);c.ProtectedFolders.Refresh();Assert(!c.ProtectedFolders.CanModify&&c.ProtectedFolders.Folders.Count==1,"Stored ambiguous alias replaced known protections.");
   });
   return report;
  }
  private static void Run(ProtectedFoldersTestReport report,string name,Action test){try{test();report.Passed++;report.Tests.Add(new ProtectedFoldersTestEntry{Name=name,Status="PASS"});}catch(Exception e){report.Failed++;report.Tests.Add(new ProtectedFoldersTestEntry{Name=name,Status="FAIL",Detail=e.GetType().Name+": "+e.Message});}}
  private static string Prepare(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));return path;}
  private static string Put(string root,string leaf,string content){string path=Prepare(Path.Combine(root,leaf));File.WriteAllText(path,content,new UTF8Encoding(false));return path;}
  private static void Zip(string path){using(var stream=new FileStream(Prepare(path),FileMode.CreateNew))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("project/main.cs").Open()))writer.Write("class GeneratedProtectedFolderFixture {}");}
  private static void Junction(string link,string target){var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};using(var process=Process.Start(start)){string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();Assert(process.WaitForExit(5000)&&process.ExitCode==0,"Generated junction creation failed: "+output+error);}}
  private static bool Same(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd('\\'),Path.GetFullPath(b).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 }
}
