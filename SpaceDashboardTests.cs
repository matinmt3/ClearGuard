using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;

namespace ClearGuard {
 public sealed class SpaceDashboardTestEntry { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
 public sealed class SpaceDashboardTestReport {
  public int Passed {get;set;} public int Failed {get;set;} public int Skipped {get;set;}
  public bool RealUserFilesModified {get;set;}
  public List<SpaceDashboardTestEntry> Tests {get;set;}
  public SpaceDashboardTestReport(){Tests=new List<SpaceDashboardTestEntry>();}
 }
 public static class SpaceDashboardTests {
  public static SpaceDashboardTestReport RunTests(string fixtureRoot) {
   string allowed=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests"));
   string requested=Path.GetFullPath(fixtureRoot);
   if(!requested.Equals(allowed,StringComparison.OrdinalIgnoreCase) && !requested.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Dashboard tests must use isolated work\\ClearGuardTests fixtures.");
   string fixture=Path.Combine(requested,"SpaceDashboard-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
   var report=new SpaceDashboardTestReport();
   Run(report,"Disk dashboard: immediate folders and root files partition logical bytes",delegate {
    string root=Folder(fixture,"partition");File.WriteAllBytes(Path.Combine(root,"root.bin"),new byte[11]);string a=Folder(root,"A");File.WriteAllBytes(Path.Combine(a,"a.bin"),new byte[23]);string sub=Folder(a,"sub");File.WriteAllBytes(Path.Combine(sub,"b.bin"),new byte[41]);Folder(root,"Empty");
    var s=Service(fixture,"partition").Scan(root,CancellationToken.None);
    Assert(s.IsComplete && s.TotalBytes==75 && s.FileCount==3 && s.Rows.Count==3,"Partition total or file count incorrect.");Assert(s.Rows.Sum(x=>x.ObservedBytes)==s.TotalBytes && Row(s,"A").ObservedBytes==64,"Nested category was double-counted.");Assert(Row(s,"Empty").IsComplete && Row(s,"Empty").ObservedBytes==0 && Row(s,"Empty").SizeDisplay.Contains("0 B"),"An accessible empty folder is not exact zero.");Assert(s.Rows.All(x=>x.Ratio>=0 && x.Ratio<=100),"Bar ratios are outside 0..100.");
   });
   Run(report,"Disk dashboard: reads metadata without modifying source bytes or write stamps",delegate {
    string root=Folder(fixture,"readonly"),file=Path.Combine(root,"photo-fixture.bin");File.WriteAllBytes(file,new byte[]{1,2,3,4});DateTime stamp=File.GetLastWriteTimeUtc(file);var s=Service(fixture,"readonly").Scan(root,CancellationToken.None);
    Assert(s.IsComplete && File.GetLastWriteTimeUtc(file)==stamp && File.ReadAllBytes(file).SequenceEqual(new byte[]{1,2,3,4}),"Read-only scan modified a source fixture.");
   });
   Run(report,"Disk dashboard: inaccessible folder is unknown not zero",delegate {
    string root=Folder(fixture,"denied"),denied=Folder(root,"Denied");var service=new SpaceDashboardService(Path.Combine(fixture,"denied-store"),new DeniedFileSystem(denied));var s=service.Scan(root,CancellationToken.None);var row=Row(s,"Denied");
    Assert(!s.IsComplete && !row.IsComplete && row.ErrorCount>0 && row.SizeDisplay.Contains("ناقص") && !row.SizeDisplay.Equals("0 B"),"Denied folder was falsely reported empty or complete.");
   });
   Run(report,"Disk dashboard: bounded entry traversal is partial and never has exact deltas",delegate {
    string root=Folder(fixture,"bounded");for(int i=0;i<5;i++)File.WriteAllBytes(Path.Combine(root,"f"+i),new byte[9]);var service=Service(fixture,"bounded");var s=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxEntries=2});var comparison=service.Compare(s,s);
    Assert(!s.IsComplete && s.Status=="Partial" && s.SeenEntries<=2 && !comparison.IsComparable && comparison.Rows.All(x=>!x.DeltaBytes.HasValue),"Bounded scan gave complete or exact comparison.");
   });
   Run(report,"Disk dashboard: time budget stops traversal and labels observed bytes",delegate {
    string root=Folder(fixture,"time");File.WriteAllBytes(Path.Combine(root,"data"),new byte[4]);var service=new SpaceDashboardService(Path.Combine(fixture,"time-store"),new SlowFileSystem());var s=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxMilliseconds=1});
    Assert(!s.IsComplete && s.Status=="Partial" && s.Warnings.Any(x=>x.Contains("زمان")),"Time budget did not produce a safely labeled partial scan.");
   });
   Run(report,"Disk dashboard: file metadata errors preserve other observed bytes but remain partial",delegate {
    string root=Folder(fixture,"metadata-errors");File.WriteAllBytes(Path.Combine(root,"good"),new byte[7]);File.WriteAllBytes(Path.Combine(root,"bad"),new byte[19]);var service=new SpaceDashboardService(Path.Combine(fixture,"metadata-errors-store"),new LengthFailureFileSystem());var s=service.Scan(root,CancellationToken.None);Assert(!s.IsComplete && s.TotalBytes==7 && s.FileCount==1 && s.Rows[0].ErrorCount>0 && s.TotalDisplay.Contains("ناقص"),"Metadata failure became exact zero or concealed partial total.");
   });
   Run(report,"Disk dashboard: depth and category budgets prevent falsely complete totals",delegate {
    string root=Folder(fixture,"depth");string deep=Folder(Folder(Folder(root,"A"),"B"),"C");File.WriteAllBytes(Path.Combine(deep,"data"),new byte[9]);var service=Service(fixture,"depth");var depth=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxDepth=1});Assert(!depth.IsComplete && !Row(depth,"A").IsComplete && depth.TotalBytes==0,"Depth-excluded content was reported as exact empty.");var categories=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxCategories=1});Assert(!categories.IsComplete && categories.Rows.Count==1 && categories.Warnings.Any(x=>x.Contains("دسته")),"Category bound did not stop safely.");
   });
   Run(report,"Disk dashboard: cancellation during traversal preserves already observed bytes",delegate {
    string root=Folder(fixture,"mid-cancel");File.WriteAllBytes(Path.Combine(root,"first"),new byte[7]);File.WriteAllBytes(Path.Combine(root,"second"),new byte[11]);using(var cancellation=new CancellationTokenSource()){var service=new SpaceDashboardService(Path.Combine(fixture,"mid-cancel-store"),new CancellingFileSystem(cancellation));var s=service.Scan(root,cancellation.Token);Assert(s.Status=="Cancelled" && !s.IsComplete && s.FileCount==1 && s.TotalBytes>0,"Cancellation lost observed state or reported complete traversal.");}
   });
   Run(report,"Disk dashboard: cancellation returns partial state and prevents persistence",delegate {
    string root=Folder(fixture,"cancel");var service=Service(fixture,"cancel");var s=service.Scan(root,new CancellationToken(true));Assert(!s.IsComplete && s.Status=="Cancelled","Cancellation discarded or mislabeled state.");Throws<InvalidDataException>(delegate{service.SaveSnapshot(s);});
   });
   Run(report,"Disk dashboard: snapshot persistence honors pre-cancel without creating storage",delegate {
    string root=Folder(fixture,"save-pre-cancel");var service=Service(fixture,"save-pre-cancel");var snapshot=service.Scan(root,CancellationToken.None);Throws<OperationCanceledException>(delegate{service.SaveSnapshot(snapshot,new CancellationToken(true));});Assert(!Directory.Exists(service.SnapshotDirectory),"Pre-cancelled persistence created storage or an artifact.");
   });
   Run(report,"Disk dashboard: cancellation before stage write flush or commit leaves no snapshot",delegate {
    foreach(string checkpoint in new[]{"BeforeSerialize","BeforeStage","BeforeWrite","BeforeFlush","BeforeCommit"})using(var cancellation=new CancellationTokenSource()){string root=Folder(fixture,"save-cancel-"+checkpoint);File.WriteAllBytes(Path.Combine(root,"data"),new byte[7]);var service=new SpaceDashboardService(Path.Combine(fixture,"save-cancel-"+checkpoint+"-store"),new CancellingSaveFileSystem(cancellation,checkpoint));var snapshot=service.Scan(root,CancellationToken.None);Throws<OperationCanceledException>(delegate{service.SaveSnapshot(snapshot,cancellation.Token);});Assert(!Directory.Exists(service.SnapshotDirectory) || !Directory.EnumerateFileSystemEntries(service.SnapshotDirectory).Any(),"Cancelled save left a final snapshot or pending artifact.");Assert(File.ReadAllBytes(Path.Combine(root,"data")).Length==7,"Cancelled save modified source bytes.");}
   });
   Run(report,"Disk dashboard: cancellation after atomic commit returns saved fact",delegate {
    string root=Folder(fixture,"save-committed");using(var cancellation=new CancellationTokenSource()){var service=new SpaceDashboardService(Path.Combine(fixture,"save-committed-store"),new CancellingSaveFileSystem(cancellation,"Committed"));var snapshot=service.Scan(root,CancellationToken.None);string path=service.SaveSnapshot(snapshot,cancellation.Token);Assert(cancellation.IsCancellationRequested && File.Exists(path) && service.LoadSnapshot(path).SnapshotId==snapshot.SnapshotId,"Committed save incorrectly reported cancellation or lost final data.");}
   });
   Run(report,"Disk dashboard: mapped network unknown optical and unavailable drive classes are refused",delegate {
    string root=Folder(fixture,"drive-kind");foreach(DriveType kind in new[]{DriveType.Network,DriveType.Unknown,DriveType.NoRootDirectory,DriveType.CDRom}){var service=new SpaceDashboardService(Path.Combine(fixture,"drive-kind-"+kind+"-store"),new ClassifiedDriveFileSystem(root,kind));Throws<ArgumentException>(delegate{service.Scan(root,CancellationToken.None);});}
    foreach(DriveType kind in new[]{DriveType.Network,DriveType.Removable,DriveType.Ram})Throws<ArgumentException>(delegate{new SpaceDashboardService(root,new ClassifiedDriveFileSystem(root,kind));});
   });
   Run(report,"Disk dashboard: canonical aliases compare while other roots do not",delegate {
    string root=Folder(fixture,"aliases");File.WriteAllBytes(Path.Combine(root,"data"),new byte[8]);var service=Service(fixture,"aliases");var a=service.Scan(root,CancellationToken.None);var b=service.Scan(Path.Combine(root,"..",Path.GetFileName(root))+Path.DirectorySeparatorChar,CancellationToken.None);
    Assert(a.ScopeKey==b.ScopeKey && service.Compare(a,b).IsComparable,"Equivalent root spellings split scope.");var other=service.Scan(Folder(fixture,"aliases-other"),CancellationToken.None);Assert(!service.Compare(a,other).IsComparable,"Different roots received exact deltas.");
   });
   Run(report,"Disk dashboard: limits and traversal policy are part of comparison scope",delegate {
    string root=Folder(fixture,"policy");var service=Service(fixture,"policy");var a=service.Scan(root,CancellationToken.None);var b=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxEntries=123});Assert(!service.Compare(a,b).IsComparable && a.ScopeKey!=b.ScopeKey,"Different budgets were treated as identical scopes.");
   });
   Run(report,"Disk dashboard: new missing and changed categories have signed exact deltas",delegate {
    string root=Folder(fixture,"delta"),old=Folder(root,"Old"),keep=Folder(root,"Keep");File.WriteAllBytes(Path.Combine(old,"old.bin"),new byte[7]);File.WriteAllBytes(Path.Combine(keep,"keep.bin"),new byte[11]);var service=Service(fixture,"delta");var previous=service.Scan(root,CancellationToken.None);
    Directory.Move(old,Path.Combine(fixture,"delta-moved-old"));File.WriteAllBytes(Path.Combine(keep,"keep.bin"),new byte[5]);string added=Folder(root,"New");File.WriteAllBytes(Path.Combine(added,"new.bin"),new byte[13]);var c=service.Compare(service.Scan(root,CancellationToken.None),previous);
    Assert(c.IsComparable && c.Rows.Single(x=>x.Name=="Old").ChangeKind=="Missing" && c.Rows.Single(x=>x.Name=="Old").DeltaBytes==-7,"Missing category was not reported.");Assert(c.Rows.Single(x=>x.Name=="New").ChangeKind=="New" && c.Rows.Single(x=>x.Name=="New").DeltaBytes==13 && c.Rows.Single(x=>x.Name=="Keep").DeltaBytes==-6,"Signed category differences are incorrect.");
   });
   Run(report,"Disk dashboard: incomplete previous snapshot never manufactures zero delta",delegate {
    string root=Folder(fixture,"partial-prior");File.WriteAllBytes(Path.Combine(root,"a"),new byte[1]);File.WriteAllBytes(Path.Combine(root,"b"),new byte[1]);var service=Service(fixture,"partial-prior");var previous=service.Scan(root,CancellationToken.None,new SpaceDashboardOptions{MaxEntries=1});var c=service.Compare(service.Scan(root,CancellationToken.None),previous);Assert(!c.IsComparable && c.Rows.All(x=>!x.DeltaBytes.HasValue && x.DeltaDisplay.Contains("قابل")),"Partial prior scan manufactured exact deltas.");
   });
   Run(report,"Disk dashboard: relative drive-relative network and ADS roots are refused",delegate {
    var service=Service(fixture,"invalid-root");foreach(string root in new[]{"relative","C:relative",@"\Windows",@"\\server\share",@"C:\Users\User:stream"})Throws<ArgumentException>(delegate{service.Scan(root,CancellationToken.None);});
   });
   Run(report,"Disk dashboard: newly persisted snapshots round-trip and never overwrite",delegate {
    string root=Folder(fixture,"roundtrip");File.WriteAllBytes(Path.Combine(root,"data"),new byte[17]);var service=Service(fixture,"roundtrip");var s=service.Scan(root,CancellationToken.None);string path=service.SaveSnapshot(s);string before=File.ReadAllText(path);var loaded=service.LoadSnapshot(path);
    Assert(loaded.TotalBytes==17 && loaded.ScopeKey==s.ScopeKey && loaded.Rows.Count==s.Rows.Count,"Snapshot round-trip lost scope or totals.");Throws<IOException>(delegate{service.SaveSnapshot(s);});Assert(File.ReadAllText(path)==before,"Existing snapshot was overwritten.");
   });
   Run(report,"Disk dashboard: latest comparison uses only matching complete persisted scope",delegate {
    string root=Folder(fixture,"latest");File.WriteAllBytes(Path.Combine(root,"data"),new byte[17]);var service=Service(fixture,"latest");var noPrior=service.CompareWithLatest(service.Scan(root,CancellationToken.None));Assert(!noPrior.IsComparable && noPrior.Rows.All(x=>!x.DeltaBytes.HasValue),"No prior snapshot was presented as zero change.");var old=service.Scan(root,CancellationToken.None);service.SaveSnapshot(old);File.WriteAllBytes(Path.Combine(root,"data"),new byte[23]);var current=service.Scan(root,CancellationToken.None);var comparison=service.CompareWithLatest(current);Assert(comparison.IsComparable && comparison.Rows.Sum(x=>x.DeltaBytes.GetValueOrDefault())==6,"Latest matching snapshot not used.");
   });
   Run(report,"Disk dashboard: external snapshot paths cannot be imported",delegate {
    string root=Folder(fixture,"external");var first=Service(fixture,"external-first");string path=first.SaveSnapshot(first.Scan(root,CancellationToken.None));Throws<InvalidDataException>(delegate{Service(fixture,"external-second").LoadSnapshot(path);});
   });
   Run(report,"Disk dashboard: malicious totals schema and cross-scope keys are rejected",delegate {
    string root=Folder(fixture,"malicious");File.WriteAllBytes(Path.Combine(root,"data"),new byte[5]);var service=Service(fixture,"malicious");string path=service.SaveSnapshot(service.Scan(root,CancellationToken.None));var serializer=new JavaScriptSerializer();var data=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
    data["TotalBytes"]=600000;File.WriteAllText(path,serializer.Serialize(data));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
    data["TotalBytes"]=5;data["ScopeKey"]=new string('a',64);File.WriteAllText(path,serializer.Serialize(data));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
    data["SchemaVersion"]=99;File.WriteAllText(path,serializer.Serialize(data));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
   });
   Run(report,"Disk dashboard: oversized snapshot input fails before JSON processing",delegate {
    string root=Folder(fixture,"oversized");var service=Service(fixture,"oversized");string path=service.SaveSnapshot(service.Scan(root,CancellationToken.None));File.WriteAllText(path,new string(' ',SpaceDashboardService.MaxSnapshotBytes+1));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
   });
   Run(report,"Disk dashboard: owned snapshot store is excluded explicitly from its scan",delegate {
    string root=Folder(fixture,"store-exclusion"),store=Path.Combine(root,"OwnedDashboard");File.WriteAllBytes(Path.Combine(root,"data"),new byte[31]);var service=new SpaceDashboardService(store);var first=service.Scan(root,CancellationToken.None);service.SaveSnapshot(first);var second=service.Scan(root,CancellationToken.None);Assert(first.IsComplete && second.IsComplete && first.TotalBytes==31 && second.TotalBytes==31 && first.ScopeKey==second.ScopeKey,"Own snapshot files caused artificial changes.");Assert(second.Warnings.Any(x=>x.Contains("snapshot") || x.Contains("ذخیره")),"Own store exclusion was not visible.");
   });
   Run(report,"Disk dashboard: incomplete newest history is not skipped for an older exact baseline",delegate {
    string root=Folder(fixture,"newest-partial");File.WriteAllBytes(Path.Combine(root,"one"),new byte[5]);var fs=new ToggleDeniedFileSystem(root);var service=new SpaceDashboardService(Path.Combine(fixture,"newest-partial-store"),fs);service.SaveSnapshot(service.Scan(root,CancellationToken.None));Thread.Sleep(2);fs.Deny=true;var partial=service.Scan(root,CancellationToken.None);service.SaveSnapshot(partial);fs.Deny=false;var comparison=service.CompareWithLatest(service.Scan(root,CancellationToken.None));Assert(!comparison.IsComparable && comparison.Status=="Incomplete" && comparison.Rows.All(x=>!x.DeltaBytes.HasValue),"Newer partial history was silently bypassed for exact deltas.");
   });
   Run(report,"Disk dashboard: corrupted history disables automatic exact comparison",delegate {
    string root=Folder(fixture,"corrupt-history");var service=Service(fixture,"corrupt-history");service.SaveSnapshot(service.Scan(root,CancellationToken.None));string corrupt=Path.Combine(service.SnapshotDirectory,"snapshot-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(corrupt,"{broken");var comparison=service.CompareWithLatest(service.Scan(root,CancellationToken.None));Assert(!comparison.IsComparable && comparison.Status=="InvalidHistory" && comparison.Rows.All(x=>!x.DeltaBytes.HasValue),"Automatic comparison silently bypassed possibly newer corrupt history.");
   });
   Run(report,"Disk dashboard: malicious category boundaries unknown fields and fake file totals fail closed",delegate {
    string root=Folder(fixture,"schema-boundary");File.WriteAllBytes(Path.Combine(root,"data"),new byte[5]);var service=Service(fixture,"schema-boundary");string path=service.SaveSnapshot(service.Scan(root,CancellationToken.None));string original=File.ReadAllText(path);var serializer=new JavaScriptSerializer();var d=(Dictionary<string,object>)serializer.DeserializeObject(original);d["ExtraUnsafeField"]="ignored?";File.WriteAllText(path,serializer.Serialize(d));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
    d=(Dictionary<string,object>)serializer.DeserializeObject(original);var row=(Dictionary<string,object>)((object[])d["Rows"])[0];row["FileCount"]=0;d["FileCount"]=0;File.WriteAllText(path,serializer.Serialize(d));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
    d=(Dictionary<string,object>)serializer.DeserializeObject(original);row=(Dictionary<string,object>)((object[])d["Rows"])[0];row["CategoryKey"]="directory:..\\outside";row["Path"]=Path.GetDirectoryName(root);File.WriteAllText(path,serializer.Serialize(d));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});
   });
   Run(report,"Disk dashboard: duplicate decoded root JSON keys cannot manufacture exact comparison",delegate {
    string root=Folder(fixture,"duplicate-root-json");File.WriteAllBytes(Path.Combine(root,"data"),new byte[5]);var service=Service(fixture,"duplicate-root-json");var snapshot=service.Scan(root,CancellationToken.None);string path=service.SaveSnapshot(snapshot),original=File.ReadAllText(path);foreach(string prefix in new[]{"\"TotalBytes\":999,","\"\\u0054otalBytes\":999,"}){File.WriteAllText(path,original.Replace("\"TotalBytes\":",prefix+"\"TotalBytes\":"));Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});var comparison=service.CompareWithLatest(service.Scan(root,CancellationToken.None));Assert(!comparison.IsComparable && comparison.Status=="InvalidHistory","Duplicate root keys manufactured exact comparison.");}
   });
   Run(report,"Disk dashboard: duplicate escaped row bytes and completion keys fail closed",delegate {
    string root=Folder(fixture,"duplicate-row-json");File.WriteAllBytes(Path.Combine(root,"data"),new byte[5]);var service=Service(fixture,"duplicate-row-json");string path=service.SaveSnapshot(service.Scan(root,CancellationToken.None)),original=File.ReadAllText(path);foreach(string altered in new[]{original.Replace("\"ObservedBytes\":","\"ObservedBytes\":999,\"ObservedBytes\":"),original.Replace("\"ObservedBytes\":","\"\\u004fbservedBytes\":999,\"ObservedBytes\":"),original.Replace("\"IsComplete\":true","\"IsComplete\":false,\"IsComplete\":true")}){File.WriteAllText(path,altered);Throws<InvalidDataException>(delegate{service.LoadSnapshot(path);});Assert(!service.CompareWithLatest(service.Scan(root,CancellationToken.None)).IsComparable,"Duplicate nested keys were collapsed into trusted exact data.");}
   });
   Run(report,"Disk dashboard: aliased trailing-dot and trailing-space path components are refused",delegate {
    string root=Folder(fixture,"ambiguous-alias");var service=Service(fixture,"ambiguous-alias");Throws<ArgumentException>(delegate{service.Scan(root+".",CancellationToken.None);});Throws<ArgumentException>(delegate{service.Scan(root+" ",CancellationToken.None);});
   });
   Run(report,"Disk dashboard: tilde components cannot alias scan roots or owned snapshot stores",delegate {
    string root=Folder(fixture,"short~1");var service=Service(fixture,"tilde-input");Throws<ArgumentException>(delegate{service.Scan(root,CancellationToken.None);});Throws<ArgumentException>(delegate{new SpaceDashboardService(Path.Combine(fixture,"owned~1","snapshots"));});
   });
   Run(report,"Disk dashboard: enumerated tilde paths fail partial and preserve safe siblings",delegate {
    string root=Folder(fixture,"tilde-entry");string odd=Folder(root,"linked~1");File.WriteAllBytes(Path.Combine(odd,"private"),new byte[19]);File.WriteAllBytes(Path.Combine(root,"safe"),new byte[7]);var service=new SpaceDashboardService(Path.Combine(fixture,"tilde-entry-store"),new TildeFirstFileSystem());var s=service.Scan(root,CancellationToken.None);Assert(!s.IsComplete && s.Status=="Partial" && s.TotalBytes==7 && s.Rows.All(x=>x.Name!="linked~1"),"Tilde child was followed or prevented reading safe siblings.");Assert(s.Warnings.Any(x=>x.Contains("برای مقایسه، پوشهٔ کوچک‌تری بدون لینک یا محدودیت دسترسی انتخاب کنید.")),"Partial snapshot lacks actionable narrower-scope instructions.");
   });
   Run(report,"Disk dashboard: nested tilde paths do not turn partial folder failures into user errors",delegate {
    string root=Folder(fixture,"tilde-nested"),normal=Folder(root,"Normal"),odd=Folder(normal,"odd~1");File.WriteAllBytes(Path.Combine(odd,"private"),new byte[23]);File.WriteAllBytes(Path.Combine(normal,"safe"),new byte[5]);var service=new SpaceDashboardService(Path.Combine(fixture,"tilde-nested-store"),new TildeFirstFileSystem());var s=service.Scan(root,CancellationToken.None);Assert(!s.IsComplete && s.TotalBytes==5 && !Row(s,"Normal").IsComplete,"Nested tilde alias was traversed or escaped bounded partial reporting.");
   });
   Run(report,"Disk dashboard: history lookup is bounded without pruning any artifacts",delegate {
    string root=Folder(fixture,"history-limit");var service=Service(fixture,"history-limit");Directory.CreateDirectory(service.SnapshotDirectory);for(int i=0;i<129;i++)File.WriteAllText(Path.Combine(service.SnapshotDirectory,"snapshot-"+Guid.NewGuid().ToString("N")+".json"),"{}");var comparison=service.CompareWithLatest(service.Scan(root,CancellationToken.None));Assert(!comparison.IsComparable && comparison.Status=="HistoryLimit" && Directory.EnumerateFiles(service.SnapshotDirectory).Count()==129,"History bound scanned unboundedly or pruned existing artifacts.");
   });
   Run(report,"Disk dashboard: option bounds are enforced",delegate {
    string root=Folder(fixture,"options");var service=Service(fixture,"options");foreach(var options in new[]{new SpaceDashboardOptions{MaxEntries=0},new SpaceDashboardOptions{MaxEntries=500001},new SpaceDashboardOptions{MaxMilliseconds=60001},new SpaceDashboardOptions{MaxDepth=0},new SpaceDashboardOptions{MaxCategories=4097}})Throws<ArgumentOutOfRangeException>(delegate{service.Scan(root,CancellationToken.None,options);});
   });
   Run(report,"Disk dashboard: junction roots ancestors nested categories and stores are not followed",delegate {
    string outside=Folder(fixture,"junction-target");File.WriteAllBytes(Path.Combine(outside,"private.bin"),new byte[1234]);Folder(outside,"child");string root=Folder(fixture,"junction-root"),link=Path.Combine(root,"linked");Junction(link,outside);var service=Service(fixture,"junction");var s=service.Scan(root,CancellationToken.None);
    Assert(!s.IsComplete && Row(s,"linked").ObservedBytes==0 && !Row(s,"linked").IsComplete,"Junction category was traversed or counted as exact zero.");Throws<ArgumentException>(delegate{service.Scan(link,CancellationToken.None);});Throws<ArgumentException>(delegate{service.Scan(Path.Combine(link,"child"),CancellationToken.None);});
    var linkedStore=new SpaceDashboardService(Path.Combine(link,"snapshot-store"));Throws<InvalidDataException>(delegate{linkedStore.SaveSnapshot(service.Scan(Folder(fixture,"safe-save-source"),CancellationToken.None));});Assert(!Directory.Exists(Path.Combine(outside,"snapshot-store")) && File.ReadAllBytes(Path.Combine(outside,"private.bin")).Length==1234,"Linked store wrote into the target.");
   });
   return report;
  }
  private sealed class DeniedFileSystem : SpaceDashboardFileSystem {
   private readonly string denied;public DeniedFileSystem(string path){denied=path;}
   public override IEnumerable<string> EnumerateEntries(string path){if(path.Equals(denied,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Fixture-only access denial.");return base.EnumerateEntries(path);}
  }
  private sealed class SlowFileSystem : SpaceDashboardFileSystem {public override IEnumerable<string> EnumerateEntries(string path){Thread.Sleep(5);return base.EnumerateEntries(path);}}
  private sealed class LengthFailureFileSystem : SpaceDashboardFileSystem {public override long GetLength(string path){if(Path.GetFileName(path)=="bad")throw new IOException("Fixture-only metadata failure.");return base.GetLength(path);}}
  private sealed class CancellingFileSystem : SpaceDashboardFileSystem {private readonly CancellationTokenSource cancellation;public CancellingFileSystem(CancellationTokenSource source){cancellation=source;}public override IEnumerable<string> EnumerateEntries(string path){int index=0;foreach(string entry in base.EnumerateEntries(path)){if(index++==1)cancellation.Cancel();yield return entry;}}}
  private sealed class ToggleDeniedFileSystem : SpaceDashboardFileSystem {private readonly string root;public bool Deny;public ToggleDeniedFileSystem(string path){root=path;}public override IEnumerable<string> EnumerateEntries(string path){if(Deny && path.Equals(root,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Fixture access changed.");return base.EnumerateEntries(path);}}
  private sealed class TildeFirstFileSystem : SpaceDashboardFileSystem {public override IEnumerable<string> EnumerateEntries(string path){return base.EnumerateEntries(path).OrderBy(x=>Path.GetFileName(x).Contains("~") ? 0 : 1);}}
  private sealed class ClassifiedDriveFileSystem : SpaceDashboardFileSystem {private readonly string classified;private readonly DriveType kind;public ClassifiedDriveFileSystem(string path,DriveType type){classified=path;kind=type;}public override DriveType GetDriveType(string path){return path.Equals(classified,StringComparison.OrdinalIgnoreCase) ? kind : base.GetDriveType(path);}}
  private sealed class CancellingSaveFileSystem : SpaceDashboardFileSystem {private readonly CancellationTokenSource cancellation;private readonly string checkpoint;public CancellingSaveFileSystem(CancellationTokenSource source,string phase){cancellation=source;checkpoint=phase;}public override void SnapshotCheckpoint(string phase){if(phase==checkpoint)cancellation.Cancel();}}
  private static string Folder(string parent,string name){string path=Path.Combine(parent,name);Directory.CreateDirectory(path);return path;}
  private static SpaceDashboardService Service(string fixture,string name){return new SpaceDashboardService(Path.Combine(fixture,name+"-store"));}
  private static SpaceDashboardRow Row(SpaceSnapshot s,string name){return s.Rows.Single(x=>x.Name==name);}
  private static void Junction(string link,string target){var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};using(var process=Process.Start(start)){string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();Assert(process.WaitForExit(5000) && process.ExitCode==0,"Fixture junction creation failed: "+output+error);}}
  private static void Run(SpaceDashboardTestReport report,string name,Action test){try{test();report.Passed++;report.Tests.Add(new SpaceDashboardTestEntry{Name=name,Status="PASS"});}catch(Exception e){report.Failed++;report.Tests.Add(new SpaceDashboardTestEntry{Name=name,Status="FAIL",Detail=e.GetType().Name+": "+e.Message});}}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
  private static void Throws<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
 }
}
