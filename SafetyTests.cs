using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ClearGuard {
 public static class SafetyTests {
  private sealed class TestEntry { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
  private sealed class TestReport { public DateTime StartedUtc {get;set;} public DateTime CompletedUtc {get;set;} public string FixtureRoot {get;set;} public bool RealUserFilesModified {get;set;} public int Passed {get;set;} public int Failed {get;set;} public int Skipped {get;set;} public List<TestEntry> Tests {get;set;} public List<string> Gaps {get;set;} public List<string> RecoveredPriorFixtureReceipts {get;set;} }
  private sealed class SkipTest : Exception { public SkipTest(string text):base(text){} }
  private static string fixture;
  private static TestReport report;
  private static int sequence;
  private static string processProbe;
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  [return: MarshalAs(UnmanagedType.I1)]
  private static extern bool CreateSymbolicLink(string link,string target,int flags);
  public static int Run(string fixtureRoot,string reportPath) {
   string allowed=System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests"));
   string requested=System.IO.Path.GetFullPath(fixtureRoot);
   if(!Within(requested,allowed)) throw new InvalidOperationException("Self-test fixture must be inside the current workspace work\\ClearGuardTests directory.");
   fixture=System.IO.Path.Combine(requested,"ClearGuard-fixture-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(fixture);
   sequence=0;
   processProbe=null;
   report=new TestReport{StartedUtc=DateTime.UtcNow,FixtureRoot=fixture,RealUserFilesModified=false,Tests=new List<TestEntry>(),Gaps=new List<string>(),RecoveredPriorFixtureReceipts=new List<string>()};
   RecoverPriorFixtureReceipts(requested);
   Test("Path boundary rejects prefix siblings and dot-dot escape", delegate {
    string parent=System.IO.Path.Combine(fixture,"scope");
    Assert(SafetyPolicy.IsWithin(System.IO.Path.Combine(parent,"child"),parent),"Descendant was not recognized.");
    Assert(!SafetyPolicy.IsWithin(parent+"-sibling",parent),"A path-prefix sibling escaped the boundary.");
    Assert(!SafetyPolicy.IsWithin(System.IO.Path.Combine(parent,"..","outside"),parent),"Dot-dot escape was accepted.");
   });
   Test("Image and video extension preservation", delegate {
    string[] extensions={"jpg","jpeg","png","webp","heic","heif","gif","bmp","tif","tiff","svg","avif","ico","psd","dng","raw","mp4","mkv","mov","avi","m4v","3gp","webm","wmv","mpeg","mpg","m2ts","mts","flv","ogv"};
    ScanContext ctx=NewContext("media"); string reason;
    foreach(string ext in extensions) {
     string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\sample."+ext,new byte[]{1,2,3,4});
     Assert(SafetyPolicy.IsProtectedContent(file,out reason),"Unprotected media extension: "+ext);
    }
   });
   Test("PNG header protects a disguised .dat file", delegate {
    ScanContext ctx=NewContext("signature"); string reason;
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\opaque.dat",new byte[]{137,80,78,71,13,10,26,10,0,0,0,0});
    Assert(SafetyPolicy.IsProtectedContent(file,out reason),"PNG signature was not protected.");
   });
   Test("Media inside an opaque cache blob is protected across buffer boundaries", delegate {
    ScanContext ctx=NewContext("embedded-media");string reason;
    byte[] content=Enumerable.Repeat((byte)65,80000).ToArray();
    byte[] png={137,80,78,71,13,10,26,10};Buffer.BlockCopy(png,0,content,65533,png.Length);
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\opaque-with-embedded-media.dat",content);
    Assert(SafetyPolicy.IsProtectedContent(file,out reason),"Embedded PNG across a read-buffer boundary was not protected.");
    string archive=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","opaque-media-entry.zip");
    MakeZip(archive,"cache/opaque-blob.dat",content);
    Assert(SafetyPolicy.IsProtectedContent(archive,out reason),"Embedded media in an opaque ZIP entry beyond the entry header was not protected.");
   });
   Test("ZIP with media and nested ZIP with media are protected", delegate {
    ScanContext ctx=NewContext("archives"); string reason;
    string media=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","media.zip");
    MakeZip(media,"assets/photo.png",new byte[]{137,80,78,71,13,10,26,10});
    Assert(SafetyPolicy.IsProtectedContent(media,out reason),"Archive media entry was not protected.");
    string nested=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","nested.zip");
    MakeZip(nested,"inside.zip",File.ReadAllBytes(media));
    Assert(SafetyPolicy.IsProtectedContent(nested,out reason),"Nested archive media was not protected.");
   });
   Test("Unknown nested compressed archive is fail-closed", delegate {
    ScanContext ctx=NewContext("unknown-nested-compression"); string reason;
    string archive=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","nested-unknown.zip");
    MakeZip(archive,"unknown.7z",new byte[]{0x37,0x7a,0xbc,0xaf,0x27,0x1c,1,2,3});
    Assert(SafetyPolicy.IsProtectedContent(archive,out reason),"Unsupported nested compression was treated as proven safe.");
    foreach(string ext in new[]{"bz2","xz","zst"}) {
     string unknown=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\unknown."+ext,new byte[]{1,2,3});
     Assert(SafetyPolicy.IsProtectedContent(unknown,out reason),"Unsupported compression was not protected: "+ext);
    }
   });
   Test("Sensitive .env entry inside a cache archive is preserved", delegate {
    ScanContext ctx=NewContext("archive-secret"); string reason;
    string archive=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","settings.zip");
    MakeZip(archive,"project/.env.production",Encoding.UTF8.GetBytes("SECRET=fixture-only-value"));
    Assert(SafetyPolicy.IsProtectedContent(archive,out reason),"Sensitive environment archive entry was not protected.");
   });
   Test("Source code, APK, AAB, and key material are preserved", delegate {
    ScanContext ctx=NewContext("source-protection"); string reason;
    string[] extensions={"cs","java","kt","js","ts","py","go","apk","aab","jks","keystore","pem"};
    foreach(string ext in extensions) {
     string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\unique."+ext,Encoding.UTF8.GetBytes("unique user-owned content"));
     Assert(SafetyPolicy.IsProtectedContent(file,out reason),"Unprotected source/artifact/key extension: "+ext);
    }
   });
   Test("Desktop, Documents, Downloads, Codex and AVD are never cache deletion targets", delegate {
    ScanContext ctx=NewContext("protected-paths"); SafetyPolicy policy=new SafetyPolicy(ctx); string reason;
    string[] paths={"Desktop\\personal.txt","Documents\\personal.txt","Downloads\\installer.exe",".codex\\cache\\cached.dat",".codex\\.tmp\\cached.tmp",".codex\\sessions\\session.jsonl",".android\\avd\\Phone.avd\\snapshot.bin","AppData\\Local\\Android\\Sdk\\system-images\\image.dat"};
    foreach(string relative in paths) {
     string file=Put(ctx,relative,Encoding.UTF8.GetBytes("protected"));
     Assert(policy.IsProtectedFile(file,out reason),"Protected path was accepted: "+relative);
    }
   });
   Test("Unknown cache path and outside-user path are refused", delegate {
    ScanContext ctx=NewContext("unknown");
    string file=Put(ctx,"not-approved-cache\\safe.tmp",Encoding.UTF8.GetBytes("fixture-only"));
    ScanRow row=FabricateRow(System.IO.Path.GetDirectoryName(file),file);
    OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
    Assert(File.Exists(file),"Unknown path was deleted.");
    Assert(op.ProcessedBytes==0,"Unknown path counted as deleted.");
    string outside=System.IO.Path.Combine(fixture,"outside-user","safe.tmp"); PutAbsolute(outside,Encoding.UTF8.GetBytes("fixture-only outside ctx"));
    row=FabricateRow(System.IO.Path.GetDirectoryName(outside),outside);
    op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
    Assert(File.Exists(outside)&&op.ProcessedBytes==0,"Outside-user path was deleted.");
   });
   Test("SHA revalidation detects changed content with identical length and timestamp", delegate {
    ScanContext ctx=NewContext("changed-manifest");
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\safe.tmp",Encoding.UTF8.GetBytes("AAAAAAAAAAAAAAAA"));
    ScanRow row=ScannedRowFor(ctx,file); row.Selected=true;
    DateTime originalTime=File.GetLastWriteTimeUtc(file);
    File.WriteAllText(file,"BBBBBBBBBBBBBBBB",new UTF8Encoding(false));
    File.SetLastWriteTimeUtc(file,originalTime);
    OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
    Assert(File.Exists(file),"Changed scan manifest file was deleted.");
    Assert(op.ProcessedBytes==0,"Changed scan manifest counted as deleted.");
   });
   Test("Exclusive lock blocks deletion and leaves file intact", delegate {
    ScanContext ctx=NewContext("locked-file");
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\safe.tmp",Encoding.UTF8.GetBytes("locked fixture"));
    ScanRow row=ScannedRowFor(ctx,file); row.Selected=true;
    using(FileStream locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
     OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
     Assert(op.ProcessedBytes==0,"Locked file counted as deleted.");
    }
    Assert(File.Exists(file),"Locked file was deleted.");
   });
   Test("Media created after scan is retained on revalidation", delegate {
    ScanContext ctx=NewContext("media-after-scan");
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\opaque.dat",Encoding.UTF8.GetBytes("ordinary fixture data"));
    ScanRow row=ScannedRowFor(ctx,file); row.Selected=true;
    File.WriteAllBytes(file,new byte[]{137,80,78,71,13,10,26,10});
    OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
    Assert(File.Exists(file)&&op.ProcessedBytes==0,"New protected media was deleted.");
   });
   Test("Isolated approved safe-cache fixture deletion reports exact logical bytes", delegate {
    ScanContext ctx=NewContext("approved-delete");
    byte[] content=Encoding.UTF8.GetBytes("This is generated QA cache data, not a user file.");
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\generated-test.tmp",content);
    ScanRow row=ScannedRowFor(ctx,file); row.Selected=true;
    Assert(row.Eligible,"Approved fixture row was not eligible: "+row.BlockReason);
    OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,CancellationToken.None,null);
    Assert(!File.Exists(file),"Approved generated fixture was not deleted.");
    Assert(op.ProcessedBytes==content.Length,"Logical byte accounting was inaccurate.");
   });
   Test("Reparse ancestor is detected without traversing target", delegate {
    ScanContext ctx=NewContext("reparse");
    string target=System.IO.Path.Combine(fixture,"reparse-target"); Directory.CreateDirectory(target);
    string file=System.IO.Path.Combine(target,"unique.tmp"); PutAbsolute(file,Encoding.UTF8.GetBytes("preserve target"));
    string link=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(link));
    if(!CreateSymbolicLink(link,target,3)) {
     int error=Marshal.GetLastWin32Error();
     if(!CreateFixtureJunction(link,target))throw new SkipTest("Windows did not grant unprivileged symbolic-link creation (error "+error+") or isolated junction creation.");
    }
    Assert(SafetyPolicy.HasReparseAncestor(System.IO.Path.Combine(link,"unique.tmp")),"Reparse ancestor was not detected.");
    ScanResult result=CacheScanner.Scan(ctx,CancellationToken.None,null);
    Assert(!result.Rows.Any(delegate(ScanRow r){return r.Eligible&&r.Files.Any(delegate(FileRecord f){return Within(f.Path,link);});}),"Reparse-linked files were eligible.");
    Assert(File.Exists(file),"Link target was modified.");
   });
   Test("Process guard never terminates the running test process", delegate {
    int pid=Process.GetCurrentProcess().Id; ScanContext ctx=NewContext("process"); string reason;
    ProcessGuard.Check("Android Studio",ctx,out reason);
    ProcessGuard.Check("Gradle",ctx,out reason);
    Assert(!Process.GetProcessById(pid).HasExited,"Process guard terminated its caller.");
   });
   Test("Actual process guard blocks an owned hidden Chrome fixture without stopping it",delegate{AssertRunningGuard("Chrome","chrome","chrome-guard");});
   Test("Actual process guard blocks an owned hidden Android Studio fixture without stopping it",delegate{AssertRunningGuard("Android Studio","studio64","studio-guard");});
   Test("Actual process guard blocks an owned hidden Gradle Java fixture without stopping it",delegate{AssertRunningGuard("Gradle","java","gradle-guard");});
   Test("Actual process guard blocks an owned hidden BlueStacks alias fixture without stopping it",delegate{AssertRunningGuard("BlueStacks","hd-player","bluestacks-guard");});
   Test("Actual process guard blocks an owned hidden updater fixture without stopping it",delegate{AssertRunningGuard("Chatbox","chatbox","chatbox-guard");});
   Test("Exact source archive duplicates keep newest and remain unselected", delegate {
    ScanContext ctx=NewContext("duplicate-sources");
    string newer=System.IO.Path.Combine(ctx.Desktop,"Example-v2.zip"); MakeZip(newer,"src/Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    string older=System.IO.Path.Combine(ctx.Desktop,"Example-v1.zip"); File.Copy(newer,older);
    ScanResult result=DesktopScanner.Scan(ctx,CancellationToken.None,null);
    ScanRow oldRow=result.Rows.FirstOrDefault(delegate(ScanRow r){return Same(r.Path,older)&&r.Eligible;});
    Assert(oldRow!=null,"Exact duplicate source archive was not identified.");
    Assert(Same(oldRow.Keeper,newer),"Newer source archive was not the keeper.");
    Assert(!oldRow.Selected,"Source duplicate was automatically selected.");
   });
   Test("Changed source keeper blocks duplicate recycling", delegate {
    ScanContext ctx=NewContext("keeper-changed");
    string keeper=System.IO.Path.Combine(ctx.Desktop,"Example-v2.zip"); MakeZip(keeper,"src/Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    string old=System.IO.Path.Combine(ctx.Desktop,"Example-v1.zip"); File.Copy(keeper,old);
    ScanResult result=DesktopScanner.Scan(ctx,CancellationToken.None,null);
    ScanRow row=result.Rows.FirstOrDefault(delegate(ScanRow r){return Same(r.Path,old)&&r.Eligible;});
    Assert(row!=null,"Fixture source duplicate was not identified."); row.Selected=true;
    File.AppendAllText(keeper,"changed keeper",new UTF8Encoding(false));
    OperationReport op=DesktopCleanup.Run(ctx,new List<ScanRow>{row},CancellationToken.None,null);
    Assert(File.Exists(old)&&File.Exists(keeper)&&op.ProcessedBytes==0,"Changed keeper allowed source deletion.");
   });
   Test("SYMEXVPN and proxyx copies are not source-cleanup candidates", delegate {
    ScanContext ctx=NewContext("protected-projects");
    string[] names={"SYMEXVPN","proxyx"};
    foreach(string name in names) {
     string current=System.IO.Path.Combine(ctx.Desktop,name+".zip"); MakeZip(current,"src/Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
     File.Copy(current,System.IO.Path.Combine(ctx.Desktop,name+"-v1.zip"));
    }
    ScanResult result=DesktopScanner.Scan(ctx,CancellationToken.None,null);
    Assert(!result.Rows.Any(delegate(ScanRow r){return r.Eligible;}),"Protected original project archive became eligible.");
   });
   Test("Organizer destination collision blocks move without overwrite", delegate {
    ScanContext ctx=NewContext("organizer-collision");
    string source=Put(ctx,"Desktop\\notes.txt",Encoding.UTF8.GetBytes("source notes"));
    string destination=Organizer.TargetFor(ctx,source); PutAbsolute(destination,Encoding.UTF8.GetBytes("existing destination"));
    ScanResult preview=Organizer.Preview(ctx,CancellationToken.None,null);
    ScanRow row=preview.Rows.FirstOrDefault(delegate(ScanRow r){return Same(r.Path,source);});
    Assert(row==null||!row.Eligible,"Organizer collision remained eligible.");
    Assert(File.ReadAllText(destination)=="existing destination"&&File.Exists(source),"Collision overwrote data.");
   });
   Test("Organizer move and restore conflict preserve both copies", delegate {
    ScanContext ctx=NewContext("organizer-restore");
    string source=Put(ctx,"Desktop\\notes.txt",Encoding.UTF8.GetBytes("original notes"));
    ScanResult preview=Organizer.Preview(ctx,CancellationToken.None,null);
    ScanRow row=preview.Rows.FirstOrDefault(delegate(ScanRow r){return Same(r.Path,source)&&r.Eligible;});
    Assert(row!=null,"Plain-text organizer fixture was not eligible."); row.Selected=true;
    OperationReport moved=Organizer.Apply(ctx,new List<ScanRow>{row},CancellationToken.None,null);
    Assert(!File.Exists(source)&&File.Exists(row.Keeper),"Organizer did not move its generated fixture.");
    PutAbsolute(source,Encoding.UTF8.GetBytes("new file at original path"));
    OperationReport restored=Organizer.Restore(ctx,moved,CancellationToken.None,null);
    Assert(File.ReadAllText(source)=="new file at original path", "Restore overwrote conflicting original path.");
    Assert(File.Exists(row.Keeper),"Restore conflict removed organized copy.");
    Assert(restored.ProcessedBytes==0,"Restore conflict counted as restored.");
   });
   Test("Organizer restore refuses a forged journal outside the actual Desktop", delegate {
    ScanContext ctx=NewContext("forged-organizer-journal");
    string arbitraryParent=System.IO.Path.Combine(ctx.UserRoot,"not-desktop");
    string target=System.IO.Path.Combine(arbitraryParent,"notes.txt");
    string source=System.IO.Path.Combine(arbitraryParent,"دسکتاپ مرتب","اسناد","notes.txt");
    byte[] data=Encoding.UTF8.GetBytes("fixture-only forged receipt data");PutAbsolute(source,data);
    OperationReport forged=new OperationReport{Kind="organize-move",Root=ctx.Desktop,Entries=new List<OperationEntry>{new OperationEntry{OriginalPath=target,DestinationPath=source,Status="moved",Bytes=data.Length,Hash=SafetyPolicy.HashFile(source)}}};
    OperationReport result=Organizer.Restore(ctx,forged,CancellationToken.None,null);
    Assert(File.Exists(source)&&!File.Exists(target)&&result.ProcessedBytes==0,"Forged journal escaped actual Desktop restore boundary.");
   });
   Test("Actual Recycle Bin receipt and SHA-verified restore of generated fixture", delegate {
    ScanContext ctx=NewContext("recycle-roundtrip");
    byte[] content=Encoding.UTF8.GetBytes("Generated ClearGuard QA recycle roundtrip data.");
    string file=Put(ctx,"generated-roundtrip.tmp",content);
    OperationEntry receipt=RecycleService.Recycle(file);
    Assert(receipt.Status=="Recycled","Recycle receipt was not verified: "+receipt.Status+" / "+receipt.Detail);
    Assert(!File.Exists(file)&&File.Exists(receipt.DestinationPath)&&File.Exists(receipt.RecycleMetadata),"Verified bin payload/metadata missing.");
    Assert(receipt.Bytes==content.Length&&SafetyPolicy.HashFile(receipt.DestinationPath)==receipt.Hash,"Recycle byte count/hash mismatch.");
    OperationEntry restored=RecycleService.Restore(receipt);
    Assert(restored.Status=="Restored"&&File.Exists(file),"Generated recycled fixture was not restored.");
    Assert(File.ReadAllBytes(file).SequenceEqual(content),"Restored generated content changed.");
    Assert(!File.Exists(receipt.DestinationPath)&&!File.Exists(receipt.RecycleMetadata),"Own test bin receipt was not consumed after successful restoration.");
   });
   Test("Actual Recycle Bin restore conflict never overwrites and remains recoverable", delegate {
    ScanContext ctx=NewContext("recycle-conflict");
    byte[] original=Encoding.UTF8.GetBytes("Generated old fixture data.");
    byte[] conflict=Encoding.UTF8.GetBytes("Generated newer conflicting fixture data.");
    string file=Put(ctx,"generated-conflict.tmp",original);
    OperationEntry receipt=RecycleService.Recycle(file);
    Assert(receipt.Status=="Recycled","Conflict fixture recycle receipt was not verified.");
    PutAbsolute(file,conflict);
    bool refused=false;try{RecycleService.Restore(receipt);}catch(IOException){refused=true;}
    Assert(refused&&File.ReadAllBytes(file).SequenceEqual(conflict),"Restore conflict overwrote generated newer file.");
    Assert(File.Exists(receipt.DestinationPath)&&File.Exists(receipt.RecycleMetadata),"Restore conflict damaged recoverability.");
    File.Move(file,file+".preserved-conflict");
    OperationEntry restored=RecycleService.Restore(receipt);
    Assert(restored.Status=="Restored"&&File.ReadAllBytes(file).SequenceEqual(original),"Generated original fixture could not be restored after resolving conflict.");
    Assert(File.ReadAllBytes(file+".preserved-conflict").SequenceEqual(conflict),"Resolved conflicting fixture was not preserved.");
   });
   Test("Actual Recycle Bin folder restore preserves nested files and empty directories", delegate {
    ScanContext ctx=NewContext("recycle-folder");
    string file=Put(ctx,"generated-folder\\nested\\cache.tmp",Encoding.UTF8.GetBytes("generated folder cache"));
    string folder=System.IO.Path.Combine(ctx.UserRoot,"generated-folder");
    Directory.CreateDirectory(System.IO.Path.Combine(folder,"empty-directory"));
    string before=SafetyPolicy.HashTree(folder);
    OperationEntry receipt=RecycleService.Recycle(folder);
    Assert(receipt.Status=="Recycled"&&!Directory.Exists(folder),"Generated folder was not verifiably recycled.");
    OperationEntry restored=RecycleService.Restore(receipt);
    Assert(restored.Status=="Restored"&&File.Exists(file)&&Directory.Exists(System.IO.Path.Combine(folder,"empty-directory")),"Generated folder structure was not restored.");
    Assert(SafetyPolicy.HashTree(folder)==before,"Generated folder content changed after restore.");
   });
   Test("Cancellation before cleanup preserves generated candidate", delegate {
    ScanContext ctx=NewContext("cancelled-cleanup");
    string file=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\safe.tmp",Encoding.UTF8.GetBytes("cancelled fixture"));
    ScanRow row=ScannedRowFor(ctx,file);row.Selected=true;
    using(CancellationTokenSource cancelled=new CancellationTokenSource()) {
     cancelled.Cancel();
     try {OperationReport result=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,cancelled.Token,null);Assert(result.ProcessedBytes==0,"Cancelled candidate counted as deleted.");}catch(OperationCanceledException){}
    }
    Assert(File.Exists(file),"Cancelled-before-start candidate was deleted.");
   });
   Test("Deep organized Desktop container still discovers protected originals", delegate {
    ScanContext ctx=NewContext("deep-desktop");
    string manifest=Put(ctx,"Desktop\\دسکتاپ مرتب\\پروژه‌ها\\Container\\SYMEXVPN\\app\\build.gradle",Encoding.UTF8.GetBytes("versionName = \"3.2.6\""));
    ScanResult result=DesktopScanner.Scan(ctx,CancellationToken.None,null);
    Assert(result.Rows.Any(delegate(ScanRow r){return r.Path.IndexOf("SYMEXVPN",StringComparison.OrdinalIgnoreCase)>=0&&!r.Eligible;}),"Organized original project disappeared from scan.");
    Assert(File.Exists(manifest),"Read-only source discovery modified manifest.");
   });
   Test("Approved exact source ZIP recycling and recovery preserve keeper", delegate {
    ScanContext ctx=NewContext("source-recycle-success");
    string keeper=System.IO.Path.Combine(ctx.Desktop,"Example-v2.zip");MakeZip(keeper,"src/Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    string old=System.IO.Path.Combine(ctx.Desktop,"Example-v1.zip");File.Copy(keeper,old);string before=SafetyPolicy.HashFile(keeper);
    ScanResult result=DesktopScanner.Scan(ctx,CancellationToken.None,null);ScanRow row=result.Rows.First(delegate(ScanRow r){return Same(r.Path,old)&&r.Eligible;});row.Selected=true;
    OperationReport recycled=DesktopCleanup.Run(ctx,new List<ScanRow>{row},CancellationToken.None,null);
    Assert(!File.Exists(old)&&SafetyPolicy.HashFile(keeper)==before,"Source recycle failed or changed keeper.");
    OperationEntry receipt=recycled.Entries.First(delegate(OperationEntry e){return e.Status=="Recycled";});RecycleService.Restore(receipt);
    Assert(File.Exists(old)&&SafetyPolicy.HashFile(old)==before&&SafetyPolicy.HashFile(keeper)==before,"Source duplicate recovery failed.");
   });
   Test("Large-file inventory and drive duplicates are read-only and SHA-proven", delegate {
    ScanContext ctx=NewContext("inventory-duplicates");byte[] data=new byte[1024*1024];for(int i=0;i<data.Length;i++)data[i]=(byte)(i%251);
    string one=Put(ctx,"Downloads\\one.bin",data);string two=Put(ctx,"Downloads\\two.bin",data);data[17]=255;string changed=Put(ctx,"Downloads\\different.bin",data);
    ScanResult inventory=InventoryScanner.Scan(ctx.UserRoot,CancellationToken.None,null);Assert(inventory.Rows.Any(delegate(ScanRow r){return r.Category=="LargeFile"&&Same(r.Path,one);}),"Largest-file detail was omitted.");
    ScanResult duplicates=DuplicateScanner.Scan(ctx.UserRoot,CancellationToken.None,null);Assert(duplicates.Rows.Any(delegate(ScanRow r){return Same(r.Path,one)||Same(r.Path,two);}),"SHA-identical files not reported.");
    Assert(!duplicates.Rows.Any(delegate(ScanRow r){return Same(r.Path,changed)||r.Eligible||r.Selected;}),"Different same-size file or read-only deletion guard failed.");
    Assert(File.Exists(one)&&File.Exists(two)&&File.Exists(changed),"Read-only inventory changed generated files.");
   });
   Test("Report exports escape HTML and neutralize spreadsheet formulas", delegate {
    ScanContext ctx=NewContext("report-export");var scan=new ScanResult{Kind="Inventory",CompletedUtc=DateTime.UtcNow};scan.Rows.Add(new ScanRow{Name="=2+2",Path=ctx.UserRoot,Effect="<script>unsafe()</script>",Bytes=12,Eligible=false});
    string html=System.IO.Path.Combine(ctx.ReportDirectory,"export.html"),csv=System.IO.Path.Combine(ctx.ReportDirectory,"export.csv");Directory.CreateDirectory(ctx.ReportDirectory);ReportExport.Scan(html,scan);ReportExport.Csv(csv,scan);
    Assert(!File.ReadAllText(html).Contains("<script>unsafe()"),"HTML export contains executable markup.");Assert(File.ReadAllText(csv).Contains("\"'=2+2\""),"CSV export did not escape formula-prefixed name.");
   });
   Test("Android app version is not confused with a dependency package version", delegate {
    ScanContext ctx=NewContext("version-priority");Put(ctx,"Desktop\\Example\\package.json",Encoding.UTF8.GetBytes("{\"version\":\"26.7.11\"}"));Put(ctx,"Desktop\\Example\\app\\build.gradle.kts",Encoding.UTF8.GetBytes("versionName = \"3.2.6\""));
    ScanResult scan=DesktopScanner.Scan(ctx,CancellationToken.None,null);ScanRow project=scan.Rows.First(delegate(ScanRow r){return Same(r.Path,System.IO.Path.Combine(ctx.Desktop,"Example"));});Assert(project.Version=="3.2.6","Dependency version replaced Android application version.");
   });
   Test("ZIP trailing media outside entries is preserved fail-closed", delegate {
    ScanContext ctx=NewContext("zip-trailing");string path=System.IO.Path.Combine(ctx.LocalAppData,"node-gyp","Cache","trailing.zip");MakeZip(path,"ordinary.dat",Encoding.UTF8.GetBytes("ordinary cache"));
    using(FileStream stream=new FileStream(path,FileMode.Append,FileAccess.Write)){byte[] media={137,80,78,71,13,10,26,10};stream.Write(media,0,media.Length);}
    string reason;Assert(SafetyPolicy.IsProtectedContent(path,out reason),"ZIP trailing media was ignored and became eligible.");
   });
   Test("Unknown local Maven artifact remains untouched", delegate {
    ScanContext ctx=NewContext("maven-local");
    string artifact=Put(ctx,".m2\\repository\\local\\private\\1.0\\private-1.0.jar",Encoding.UTF8.GetBytes("local artifact not downloadable"));
    ScanResult result=CacheScanner.Scan(ctx,CancellationToken.None,null);
    Assert(!result.Rows.Any(delegate(ScanRow r){return r.Eligible&&r.Files.Any(delegate(FileRecord f){return Same(f.Path,artifact);});}),"Unknown local Maven artifact became eligible.");
    Assert(File.Exists(artifact),"Read-only scan modified local Maven artifact.");
   });
   Test("Source folder keeper handles deny concurrent writes and renames", delegate {
    ScanContext ctx=NewContext("keeper-folder-lock");
    string kept=Put(ctx,"Desktop\\Example-v2\\src\\Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    string duplicate=Put(ctx,"Desktop\\Example-v1\\src\\Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    foreach(string name in new[]{"Example-v1","Example-v2"})Put(ctx,"Desktop\\"+name+"\\package.json",Encoding.UTF8.GetBytes("{\"name\":\"fixture-example\",\"version\":\"1.0.0\"}"));
    ScanRow row=DesktopScanner.Scan(ctx,CancellationToken.None,null).Rows.First(r=>!String.IsNullOrEmpty(r.Keeper));
    var method=typeof(DesktopCleanup).GetMethod("OpenKeeperLocks",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
    Assert(method!=null,"Folder keeper is not held against writers or deletion during duplicate recycling.");
    var locks=(List<FileStream>)method.Invoke(null,new object[]{row,CancellationToken.None});
    try {
     bool writeDenied=false,renameDenied=false;
     try{File.AppendAllText(kept,"writer must be denied");}catch(IOException){writeDenied=true;}
     try{File.Move(kept,kept+".renamed");}catch(IOException){renameDenied=true;}
     Assert(writeDenied&&renameDenied,"Keeper can change or disappear while old source is being recycled.");
     Assert(File.ReadAllText(kept)=="class Example {}"&&File.Exists(duplicate),"Keeper lock modified either source.");
    } finally {foreach(FileStream locked in locks)locked.Dispose();}
   });
   Test("Organizer move handle denies concurrent writers and still permits rename", delegate {
    ScanContext ctx=NewContext("organizer-move-lock");string source=Put(ctx,"Desktop\\notes.txt",Encoding.UTF8.GetBytes("generated original notes"));
    ScanRow row=Organizer.Preview(ctx,CancellationToken.None,null).Rows.First(r=>Same(r.Path,source)&&r.Eligible);
    var method=typeof(Organizer).GetMethod("HoldUnchangedMoveSource",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
    Assert(method!=null,"Organizer does not hold verified content against concurrent writers.");
    using(var hold=(FileStream)method.Invoke(null,new object[]{row})) {
     bool denied=false;try{File.AppendAllText(source,"must be denied");}catch(IOException){denied=true;}
     Assert(denied,"Concurrent write was allowed during organizer move.");
     Directory.CreateDirectory(System.IO.Path.GetDirectoryName(row.Keeper));File.Move(source,row.Keeper);
     Assert(File.Exists(row.Keeper)&&SafetyPolicy.HashFile(row.Keeper)==row.Hash,"Read-hold prevented rename or changed fixture bytes.");
    }
   });
   Test("Exact source folders remain report-only and forged deletion is refused", delegate {
    ScanContext ctx=NewContext("source-folder-roundtrip");
    Put(ctx,"Desktop\\Example-v2\\src\\Main.cs",Encoding.UTF8.GetBytes("class Example {}"));Put(ctx,"Desktop\\Example-v1\\src\\Main.cs",Encoding.UTF8.GetBytes("class Example {}"));
    foreach(string name in new[]{"Example-v1","Example-v2"})Put(ctx,"Desktop\\"+name+"\\package.json",Encoding.UTF8.GetBytes("{\"name\":\"fixture-example\",\"version\":\"1.0.0\"}"));
    string keeper=System.IO.Path.Combine(ctx.Desktop,"Example-v2"),old=System.IO.Path.Combine(ctx.Desktop,"Example-v1");Directory.CreateDirectory(System.IO.Path.Combine(keeper,"empty"));Directory.CreateDirectory(System.IO.Path.Combine(old,"empty"));
    string before=SafetyPolicy.HashTree(keeper);ScanRow row=DesktopScanner.Scan(ctx,CancellationToken.None,null).Rows.First(r=>Same(r.Path,old)&&!String.IsNullOrEmpty(r.Keeper));Assert(!row.Eligible&&row.SafeBytes==0,"Folder source exposed an unsafe recycle action.");row.Selected=true;row.Eligible=true;
    OperationReport op=DesktopCleanup.Run(ctx,new List<ScanRow>{row},CancellationToken.None,null);
    Assert(Directory.Exists(old)&&Directory.Exists(System.IO.Path.Combine(old,"empty"))&&SafetyPolicy.HashTree(old)==before&&SafetyPolicy.HashTree(keeper)==before&&op.ProcessedBytes==0,"Forged folder eligibility bypassed conservative no-mutation policy.");Assert(op.Entries.All(e=>String.Equals(e.Status,"skipped",StringComparison.OrdinalIgnoreCase)),"Refused folder operation has an inaccurate pending or completed receipt.");
   });
   Test("Different source branches and duplicates containing media or secrets are preserved", delegate {
    ScanContext ctx=NewContext("source-unique-protected");Put(ctx,"Desktop\\Branch-v1\\src\\Main.cs",Encoding.UTF8.GetBytes("class Old {}"));Put(ctx,"Desktop\\Branch-v2\\src\\Main.cs",Encoding.UTF8.GetBytes("class New {}"));
    foreach(string name in new[]{"Branch-v1","Branch-v2","Media-v1","Media-v2","Private-v1","Private-v2"})Put(ctx,"Desktop\\"+name+"\\package.json",Encoding.UTF8.GetBytes("{\"name\":\"fixture-example\",\"version\":\"1.0.0\"}"));
    foreach(string name in new[]{"Media-v1","Media-v2"}){Put(ctx,"Desktop\\"+name+"\\src\\Main.cs",Encoding.UTF8.GetBytes("class Same {}"));Put(ctx,"Desktop\\"+name+"\\assets\\photo.webp",new byte[]{1,2,3});}
    foreach(string name in new[]{"Private-v1","Private-v2"}){Put(ctx,"Desktop\\"+name+"\\src\\Main.cs",Encoding.UTF8.GetBytes("class Same {}"));Put(ctx,"Desktop\\"+name+"\\local.properties",Encoding.UTF8.GetBytes("unique.fixture=true"));}
    ScanResult scan=DesktopScanner.Scan(ctx,CancellationToken.None,null);Assert(scan.Rows.Count==6&&scan.Rows.All(r=>!r.Eligible&&!r.Selected),"Unique branch, media, or local settings became a source deletion candidate.");
   });
   Test("Markerless src or app children with unique parent assets remain report-only", delegate {
    ScanContext ctx=NewContext("ambiguous-source-boundary");
    foreach(string child in new[]{"src","app"})foreach(string version in new[]{"v1","v2"}) {
     string project="Markerless-"+child+"-"+version;Put(ctx,"Desktop\\"+project+"\\"+child+"\\Main.cs",Encoding.UTF8.GetBytes("class Same {}"));Put(ctx,"Desktop\\"+project+"\\assets\\personal-photo.png",new byte[]{1,2,3});Put(ctx,"Desktop\\"+project+"\\.env.local",Encoding.UTF8.GetBytes("unique.fixture.version="+version));
    }
    ScanResult scan=DesktopScanner.Scan(ctx,CancellationToken.None,null);Assert(scan.Rows.Count==4&&scan.Rows.All(r=>!r.Eligible&&!r.Selected),"Markerless conventional child source was treated as a complete deletable project while parent assets/settings were outside the proof.");
    Assert(Directory.GetFiles(ctx.Desktop,"personal-photo.png",SearchOption.AllDirectories).Length==4&&Directory.GetFiles(ctx.Desktop,".env.local",SearchOption.AllDirectories).Length==4,"Read-only source-boundary scan modified personal fixture siblings.");
   });
   Test("Partial cache cancellation persists completed and cancelled entries with exact bytes", delegate {
    ScanContext ctx=NewContext("partial-cancel-journal");byte[] bytes=Encoding.UTF8.GetBytes("generated partial-cancel cache");
    string one=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\one.tmp",bytes),two=Put(ctx,"AppData\\Local\\node-gyp\\Cache\\two.tmp",bytes);ScanRow row=ScannedRowFor(ctx,one);row.Selected=true;
    using(var cancel=new CancellationTokenSource()) {
     OperationReport op=CleanupEngine.Run(ctx,new List<ScanRow>{row},true,cancel.Token,delegate(string progress){cancel.Cancel();});
     Assert(op.ProcessedBytes==bytes.Length&&op.Entries.Count(e=>e.Status=="Deleted")==1&&op.Entries.Any(e=>e.Status=="Cancelled"),"Partial cancellation discarded completed or cancelled outcome.");
     Assert(new[]{one,two}.Count(File.Exists)==1,"Partial cancellation deleted an unprocessed fixture.");
     string journal=System.IO.Path.Combine(ctx.ReportDirectory,"operation-"+op.Id+".json");var saved=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<OperationReport>(File.ReadAllText(journal));
     Assert(saved.CompletedUtc!=DateTime.MinValue&&saved.ProcessedBytes==op.ProcessedBytes&&saved.Entries.Count==op.Entries.Count,"Partial cancellation journal is incomplete.");
    }
   });
   Test("Organizer normal move and restore preserve generated media byte-for-byte", delegate {
    ScanContext ctx=NewContext("organizer-media-roundtrip");byte[] media={137,80,78,71,13,10,26,10,1,2,3,4};string photo=Put(ctx,"Desktop\\fixture-photo.png",media);
    ScanRow row=Organizer.Preview(ctx,CancellationToken.None,null).Rows.First(r=>Same(r.Path,photo)&&r.Eligible);Assert(!row.Selected,"Organizer auto-selected media.");row.Selected=true;
    OperationReport moved=Organizer.Apply(ctx,new List<ScanRow>{row},CancellationToken.None,null);Assert(!File.Exists(photo)&&File.ReadAllBytes(row.Keeper).SequenceEqual(media)&&moved.ProcessedBytes==media.Length,"Organizer media move changed or deleted bytes.");
    OperationReport restored=Organizer.Restore(ctx,moved,CancellationToken.None,null);Assert(File.ReadAllBytes(photo).SequenceEqual(media)&&restored.ProcessedBytes==media.Length,"Organizer normal media restore failed.");
   });
   Test("Operation export escapes receipt fields and accurately labels restore versus permanent deletion", delegate {
    ScanContext ctx=NewContext("operation-export");Directory.CreateDirectory(ctx.ReportDirectory);string html=System.IO.Path.Combine(ctx.ReportDirectory,"receipt.html");
    var op=new OperationReport{Kind="source-recycle",ProcessedBytes=15,FreeBefore=50,FreeAfter=50,Entries=new List<OperationEntry>{new OperationEntry{OriginalPath="<script>bad()</script>",Detail="<img src=x onerror=bad()>",Status="Recycled",Bytes=15}}};ReportExport.Operation(html,op);string text=File.ReadAllText(html);
    Assert(!text.Contains("<script>bad()")&&!text.Contains("<img src=x"),"Operation receipt allows executable markup.");Assert(text.Contains("سطل زباله تخلیه نشده"),"Recycle receipt incorrectly claims immediate disk-space release.");
    op.Kind="restore";ReportExport.Operation(html,op);Assert(File.ReadAllText(html).Contains("بازیابی موارد همین رسید"),"Restore receipt is mislabeled as deletion.");op.Permanent=true;ReportExport.Operation(html,op);Assert(File.ReadAllText(html).Contains("غیرقابل بازگردانی"),"Permanent receipt omits irreversibility.");
   });
   Test("Installed app exports escape untrusted fields and preserve unknown size without invented zero", delegate {
    ScanContext ctx=NewContext("apps-export");Directory.CreateDirectory(ctx.ReportDirectory);string html=System.IO.Path.Combine(ctx.ReportDirectory,"apps.html"),csv=System.IO.Path.Combine(ctx.ReportDirectory,"apps.csv"),json=System.IO.Path.Combine(ctx.ReportDirectory,"apps.json");
    var result=new InstalledAppsResult();result.Apps.Add(new InstalledAppRow{Name="=2+2",Version="1",Publisher="<script>bad()</script>",InstallLocation=ctx.UserRoot,Source="Registry",SizeBytes=null,SizeKind="Unknown",SizeNote="<img src=x onerror=bad()>"});result.Apps.Add(new InstalledAppRow{Name="Known fixture",Version="1",SizeBytes=12,SizeKind="Estimated",SizeNote="fixture estimate"});result.Warnings.Add("<svg onload=bad()>");
    ReportExport.Apps(html,result);ReportExport.AppsCsv(csv,result);Format.SaveJson(json,result);
    string text=File.ReadAllText(html);Assert(!text.Contains("<script>bad()")&&!text.Contains("<img src=x")&&!text.Contains("<svg onload="),"App HTML export contains unescaped metadata.");Assert(text.Contains("نامشخص"),"App export lost unknown-size label.");Assert(File.ReadAllText(csv).Contains("\"'=2+2\""),"App CSV export allows formula execution.");
    var reread=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<InstalledAppsResult>(File.ReadAllText(json));Assert(reread.Apps.Count==2&&!reread.Apps.First(r=>r.Name=="=2+2").SizeBytes.HasValue,"App JSON export invented a zero size.");
   });
   InstalledAppsTestReport installedApps=InstalledAppsTests.RunTests(requested);
   foreach(InstalledAppsTestEntry entry in installedApps.Tests)report.Tests.Add(new TestEntry{Name=entry.Name,Status=entry.Status,Detail=entry.Detail});
   report.Passed+=installedApps.Passed;report.Failed+=installedApps.Failed;report.Skipped+=installedApps.Skipped;
   var updates=UpdateCheckerTests.RunTests();foreach(var entry in updates.Tests)report.Tests.Add(new TestEntry{Name=entry.Name,Status=entry.Status,Detail=entry.Detail});report.Passed+=updates.Passed;report.Failed+=updates.Failed;report.Skipped+=updates.Skipped;
   var dashboard=SpaceDashboardTests.RunTests(requested);foreach(var entry in dashboard.Tests)report.Tests.Add(new TestEntry{Name=entry.Name,Status=entry.Status,Detail=entry.Detail});report.Passed+=dashboard.Passed;report.Failed+=dashboard.Failed;report.Skipped+=dashboard.Skipped;
   var protection=ProtectedFoldersTests.RunTests(requested);foreach(var entry in protection.Tests)report.Tests.Add(new TestEntry{Name=entry.Name,Status=entry.Status,Detail=entry.Detail});report.Passed+=protection.Passed;report.Failed+=protection.Failed;report.Skipped+=protection.Skipped;
   var journals=JournalTests.RunTests(requested);foreach(var entry in journals.Tests)report.Tests.Add(new TestEntry{Name=entry.Name,Status=entry.Status,Detail=entry.Detail});report.Passed+=journals.Passed;report.Failed+=journals.Failed;report.Skipped+=journals.Skipped;
   report.CompletedUtc=DateTime.UtcNow;
   report.Gaps.Add("No cleanup, recycling, or organization of real user files was performed. All mutation tests used newly generated isolated fixture data.");
   report.Gaps.Add("Process guards were exercised using hidden generated Chrome/Studio/Java/BlueStacks/updater fixture executables, not every vendor version or inaccessible Java-command scenario. ACL-denied folders, shell recycle-limit warnings and giant/corrupt archive limits still require interactive/manual testing.");
   report.Gaps.Add("Exact source folders are report-only: Windows Shell refuses folder recycling while required write-denial child handles are held (observed HRESULT 0x80270021). Safe source ZIP duplicate recycling and prior receipt restoration remain tested.");
   Format.SaveJson(reportPath,report);
   return report.Failed==0?0:1;
  }
  private static void Test(string name,Action action){
   try{action();report.Tests.Add(new TestEntry{Name=name,Status="PASS",Detail="Verified using isolated generated fixture data."});report.Passed++;}
   catch(SkipTest e){report.Tests.Add(new TestEntry{Name=name,Status="SKIP",Detail=e.Message});report.Skipped++;report.Gaps.Add(name+": "+e.Message);}
   catch(Exception e){report.Tests.Add(new TestEntry{Name=name,Status="FAIL",Detail=e.GetType().Name+": "+e.Message});report.Failed++;}
  }
  private static void RecoverPriorFixtureReceipts(string fixtureBase){
   // Only recover generated QA payloads from previously failed runs. Never enumerate or empty the entire bin.
   foreach(string previous in Directory.GetDirectories(fixtureBase,"ClearGuard-fixture-*",SearchOption.TopDirectoryOnly)) {
    if(Same(previous,fixture)||!Within(previous,fixtureBase)||SafetyPolicy.HasReparseAncestor(previous))continue;
    foreach(string testCase in Directory.GetDirectories(previous)) {
     string reports=System.IO.Path.Combine(testCase,"Reports");
     if(!Directory.Exists(reports)||SafetyPolicy.HasReparseAncestor(reports))continue;
     foreach(string file in Directory.GetFiles(reports,"*.json",SearchOption.TopDirectoryOnly)) {
      try {
       OperationReport old=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<OperationReport>(File.ReadAllText(file));
       if(old==null||old.Entries==null)continue;
       foreach(OperationEntry entry in old.Entries) {
        if(entry.Status!="Recycled"||String.IsNullOrEmpty(entry.OriginalPath)||!Within(entry.OriginalPath,previous)||File.Exists(entry.OriginalPath)||Directory.Exists(entry.OriginalPath)||String.IsNullOrEmpty(entry.RecycleMetadata)||!File.Exists(entry.RecycleMetadata))continue;
        OperationEntry restored=RecycleService.Restore(entry);
        if(restored.Status=="Restored")report.RecoveredPriorFixtureReceipts.Add(entry.OriginalPath);
       }
      }catch(Exception e){report.Gaps.Add("Prior generated-fixture receipt recovery: "+e.Message);}
     }
    }
   }
  }
  private static ScanContext NewContext(string name){
   string root=System.IO.Path.Combine(fixture,(++sequence).ToString("D2")+"-"+name);
   ScanContext ctx=new ScanContext{UserRoot=root,LocalAppData=System.IO.Path.Combine(root,"AppData","Local"),Desktop=System.IO.Path.Combine(root,"Desktop"),Documents=System.IO.Path.Combine(root,"Documents"),Downloads=System.IO.Path.Combine(root,"Downloads"),ReportDirectory=System.IO.Path.Combine(root,"Reports"),IsFixture=true};
   Directory.CreateDirectory(ctx.LocalAppData);Directory.CreateDirectory(ctx.Desktop);Directory.CreateDirectory(ctx.Documents);Directory.CreateDirectory(ctx.Downloads);
   return ctx;
  }
  private static bool CreateFixtureJunction(string link,string target){
   if(!Within(link,fixture)||!Within(target,fixture)||File.Exists(link)||Directory.Exists(link))return false;
   string executable=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   if(!File.Exists(executable))return false;
   string script="New-Item -ItemType Junction -Path '"+link.Replace("'","''")+"' -Target '"+target.Replace("'","''")+"' -ErrorAction Stop | Out-Null";
   ProcessStartInfo start=new ProcessStartInfo(executable,"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true};
   try{using(Process process=Process.Start(start)){return process.WaitForExit(10000)&&process.ExitCode==0&&Directory.Exists(link);}}catch{return false;}
  }
  private static void AssertRunningGuard(string app,string processName,string caseName) {
   ScanContext ctx=NewContext(caseName);string executable=System.IO.Path.Combine(ctx.UserRoot,processName+".exe");
   if(processProbe==null) {
    string folder=System.IO.Path.Combine(fixture,"generated-process-probe");Directory.CreateDirectory(folder);string source=System.IO.Path.Combine(folder,"Probe.cs");processProbe=System.IO.Path.Combine(folder,"Probe.exe");
    PutAbsolute(source,Encoding.UTF8.GetBytes("using System.Threading; public static class GeneratedProcessGuardFixture { public static void Main(){Thread.Sleep(30000);} }"));
    string compiler=System.IO.Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"csc.exe");Assert(File.Exists(compiler),"Compiler for isolated process-guard fixture is unavailable.");
    var build=new ProcessStartInfo(compiler,"/nologo /target:winexe /out:\""+processProbe+"\" \""+source+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
    using(Process compilation=Process.Start(build)){Assert(compilation.WaitForExit(10000)&&compilation.ExitCode==0&&File.Exists(processProbe),"Isolated process-guard fixture did not compile.");}
   }
   Assert(Within(executable,fixture)&&!SafetyPolicy.HasReparseAncestor(executable),"Generated process fixture path escaped its boundary.");File.Copy(processProbe,executable);
   using(Process owned=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
    try {Assert(!owned.HasExited,"Generated process exited before guard test.");string reason;Assert(!ProcessGuard.Check(app,ctx,out reason)&&!String.IsNullOrWhiteSpace(reason),"Running "+app+" fixture did not block cache cleanup.");Assert(!owned.HasExited,"Process guard stopped a running process.");}
    finally {if(!owned.HasExited){owned.Kill();owned.WaitForExit(3000);}}
   }
  }
  private static string Put(ScanContext ctx,string relative,byte[] content){string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(ctx.UserRoot,relative));if(!Within(path,ctx.UserRoot))throw new InvalidOperationException("Fixture path escape.");PutAbsolute(path,content);return path;}
  private static void PutAbsolute(string path,byte[] content){if(!Within(path,fixture))throw new InvalidOperationException("Fixture writes outside isolated root are refused.");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));File.WriteAllBytes(path,content);}
  private static void MakeZip(string path,string entry,byte[] content){if(!Within(path,fixture))throw new InvalidOperationException("Archive fixture path escape.");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));using(FileStream fs=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(ZipArchive archive=new ZipArchive(fs,ZipArchiveMode.Create)){using(Stream s=archive.CreateEntry(entry).Open()){s.Write(content,0,content.Length);}}}
  private static ScanRow ScannedRowFor(ScanContext ctx,string file){ScanResult scan=CacheScanner.Scan(ctx,CancellationToken.None,null);ScanRow row=scan.Rows.FirstOrDefault(delegate(ScanRow r){return r.Files.Any(delegate(FileRecord f){return Same(f.Path,file);});});if(row==null)throw new InvalidOperationException("Approved fixture file missing from cache scan rows: "+file);return row;}
  private static ScanRow FabricateRow(string root,string file){FileInfo f=new FileInfo(file);return new ScanRow{Id=Guid.NewGuid().ToString("N"),Name="fabricated unauthorized fixture",Path=root,Category="Cache",App="node-gyp",Bytes=f.Length,SafeBytes=f.Length,FileCount=1,Eligible=true,Selected=true,Fingerprint=SafetyPolicy.HashTree(root),Files=new List<FileRecord>{new FileRecord{Path=file,Bytes=f.Length,ModifiedTicks=f.LastWriteTimeUtc.Ticks,Hash=SafetyPolicy.HashFile(file)}}};}
  private static bool Same(string left,string right){return String.Equals(System.IO.Path.GetFullPath(left).TrimEnd('\\'),System.IO.Path.GetFullPath(right).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
  private static bool Within(string path,string root){string p=System.IO.Path.GetFullPath(path).TrimEnd('\\');string r=System.IO.Path.GetFullPath(root).TrimEnd('\\');return String.Equals(p,r,StringComparison.OrdinalIgnoreCase)||p.StartsWith(r+"\\",StringComparison.OrdinalIgnoreCase);}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 }
}
