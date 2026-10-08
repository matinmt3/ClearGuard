using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClearGuard {
 public class ScanRow : INotifyPropertyChanged {
  private bool selected;
  public bool Selected { get { return selected; } set { selected=value; Notify("Selected"); } }
  public string Id {get;set;}
  public string Name {get;set;}
  public string Path {get;set;}
  public string Category {get;set;}
  public string App {get;set;}
  public long Bytes {get;set;}
  public long SafeBytes {get;set;}
  public int FileCount {get;set;}
  public int ProtectedCount {get;set;}
  public bool Eligible {get;set;}
  public string Status {get;set;}
  public string Effect {get;set;}
  public string BlockReason {get;set;}
  public string Keeper {get;set;}
  public string KeeperHash {get;set;}
  public long KeeperBytes {get;set;}
  public long KeeperModifiedTicks {get;set;}
  public string Version {get;set;}
  public string Hash {get;set;}
  public string Fingerprint {get;set;}
  public List<FileRecord> Files {get;set;}
  public string Size { get { return Format.Bytes(Bytes); } }
  public string SafeSize { get { return Format.Bytes(SafeBytes); } }
  public ScanRow() { Files=new List<FileRecord>(); Status="فقط بررسی"; }
  public event PropertyChangedEventHandler PropertyChanged;
  private void Notify(string prop) { if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs(prop)); }
 }
 public class FileRecord {
  public string Path {get;set;}
  public long Bytes {get;set;}
  public long ModifiedTicks {get;set;}
  public string Hash {get;set;}
 }
 public class ScanResult {
  public string Kind {get;set;}
  public string Root {get;set;}
  public DateTime StartedUtc {get;set;}
  public DateTime CompletedUtc {get;set;}
  public long FreeBefore {get;set;}
  public List<ScanRow> Rows {get;set;}
  public List<string> Warnings {get;set;}
  public ScanResult(){Rows=new List<ScanRow>();Warnings=new List<string>();StartedUtc=DateTime.UtcNow;}
 }
 public class OperationEntry {
  public string OriginalPath {get;set;}
  public string DestinationPath {get;set;}
  public string RecycleMetadata {get;set;}
  public string Status {get;set;}
  public string Detail {get;set;}
  public long Bytes {get;set;}
  public string Hash {get;set;}
 }
 public class OperationReport {
  public string Id {get;set;}
  public string Kind {get;set;}
  public DateTime StartedUtc {get;set;}
  public string Root {get;set;}
  public DateTime CompletedUtc {get;set;}
  public long FreeBefore {get;set;}
  public long FreeAfter {get;set;}
  public long ProcessedBytes {get;set;}
  public bool Permanent {get;set;}
  public List<OperationEntry> Entries {get;set;}
  public OperationReport(){Id=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);StartedUtc=DateTime.UtcNow;Entries=new List<OperationEntry>();}
 }
 public class ScanContext {
  public string UserRoot {get;set;}
  public string LocalAppData {get;set;}
  public string Desktop {get;set;}
  public string Documents {get;set;}
  public string Downloads {get;set;}
  public string ReportDirectory {get;set;}
  public bool IsFixture {get;set;}
  public static ScanContext Current(){string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);return new ScanContext{UserRoot=user, LocalAppData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Downloads=System.IO.Path.Combine(user,"Downloads"),ReportDirectory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearGuard","Reports")};}
 }
 public static class Format {
  public static string Bytes(long n){ if(n<1024)return n.ToString("N0")+" B"; string[] units={"KiB","MiB","GiB","TiB"};double v=n;int i=-1;do{v/=1024;i++;}while(v>=1024&&i<3);return v.ToString("N2")+" "+units[i]; }
  public static long FreeC(){try{return new DriveInfo("C").AvailableFreeSpace;}catch{return 0;}}
  public static string Json(object o){var j=new JavaScriptSerializer();j.MaxJsonLength=int.MaxValue;j.RecursionLimit=150;return j.Serialize(o);}
  public static void SaveJson(string path,object o){path=System.IO.Path.GetFullPath(path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string temp=path+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(temp,Json(o),new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 }
}
