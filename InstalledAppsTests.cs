using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using Microsoft.Win32;

namespace ClearGuard {
 public sealed class InstalledAppsTestEntry { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
 public sealed class InstalledAppsTestReport {
  public int Passed {get;set;} public int Failed {get;set;} public int Skipped {get;set;}
  public bool RealUserFilesModified {get;set;}
  public List<InstalledAppsTestEntry> Tests {get;set;}
  public InstalledAppsTestReport(){Tests=new List<InstalledAppsTestEntry>();}
 }
 public static class InstalledAppsTests {
  public static InstalledAppsTestReport RunTests(string fixtureRoot) {
   string allowed=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests"));
   string requested=Path.GetFullPath(fixtureRoot);
   if(!requested.Equals(allowed,StringComparison.OrdinalIgnoreCase) && !requested.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Installed-app tests must use isolated work\\ClearGuardTests fixtures.");
   string fixture=Path.Combine(requested,"InstalledApps-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
   var report=new InstalledAppsTestReport();
   Run(report,"Installed apps: registry sizes use KiB and remain explicitly estimated",delegate {
    var a=Record("Editor","1","app",4096);var r=InstalledAppsScanner.ConvertRecords(new[]{a},CancellationToken.None);
    Assert(r.Apps.Count==1 && r.Apps[0].SizeBytes==4194304,"KiB conversion failed.");
    Assert(r.Apps[0].SizeKind=="Estimated" && r.Apps[0].SizeDisplay.Contains("تخمینی"),"Estimated size was presented as exact.");
   });
   Run(report,"Installed apps: signed DWORD and unsigned DWORD agree",delegate {
    long? signed=InstalledAppsScanner.ParseEstimatedSize(unchecked((int)0x80000001),RegistryValueKind.DWord);
    long? unsigned=InstalledAppsScanner.ParseEstimatedSize((uint)0x80000001,RegistryValueKind.DWord);
    Assert(signed==2199023256576L && signed==unsigned,"Unsigned DWORD was misinterpreted.");
   });
   Run(report,"Installed apps: absent malformed zero and non-DWORD sizes are unknown",delegate {
    foreach(object v in new object[]{null,"600",0,-1L,UInt64.MaxValue,Double.NaN,new object()})Assert(!InstalledAppsScanner.ParseEstimatedSize(v,RegistryValueKind.DWord).HasValue,"Invalid size became known.");
    Assert(!InstalledAppsScanner.ParseEstimatedSize(600,RegistryValueKind.String).HasValue,"String size was trusted.");
    var r=InstalledAppsScanner.ConvertRecords(new[]{Record("Unknown","1","u",null)},CancellationToken.None);
    Assert(!r.Apps[0].SizeBytes.HasValue && r.Apps[0].SizeDisplay=="نامشخص","Unknown size was displayed as zero.");
   });
   Run(report,"Installed apps: mirrored registry registrations deduplicate but versions remain",delegate {
    var a=Record("Editor","1","product-guid",200);var b=Record("Editor","1","PRODUCT-GUID",200);b.Architecture="32-bit registry";
    var c=Record("Editor","2","product-guid",300);
    var r=InstalledAppsScanner.ConvertRecords(new[]{a,b,c},CancellationToken.None);
    Assert(r.Apps.Count==2 && r.DuplicateRegistrationCount==1,"Versions or mirror registrations were incorrectly deduplicated.");
   });
   Run(report,"Installed apps: same names with distinct registrations are retained",delegate {
    var a=Record("Editor","1","product-A",200);var b=Record("Editor","1","product-B",200);
    Assert(InstalledAppsScanner.ConvertRecords(new[]{a,b},CancellationToken.None).Apps.Count==2,"Separate registrations collapsed.");
   });
   Run(report,"Installed apps: system components and update children have explicit hidden counts",delegate {
    var a=Record("App","1","app",200);var system=Record("System","1","system",200);system.SystemComponent=true;
    var child=Record("Patch","1","patch",200);child.ParentKeyName="app";
    var update=Record("Update","1","update",200);update.ReleaseType="Security Update";
    var framework=Record("Framework","1","store-framework",null);framework.IsFramework=true;framework.Source="Microsoft Store";
    var r=InstalledAppsScanner.ConvertRecords(new[]{a,system,child,update,framework},CancellationToken.None);
    Assert(r.Apps.Count==1 && r.HiddenComponentCount==4 && r.Warnings.Any(x=>x.Contains("4")),"Hidden component count missing.");
   });
   Run(report,"Installed apps: untrusted names are bounded and control characters removed",delegate {
    var a=Record(new string('A',3000)+"\r\n\0","1","app",100);var r=InstalledAppsScanner.ConvertRecords(new[]{a},CancellationToken.None);
    Assert(r.Apps[0].Name.Length<=1024 && r.Apps[0].Name.IndexOf('\n')<0,"Untrusted registry text was not bounded.");
   });
   Run(report,"Installed apps: Store JSON handles empty one and many without command execution",delegate {
    Assert(InstalledAppsScanner.ParseStoreJson("[]",CancellationToken.None).Count==0,"Empty packages failed.");
    string one="{\"Name\":\"Pkg\",\"Version\":\"1.0\",\"Publisher\":\"Vendor\",\"InstallLocation\":\"C:\\\\Apps\\\\Pkg\",\"PackageFullName\":\"Pkg_1_x64\",\"Architecture\":\"X64\",\"IsFramework\":false,\"IsResourcePackage\":false}";
    var r=InstalledAppsScanner.ConvertRecords(InstalledAppsScanner.ParseStoreJson(one,CancellationToken.None),CancellationToken.None);
    Assert(r.Apps.Count==1 && r.Apps[0].Source=="Microsoft Store" && !r.Apps[0].SizeBytes.HasValue,"Store metadata failed.");
    Assert(InstalledAppsScanner.ParseStoreJson("["+one+","+one+"]",CancellationToken.None).Count==2,"Package array failed.");
   });
   Run(report,"Installed apps: malformed and oversized Store JSON fail closed",delegate {
    Throws<ArgumentException>(delegate {InstalledAppsScanner.ParseStoreJson("{broken",CancellationToken.None);});
    Throws<InvalidDataException>(delegate {InstalledAppsScanner.ParseStoreJson(new string(' ',4*1024*1024+1),CancellationToken.None);});
   });
   Run(report,"Installed apps: canceled registry and Store parsing stop promptly",delegate {
    var c=new CancellationToken(true);
    Throws<OperationCanceledException>(delegate {InstalledAppsScanner.ConvertRecords(new[]{Record("App","1","app",100)},c);});
    Throws<OperationCanceledException>(delegate {InstalledAppsScanner.ParseStoreJson("[]",c);});
   });
   Run(report,"Installed apps: root system shared and relative locations cannot be measured",delegate {
    string reason;string[] locations={Path.GetPathRoot(fixture),Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"..\\relative","\\\\server\\share\\app","C:\\Windows\\System32"};
    foreach(string location in locations)if(!String.IsNullOrEmpty(location))Assert(!InstalledAppsScanner.CanMeasureLocation(location,out reason),"Unsafe broad location accepted: "+location);
   });
   Run(report,"Installed apps: app folder sizes read lengths only and do not change files",delegate {
    string app=Path.Combine(fixture,"isolated-app");Directory.CreateDirectory(Path.Combine(app,"sub"));File.WriteAllBytes(Path.Combine(app,"a.bin"),new byte[101]);File.WriteAllBytes(Path.Combine(app,"sub","b.bin"),new byte[203]);
    var row=InstalledAppsScanner.ConvertRecords(new[]{Record("Fixture App","1","fixture",999,app)},CancellationToken.None).Apps[0];
    DateTime stamp=File.GetLastWriteTimeUtc(Path.Combine(app,"a.bin"));var measured=InstalledAppsScanner.Measure(row,CancellationToken.None);
    Assert(measured.SizeBytes==304 && measured.SizeKind=="MeasuredFolder" && measured.SizeDisplay.Contains("پوشه"),"Folder length measurement wrong.");
    Assert(row.SizeKind=="Estimated" && File.GetLastWriteTimeUtc(Path.Combine(app,"a.bin"))==stamp && File.ReadAllBytes(Path.Combine(app,"a.bin")).Length==101,"Input row or fixture modified.");
   });
   Run(report,"Installed apps: unknown location retains prior estimate when measurement refused",delegate {
    var row=InstalledAppsScanner.ConvertRecords(new[]{Record("No Path","1","no-path",123)},CancellationToken.None).Apps[0];var measured=InstalledAppsScanner.Measure(row,CancellationToken.None);
    Assert(measured.SizeBytes==row.SizeBytes && measured.SizeKind=="Estimated" && measured.SizeNote.Contains("اندازه‌گیری نشد"),"Refusal destroyed the prior estimate.");
   });
   Run(report,"Installed apps: locked file metadata remains readable without opening content",delegate {
    string app=Path.Combine(fixture,"locked-app");Directory.CreateDirectory(app);string file=Path.Combine(app,"locked.bin");File.WriteAllBytes(file,new byte[82]);
    var row=InstalledAppsScanner.ConvertRecords(new[]{Record("Locked Fixture","1","locked",null,app)},CancellationToken.None).Apps[0];
    using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){var m=InstalledAppsScanner.Measure(row,CancellationToken.None);Assert(m.SizeBytes==82 && m.SizeKind=="MeasuredFolder","Locked metadata could not be measured.");}
   });
   Run(report,"Installed apps: overlapping app roots carry explicit double-count warning",delegate {
    string app=Path.Combine(fixture,"shared-app");Directory.CreateDirectory(Path.Combine(app,"child"));
    var a=Record("Parent","1","parent",500,app);var b=Record("Child","1","child",100,Path.Combine(app,"child"));
    var r=InstalledAppsScanner.ConvertRecords(new[]{a,b},CancellationToken.None);
    Assert(r.Apps.All(x=>x.SizeNote.Contains("هم‌پوشانی")) && r.Warnings.Any(x=>x.Contains("هم‌پوشانی")),"Overlapping sizes were silently additive.");
   });
   Run(report,"Installed apps: measurement cancellation leaves registry estimate untouched",delegate {
    string app=Path.Combine(fixture,"cancel-app");Directory.CreateDirectory(app);var row=InstalledAppsScanner.ConvertRecords(new[]{Record("Cancel","1","cancel",100,app)},CancellationToken.None).Apps[0];
    Throws<OperationCanceledException>(delegate {InstalledAppsScanner.Measure(row,new CancellationToken(true));});Assert(row.SizeBytes==102400,"Canceled scan changed row.");
   });
   Run(report,"Installed apps: bounded measurement never presents partial totals as complete",delegate {
    string app=Path.Combine(fixture,"bounded-app");Directory.CreateDirectory(app);for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(app,"data"+i+".bin"),new byte[40]);
    var row=InstalledAppsScanner.ConvertRecords(new[]{Record("Bounded","1","bounded",null,app)},CancellationToken.None).Apps[0];var measured=InstalledAppsScanner.Measure(row,CancellationToken.None,2,1000);
    Assert(!measured.SizeBytes.HasValue && measured.SizeKind=="Unknown" && measured.SizeNote.Contains("ناقص"),"Partial total was presented as the full folder size.");
    Throws<ArgumentOutOfRangeException>(delegate{InstalledAppsScanner.Measure(row,CancellationToken.None,100001,20000);});
   });
   Run(report,"Installed apps: real junctions and linked ancestors are never followed",delegate {
    string outside=Path.Combine(fixture,"junction-target");Directory.CreateDirectory(Path.Combine(outside,"sub"));File.WriteAllBytes(Path.Combine(outside,"private.bin"),new byte[1234]);
    string app=Path.Combine(fixture,"junction-app");Directory.CreateDirectory(app);string link=Path.Combine(app,"linked");
    var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/c mklink /J \""+link+"\" \""+outside+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
    using(var process=Process.Start(start)){string output=process.StandardOutput.ReadToEnd();string error=process.StandardError.ReadToEnd();Assert(process.WaitForExit(5000) && process.ExitCode==0,"Fixture junction creation failed: "+output+error);}
    string reason;Assert(!InstalledAppsScanner.CanMeasureLocation(link,out reason),"Root junction was accepted.");
    Assert(!InstalledAppsScanner.CanMeasureLocation(Path.Combine(link,"sub"),out reason),"Linked ancestor was accepted.");
    var row=InstalledAppsScanner.ConvertRecords(new[]{Record("Junction","1","junction",null,app)},CancellationToken.None).Apps[0];var measured=InstalledAppsScanner.Measure(row,CancellationToken.None);
    Assert(!measured.SizeBytes.HasValue && measured.SizeNote.Contains("junction"),"Nested junction target was traversed or partial size reported complete.");
    Assert(File.ReadAllBytes(Path.Combine(outside,"private.bin")).Length==1234,"Junction target modified.");
   });
   Run(report,"Installed apps: repeated shared paths do not silently aggregate sizes",delegate {
    string app=Path.Combine(fixture,"many-shared");Directory.CreateDirectory(app);var records=Enumerable.Range(0,1000).Select(i=>Record("App "+i,"1","product-"+i,100,app)).ToArray();
    var result=InstalledAppsScanner.ConvertRecords(records,CancellationToken.None);
    Assert(result.Apps.Count==1000 && result.Apps.All(x=>x.SizeNote.Contains("هم‌پوشانی")),"Shared paths lost metadata or warnings.");
   });
   Run(report,"Installed apps: drive-relative and root-relative install paths are rejected",delegate {
    string app=Path.Combine(fixture,"absolute-only");Directory.CreateDirectory(app);string reason;
    Assert(!InstalledAppsScanner.CanMeasureLocation(app.Substring(0,2)+app.Substring(3),out reason),"Drive-relative path was resolved using the current directory.");
    Assert(!InstalledAppsScanner.CanMeasureLocation(app.Substring(2),out reason),"Root-relative path was resolved using the current drive.");
   });
   Run(report,"Installed apps: manual rows with null notes measure safely",delegate {
    string app=Path.Combine(fixture,"null-note");Directory.CreateDirectory(app);File.WriteAllBytes(Path.Combine(app,"app.bin"),new byte[17]);
    var measured=InstalledAppsScanner.Measure(new InstalledAppRow{Name="Fixture",InstallLocation=app},CancellationToken.None);
    Assert(measured.SizeBytes==17 && measured.SizeKind=="MeasuredFolder" && measured.SizeNote!=null,"Null note caused a measurement failure.");
   });
   Run(report,"Installed apps: localized size labels retain machine-readable size kinds",delegate {
    var estimated=new InstalledAppRow{SizeKind="Estimated"};var measured=new InstalledAppRow{SizeKind="MeasuredFolder"};var unknown=new InstalledAppRow{SizeKind="Unknown"};
    Assert(estimated.SizeKindDisplay=="اعلام نصب‌کننده" && estimated.SizeKind=="Estimated","Estimated label or machine kind changed.");
    Assert(measured.SizeKindDisplay=="اندازه‌گیری پوشه" && measured.SizeKind=="MeasuredFolder","Measured label or machine kind changed.");
    Assert(unknown.SizeKindDisplay=="نامشخص" && unknown.SizeKind=="Unknown" && new InstalledAppRow().SizeKindDisplay=="نامشخص","Unknown label failed.");
   });
   Run(report,"Report bundles: installed apps save three new formats without data loss",delegate {
    string dir=Path.Combine(fixture,"apps-report-success");Directory.CreateDirectory(dir);string json=Path.Combine(dir,"report.json");var apps=ReportApps();ReportExport.SaveNewAppsBundle(json,apps);
    Assert(File.ReadAllText(json).Contains("Fixture App") && File.ReadAllText(Path.ChangeExtension(json,"html")).Contains("Fixture App") && File.ReadAllText(Path.ChangeExtension(json,"csv")).Contains("Name,Version"),"New app report formats missing.");
    Assert(File.ReadAllText(Path.ChangeExtension(json,"csv")).Contains("Unknown"),"Unknown size provenance missing from CSV.");
   });
   Run(report,"Report bundles: scan saves three new formats with preserved source data",delegate {
    string dir=Path.Combine(fixture,"scan-report-success");Directory.CreateDirectory(dir);string json=Path.Combine(dir,"report.json");var scan=ReportScan();ReportExport.SaveNewScanBundle(json,scan);
    Assert(File.ReadAllText(json).Contains("Fixture scan") && File.ReadAllText(Path.ChangeExtension(json,"html")).Contains("Fixture scan") && File.ReadAllText(Path.ChangeExtension(json,"csv")).Contains("Name,Path"),"New scan report formats missing.");
    Assert(scan.Rows.Count==1 && scan.Rows[0].Name=="Fixture scan","Export changed scan source data.");
   });
   Run(report,"Report bundles: existing file or directory companions prevent every write",delegate {
    foreach(string extension in new[]{"json","html","csv"})foreach(bool folder in new[]{false,true})foreach(bool apps in new[]{false,true}) {
     string dir=Path.Combine(fixture,"report-collision-"+extension+"-"+folder+"-"+apps);Directory.CreateDirectory(dir);string json=Path.Combine(dir,"report.json"),collision=Path.ChangeExtension(json,extension);if(folder)Directory.CreateDirectory(collision);else File.WriteAllText(collision,"KEEP ORIGINAL PERSONAL FIXTURE");
     Throws<IOException>(delegate{if(apps)ReportExport.SaveNewAppsBundle(json,ReportApps());else ReportExport.SaveNewScanBundle(json,ReportScan());});
     if(folder)Assert(Directory.Exists(collision) && !Directory.EnumerateFileSystemEntries(collision).Any(),"Directory companion changed.");else Assert(File.ReadAllText(collision)=="KEEP ORIGINAL PERSONAL FIXTURE","Existing companion overwritten.");
     foreach(string candidate in new[]{json,Path.ChangeExtension(json,"html"),Path.ChangeExtension(json,"csv")})if(candidate!=collision)Assert(!File.Exists(candidate) && !Directory.Exists(candidate),"Preflight collision wrote a partial report.");
    }
   });
   Run(report,"Report bundles: exclusive writers reject targets created after preflight",delegate {
    string dir=Path.Combine(fixture,"report-create-new");Directory.CreateDirectory(dir);string html=Path.Combine(dir,"existing.html"),csv=Path.Combine(dir,"existing.csv");File.WriteAllText(html,"HTML KEEP");File.WriteAllText(csv,"CSV KEEP");
    Throws<IOException>(delegate{ReportExport.Apps(html,ReportApps(),true);});Throws<IOException>(delegate{ReportExport.AppsCsv(csv,ReportApps(),true);});Throws<IOException>(delegate{ReportExport.Scan(html,ReportScan(),true);});Throws<IOException>(delegate{ReportExport.Csv(csv,ReportScan(),true);});
    Assert(File.ReadAllText(html)=="HTML KEEP" && File.ReadAllText(csv)=="CSV KEEP","CreateNew race guard overwrote a target.");
   });
   Run(report,"Report bundles: non-JSON and same-target extension choices are refused",delegate {
    string dir=Path.Combine(fixture,"report-extension");Directory.CreateDirectory(dir);foreach(string extension in new[]{"html","csv","txt"}){string path=Path.Combine(dir,"report."+extension);Throws<IOException>(delegate{ReportExport.SaveNewAppsBundle(path,ReportApps());});Throws<IOException>(delegate{ReportExport.SaveNewScanBundle(path,ReportScan());});}
    Assert(!Directory.EnumerateFileSystemEntries(dir).Any(),"Non-JSON export wrote overlapping or unexpected formats.");
   });
   Run(report,"CSV reports: leading whitespace formula fields remain inert and unchanged",delegate {
    string dir=Path.Combine(fixture,"report-csv-prefix");Directory.CreateDirectory(dir);string[] values={" =2+2","  @SUM(1)","\t=2+2","\r=2+2","\n=2+2"," +2"," -2"};
    foreach(string value in values){var apps=ReportApps();apps.Apps[0].Name=value;var scan=ReportScan();scan.Rows[0].Name=value;string appPath=Path.Combine(dir,Guid.NewGuid().ToString("N")+".csv"),scanPath=Path.Combine(dir,Guid.NewGuid().ToString("N")+".csv");ReportExport.AppsCsv(appPath,apps,true);ReportExport.Csv(scanPath,scan,true);
     Assert(File.ReadAllText(appPath).Contains("\"'"+value+"\"") && File.ReadAllText(scanPath).Contains("\"'"+value+"\""),"Leading-space/control formula was not prefixed safely.");Assert(apps.Apps[0].Name==value && scan.Rows[0].Name==value,"Export changed source field text.");}
   });
   return report;
  }
  private static InstalledAppsResult ReportApps(){var r=new InstalledAppsResult();r.Apps.Add(new InstalledAppRow{Name="Fixture App",Version="1",Publisher="Fixture Vendor",Source="Registry",SizeKind="Unknown"});return r;}
  private static ScanResult ReportScan(){var r=new ScanResult{Kind="Fixture scan",CompletedUtc=DateTime.UtcNow};r.Rows.Add(new ScanRow{Name="Fixture scan",Bytes=123,Eligible=false});return r;}
  private static InstalledAppRecord Record(string name,string version,string registration,object size,string location=null){return new InstalledAppRecord{DisplayName=name,Version=version,Publisher="Fixture Vendor",RegistrationKey=registration,Hive="HKLM",Source="Registry",Architecture="64-bit registry",InstallLocation=location,EstimatedSize=size,EstimatedSizeKind=RegistryValueKind.DWord};}
  private static void Run(InstalledAppsTestReport report,string name,Action test){try{test();report.Passed++;report.Tests.Add(new InstalledAppsTestEntry{Name=name,Status="PASS"});}catch(Exception e){report.Failed++;report.Tests.Add(new InstalledAppsTestEntry{Name=name,Status="FAIL",Detail=e.GetType().Name+": "+e.Message});}}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
  private static void Throws<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
 }
}
