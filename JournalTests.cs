using System;
using System.IO;
using System.Collections.Generic;

namespace ClearGuard {
 public sealed class JournalTestEntry {public string Name{get;set;}public string Status{get;set;}public string Detail{get;set;}}
 public sealed class JournalTestReport {
  public int Passed{get;set;}public int Failed{get;set;}public int Skipped{get;set;}public bool RealUserFilesModified{get;set;}
  public List<JournalTestEntry> Tests{get;set;}
  public JournalTestReport(){Tests=new List<JournalTestEntry>();}
 }
 // Fault injection is restricted to generated files; no real cleanup is invoked.
 public static class JournalTests {
  public static JournalTestReport RunTests(string fixtureRoot) {
   string allowed=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests"));string requested=Path.GetFullPath(fixtureRoot);
   if(!requested.Equals(allowed,StringComparison.OrdinalIgnoreCase)&&!requested.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Journal tests must use isolated work\\ClearGuardTests fixtures.");
   string fixture=Path.Combine(requested,"Journal-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var report=new JournalTestReport();
   Run(report,"Journal persistence: transient 1175 retries the same atomic replacement",delegate {
    string stage,target;Pair(fixture,out stage,out target);int attempts=0;var waits=new List<int>();
    CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){Assert(source==stage&&destination==target,"Retry changed operation targets.");if(++attempts==1)throw Win32(1175);File.Replace(source,destination,null);},waits.Add);
    Assert(attempts==2&&waits.Count==1&&waits[0]==25&&File.ReadAllText(target)=="NEW GENERATED JOURNAL"&&!File.Exists(stage),"Transient replacement did not recover atomically.");
   });
   Run(report,"Journal persistence: sharing violation 32 retries without changing data",delegate{Transient(fixture,32);});
   Run(report,"Journal persistence: lock violation 33 retries without changing data",delegate{Transient(fixture,33);});
   Run(report,"Journal persistence: unknown and permanent failures immediately rethrow",delegate {
    foreach(int code in new[]{5,2,3,112,1176,1177,1234}) {
     string stage,target;Pair(fixture,out stage,out target);int attempts=0,waits=0;IOException expected=Win32(code);
     Exception actual=Capture(delegate{CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;throw expected;},delegate(int milliseconds){waits++;});});
     Assert(Object.ReferenceEquals(actual,expected)&&attempts==1&&waits==0,"Permanent or unknown error was retried/swallowed.");Unchanged(stage,target);
    }
   });
   Run(report,"Journal persistence: retry exhaustion keeps prior journal and staging file",delegate {
    string stage,target;Pair(fixture,out stage,out target);int attempts=0;var waits=new List<int>();IOException expected=Win32(1175);
    Exception actual=Capture(delegate{CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;throw expected;},waits.Add);});
    Assert(Object.ReferenceEquals(actual,expected)&&attempts==6&&waits.Count==5,"Retry was unbounded, skipped or swallowed exhaustion.");
    int total=0;foreach(int wait in waits)total+=wait;Assert(total==575&&waits[0]==25&&waits[1]==50&&waits[2]==100&&waits[3]==200&&waits[4]==200,"Retry delay budget exceeded 575ms.");Unchanged(stage,target);
   });
   Run(report,"Journal persistence: a missing destination never falls back to a move",delegate {
    string folder=Folder(fixture),stage=Path.Combine(folder,"stage.writing"),target=Path.Combine(folder,"missing.json");File.WriteAllText(stage,"NEW GENERATED JOURNAL");int attempts=0,waits=0;IOException expected=Win32(1175);
    Exception actual=Capture(delegate{CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;throw expected;},delegate(int milliseconds){waits++;});});
    Assert(Object.ReferenceEquals(actual,expected)&&attempts==1&&waits==0&&!File.Exists(target)&&File.ReadAllText(stage)=="NEW GENERATED JOURNAL","Missing destination triggered retry/fallback or lost stage.");
   });
   Run(report,"Journal persistence: a missing staging file is never replaced or recreated",delegate {
    string folder=Folder(fixture),stage=Path.Combine(folder,"missing.writing"),target=Path.Combine(folder,"target.json");File.WriteAllText(target,"PRIOR GENERATED JOURNAL");int attempts=0,waits=0;IOException expected=Win32(32);
    Exception actual=Capture(delegate{CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;throw expected;},delegate(int milliseconds){waits++;});});
    Assert(Object.ReferenceEquals(actual,expected)&&attempts==1&&waits==0&&!File.Exists(stage)&&File.ReadAllText(target)=="PRIOR GENERATED JOURNAL","Missing stage triggered retry/fallback or changed prior journal.");
   });
   Run(report,"Journal persistence: a stage disappearing during delay stops retry safely",delegate {
    string stage,target;Pair(fixture,out stage,out target);string retained=stage+".retained";int attempts=0,waits=0;IOException expected=Win32(33);
    Exception actual=Capture(delegate{CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;throw expected;},delegate(int milliseconds){waits++;File.Move(stage,retained);});});
    Assert(Object.ReferenceEquals(actual,expected)&&attempts==1&&waits==1&&!File.Exists(stage)&&File.ReadAllText(retained)=="NEW GENERATED JOURNAL"&&File.ReadAllText(target)=="PRIOR GENERATED JOURNAL","Retry continued after operation targets changed.");
   });
   Run(report,"Journal persistence: successful replacement has no retry delay",delegate {
    string stage,target;Pair(fixture,out stage,out target);int attempts=0,waits=0;
    CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){attempts++;File.Replace(source,destination,null);},delegate(int milliseconds){waits++;});
    Assert(attempts==1&&waits==0&&File.ReadAllText(target)=="NEW GENERATED JOURNAL"&&!File.Exists(stage),"Successful atomic operation was retried.");
   });
   Run(report,"Journal persistence: public SaveJournal preserves successive generated receipts",delegate {
    string target=Path.Combine(Folder(fixture),"operation-generated.json");var operation=new OperationReport{Kind="Generated journal fixture"};
    CleanupEngine.SaveJournal(target,operation);operation.ProcessedBytes=12;CleanupEngine.SaveJournal(target,operation);operation.ProcessedBytes=18;CleanupEngine.SaveJournal(target,operation);
    Assert(File.ReadAllText(target).Contains("\"ProcessedBytes\":18")&&!File.Exists(target+".writing"),"Public journal persistence did not promote the latest atomic receipt.");
   });
   return report;
  }
  private static void Transient(string fixture,int code){string stage,target;Pair(fixture,out stage,out target);int attempts=0,waits=0;CleanupEngine.ReplaceJournalWithRetry(stage,target,delegate(string source,string destination){if(++attempts==1)throw Win32(code);File.Replace(source,destination,null);},delegate(int milliseconds){Assert(milliseconds==25,"First bounded retry delay changed.");waits++;Unchanged(stage,target);});Assert(attempts==2&&waits==1&&File.ReadAllText(target)=="NEW GENERATED JOURNAL"&&!File.Exists(stage),"Transient sharing/lock error did not recover.");}
  private static IOException Win32(int code){return new IOException("Injected generated journal failure.",unchecked((int)(0x80070000u|(uint)code)));}
  private static string Folder(string fixture){string folder=Path.Combine(fixture,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);return folder;}
  private static void Pair(string fixture,out string stage,out string target){string folder=Folder(fixture);stage=Path.Combine(folder,"stage.writing");target=Path.Combine(folder,"target.json");File.WriteAllText(stage,"NEW GENERATED JOURNAL");File.WriteAllText(target,"PRIOR GENERATED JOURNAL");}
  private static void Unchanged(string stage,string target){Assert(File.ReadAllText(stage)=="NEW GENERATED JOURNAL"&&File.ReadAllText(target)=="PRIOR GENERATED JOURNAL","Failed replace changed or removed stage/prior journal.");}
  private static Exception Capture(Action action){try{action();}catch(Exception error){return error;}throw new InvalidOperationException("Expected injected replace error.");}
  private static void Run(JournalTestReport report,string name,Action action){try{action();report.Passed++;report.Tests.Add(new JournalTestEntry{Name=name,Status="PASS"});}catch(Exception error){report.Failed++;report.Tests.Add(new JournalTestEntry{Name=name,Status="FAIL",Detail=error.GetType().Name+": "+error.Message});}}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
#if JOURNAL_TEST_MAIN
  public static int Main(){var report=RunTests(Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests"));foreach(var test in report.Tests)Console.WriteLine(test.Status+" | "+test.Name+(test.Detail==null ? "" : " | "+test.Detail));Console.WriteLine("Passed="+report.Passed+" Failed="+report.Failed+" RealUserFilesModified="+report.RealUserFilesModified);return report.Failed==0?0:1;}
#endif
 }
}
