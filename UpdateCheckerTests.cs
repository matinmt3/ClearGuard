using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClearGuard {
 public sealed class UpdateCheckerTestEntry { public string Name {get;set;} public string Status {get;set;} public string Detail {get;set;} }
 public sealed class UpdateCheckerTestReport {
  public int Passed {get;set;} public int Failed {get;set;} public int Skipped {get;set;}
  public bool RealUserFilesModified {get;set;} public int OutboundNetworkRequests {get;set;}
  public List<UpdateCheckerTestEntry> Tests {get;set;}
  public UpdateCheckerTestReport(){Tests=new List<UpdateCheckerTestEntry>();}
 }
 // All fixtures are in-memory. The production fetcher is never called by this suite.
 public static class UpdateCheckerTests {
  private static readonly DateTime Checked=new DateTime(2026,10,8,12,0,0,DateTimeKind.Utc);
  public static UpdateCheckerTestReport RunTests() {
   var report=new UpdateCheckerTestReport();
   Run(report,"Update checker: numeric stable versions detect newer release",delegate {
    var r=UpdateChecker.Evaluate("1.9.9",Json("v1.10.0"),Checked);
    Assert(r.Status==UpdateCheckStatus.UpdateAvailable && r.CurrentVersion=="1.9.9" && r.LatestVersion=="1.10.0","Numeric comparison or canonical versions failed.");
    Assert(r.ReleaseUrl==Url("v1.10.0") && r.CheckedUtc==Checked && r.CheckedUtc.Kind==DateTimeKind.Utc,"Validated release URL or UTC timestamp missing.");
   });
   Run(report,"Update checker: equal stable release is up to date",delegate {
    Assert(UpdateChecker.Evaluate("v1.2.0",Json("v1.2.0"),Checked).Status==UpdateCheckStatus.UpToDate,"Same stable version was not recognized.");
   });
   Run(report,"Update checker: installed version newer than release remains explicit",delegate {
    Assert(UpdateChecker.Evaluate("2.0.0",Json("v1.20.99"),Checked).Status==UpdateCheckStatus.CurrentVersionNewer,"Older public version was called current or newer.");
   });
   Run(report,"Update checker: major minor and patch comparisons are numeric",delegate {
    foreach(string tag in new[]{"v2.0.0","v1.3.0","v1.2.1"})Assert(UpdateChecker.Evaluate("1.2.0",Json(tag),Checked).Status==UpdateCheckStatus.UpdateAvailable,"Newer component was ignored.");
   });
   Run(report,"Update checker: stable tags reject prerelease build leading zeros and overflow",delegate {
    foreach(string tag in new[]{"1.2.0","V1.2.0","v01.2.0","v1.02.0","v1.2.00","v1.2","v1.2.0.0","v1.2.0-beta","v1.2.0+build","v1.2.0 "," v1.2.0","v-1.2.0","v1.2.0\n","v2147483648.2.0","v1.2.99999999999999999999","v۱.۲.۰","v1.2.0/evil"})Invalid(Json(tag));
   });
   Run(report,"Update checker: malformed current version errors before any fetch",delegate {
    int calls=0;var checker=Fake(delegate(CancellationToken t){calls++;return Task.FromResult(new UpdateFetchResponse(200,Json("v1.2.0")));});
    foreach(string version in new[]{null,"","1.2","1.2.0.0","01.2.0","1.2.0-beta","1.2.0+build","1.2.0 ","2147483648.0.0"}){
     var r=checker.CheckAsync(version,CancellationToken.None).GetAwaiter().GetResult();Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="InvalidCurrentVersion" && r.ReleaseUrl==null,"Invalid current version was trusted.");
    }
    Assert(calls==0,"Invalid current version made an outbound request.");
   });
   Run(report,"Update checker: official link guard accepts only canonical matching repository tags",delegate {
    Assert(UpdateChecker.IsOfficialReleaseUrl(Url("v1.2.0")),"Canonical official URL rejected.");
    foreach(string url in BadUrls())Assert(!UpdateChecker.IsOfficialReleaseUrl(url),"Unsafe official-link guard accepted: "+url);
   });
   Run(report,"Update checker: API links cannot escape scheme host repository or tag",delegate {
    foreach(string url in BadUrls())Invalid(Json("v1.2.0",false,false,url));
    Invalid(Json("v1.2.0",false,false,Url("v1.2.1")));
   });
   Run(report,"Update checker: draft and prerelease responses are never stable",delegate {
    Invalid(Json("v1.2.0",true,false));Invalid(Json("v1.2.0",false,true));Invalid(Json("v1.2.0",true,true));
   });
   Run(report,"Update checker: required field types and flags fail closed",delegate {
    foreach(string json in new[]{"{}","{\"tag_name\":\"v1.2.0\",\"html_url\":\""+Url("v1.2.0")+"\"}","{\"tag_name\":42,\"draft\":false,\"prerelease\":false,\"html_url\":\""+Url("v1.2.0")+"\"}",Json("v1.2.0").Replace("\"draft\":false","\"draft\":\"false\""),Json("v1.2.0").Replace("\"prerelease\":false","\"prerelease\":null"),Json("v1.2.0").Replace("\"body\":\"Fixture notes\"","\"body\":{}")})Invalid(json);
   });
   Run(report,"Update checker: malformed root JSON is an error not up to date",delegate {
    foreach(string json in new[]{null,"","{broken","[]","null","true","42","\"text\"",Json("v1.2.0")+" trailing","/*comment*/"+Json("v1.2.0")})Invalid(json);
   });
   Run(report,"Update checker: duplicate security fields are rejected",delegate {
    Invalid(Json("v1.2.0").Replace("\"tag_name\":\"v1.2.0\"","\"tag_name\":\"v9.0.0\",\"tag_name\":\"v1.2.0\""));
    Invalid(Json("v1.2.0").Replace("\"draft\":false","\"draft\":true,\"draft\":false"));
   });
   Run(report,"Update checker: response text and UTF8 byte sizes are bounded",delegate {
    Invalid(new string(' ',UpdateChecker.MaxResponseBytes+1));
    Invalid(Json("v1.2.0",false,false,null,new string('پ',UpdateChecker.MaxResponseBytes/2)));
   });
   Run(report,"Update checker: excessive nesting is rejected without running metadata",delegate {
    Invalid(Json("v1.2.0").TrimEnd('}')+",\"untrusted\":"+new string('[',40)+"0"+new string(']',40)+"}");
   });
   Run(report,"Update checker: notes are bounded inert text with deceptive controls removed",delegate {
    string body="<script>not executable</script>\r\nnormal\ttext\u0000\u0001\u202e\u2066"+new string('x',9000);
    var r=UpdateChecker.Evaluate("1.1.0",Json("v1.2.0",false,false,null,body),Checked);
    Assert(r.Status==UpdateCheckStatus.UpdateAvailable && r.Notes.Length<=UpdateChecker.MaxNotesCharacters,"Release notes were not bounded.");
    Assert(r.Notes.StartsWith("<script>not executable</script>\nnormal\ttext",StringComparison.Ordinal) && r.Notes.IndexOf('\u0000')<0 && r.Notes.IndexOf('\u202e')<0 && r.Notes.IndexOf('\u2066')<0,"Notes controls were retained or plain text was interpreted.");
   });
   Run(report,"Update checker: absent and null release bodies are safe empty notes",delegate {
    Assert(UpdateChecker.ParseReleaseJson(Json("v1.2.0").Replace(",\"body\":\"Fixture notes\"","")).Notes=="","Absent body failed.");
    Assert(UpdateChecker.ParseReleaseJson(Json("v1.2.0").Replace("\"body\":\"Fixture notes\"","\"body\":null")).Notes=="","Null body failed.");
   });
   Run(report,"Update checker: parsed release exposes only validated inert fields",delegate {
    var release=UpdateChecker.ParseReleaseJson(Json("v1.2.0").TrimEnd('}')+",\"assets\":[{\"browser_download_url\":\"https://evil.example/payload.exe\"}],\"__type\":\"Ignored.Type\"}");
    Assert(release.Tag=="v1.2.0" && release.Version=="1.2.0" && release.ReleaseUrl==Url("v1.2.0"),"Unknown API metadata affected trusted fields.");
   });
   Run(report,"Update checker: fixture fetch is invoked only after explicit check",delegate {
    int calls=0;var checker=Fake(delegate(CancellationToken t){calls++;return Task.FromResult(new UpdateFetchResponse(200,Json("v1.2.0")));});
    Assert(calls==0,"Constructing checker performed a request.");
    var r=checker.CheckAsync("1.1.0",CancellationToken.None).GetAwaiter().GetResult();
    Assert(calls==1 && r.Status==UpdateCheckStatus.UpdateAvailable && r.CheckedUtc.Kind==DateTimeKind.Utc,"Explicit fixture check failed.");
   });
   Run(report,"Update checker: transport permits only bounded fixed HTTPS without redirects or credentials",delegate {
    HttpWebRequest request=UpdateChecker.CreateFixedRequest();
    Assert(request.Address.AbsoluteUri=="https://api.github.com/repos/matinmt3/ClearGuard/releases/latest" && request.Method=="GET" && !request.AllowAutoRedirect,"Transport target, method or redirect policy changed.");
    Assert(!String.IsNullOrWhiteSpace(request.UserAgent) && request.UserAgent.IndexOf("ClearGuard",StringComparison.Ordinal)>=0,"GitHub User-Agent missing.");
    Assert(!request.UseDefaultCredentials && request.Credentials==null && !request.PreAuthenticate && request.CookieContainer==null && request.ServerCertificateValidationCallback==null,"Transport leaked credentials or bypassed certificates.");
    Assert(request.Timeout==10000 && request.ReadWriteTimeout==10000 && request.MaximumResponseHeadersLength==16 && request.AutomaticDecompression==DecompressionMethods.None,"Transport resource bounds changed.");
   });
   Run(report,"Update checker: strict JSON rejects extensions escaped duplicate keys and invalid Unicode",delegate {
    foreach(string extra in new[]{"+1","01","NaN","Infinity","1.","1e","[0,]","{\"x\":0,}"})Invalid(Json("v1.2.0").TrimEnd('}')+",\"extra\":"+extra+"}");
    Invalid(Json("v1.2.0").Replace("\"body\":\"Fixture notes\"","\"body\":\"\\ud800\""));
    Invalid(Json("v1.2.0").Replace("\"body\":\"Fixture notes\"","\"body\":\"\\udc00\""));
    Invalid(Json("v1.2.0").Replace("\"draft\":false","\"draft\":true,\"dr\\u0061ft\":false"));
   });
   Run(report,"Update checker: Persian half spaces and valid Unicode pairs remain plain text",delegate {
    string body="نسخهٔ نیم\u200cفاصله \ud83d\ude00";var r=UpdateChecker.ParseReleaseJson(Json("v1.2.0",false,false,null,body));
    Assert(r.Notes==body,"Persian or valid supplementary Unicode was corrupted.");
    var bounded=UpdateChecker.ParseReleaseJson(Json("v1.2.0",false,false,null,new string('x',UpdateChecker.MaxNotesCharacters-1)+"\ud83d\ude00"));
    Assert(bounded.Notes.Length==UpdateChecker.MaxNotesCharacters-1,"Notes truncation produced a dangling surrogate.");
   });
   Run(report,"Update checker: 403 and 429 show a visible rate-limit error",delegate {
    foreach(int code in new[]{403,429}){var r=Response(code,"private error body");Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="RateLimited" && r.Message.Length>0 && r.ReleaseUrl==null && r.LatestVersion==null,"Rate limit incorrectly meant up to date.");}
   });
   Run(report,"Update checker: HTTP failures and empty responses do not masquerade as success",delegate {
    foreach(int code in new[]{400,401,404,500,503}){var r=Response(code,Json("v1.2.0"));Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="HttpError" && r.ReleaseUrl==null,"HTTP failure leaked a trusted release.");}
    Assert(Response(200,"").ErrorCode=="InvalidResponse","Empty HTTP success was trusted.");
   });
   Run(report,"Update checker: redirects are never accepted as release metadata",delegate {
    foreach(int code in new[]{301,302,303,307,308}){var r=Response(code,Json("v1.2.0"));Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="RedirectBlocked" && r.ReleaseUrl==null,"Redirect response accepted.");}
   });
   Run(report,"Update checker: offline boundary errors stay friendly and omit exception details",delegate {
    var checker=Fake(delegate(CancellationToken t){throw new IOException("SECRET_INTERNAL_PATH_TOKEN");});
    var r=checker.CheckAsync("1.2.0",CancellationToken.None).GetAwaiter().GetResult();
    Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="NetworkError" && !r.Message.Contains("SECRET") && r.Message.Length>0 && r.ReleaseUrl==null,"Network error crashed or exposed internals.");
   });
   Run(report,"Update checker: a null boundary response is a visible error",delegate {
    var checker=Fake(delegate(CancellationToken t){return Task.FromResult<UpdateFetchResponse>(null);});
    Assert(checker.CheckAsync("1.2.0",CancellationToken.None).GetAwaiter().GetResult().ErrorCode=="InvalidResponse","Null boundary response crashed.");
   });
   Run(report,"Update checker: canceled check never calls the fetch boundary",delegate {
    int calls=0;var checker=Fake(delegate(CancellationToken t){calls++;return Task.FromResult(new UpdateFetchResponse(200,Json("v1.2.0")));});
    var r=checker.CheckAsync("1.2.0",new CancellationToken(true)).GetAwaiter().GetResult();
    Assert(calls==0 && r.Status==UpdateCheckStatus.Cancelled && r.ErrorCode=="Cancelled" && r.ReleaseUrl==null,"Canceled request fetched or claimed success.");
   });
   Run(report,"Update checker: cancellation bounds even a stalled injected fetch",delegate {
    using(var cancellation=new CancellationTokenSource()){
     var never=new TaskCompletionSource<UpdateFetchResponse>();var checker=Fake(delegate(CancellationToken t){return never.Task;});
     var task=checker.CheckAsync("1.2.0",cancellation.Token);cancellation.Cancel();
     Assert(task.Wait(2000),"Cancellation waited on a stalled fetch.");Assert(task.Result.Status==UpdateCheckStatus.Cancelled && task.Result.ReleaseUrl==null,"Canceled stalled fetch retained a result.");
    }
   });
   Run(report,"Update checker: stalled fetch timeout is bounded and visible",delegate {
    var never=new TaskCompletionSource<UpdateFetchResponse>();var checker=new UpdateChecker(delegate(CancellationToken t){return never.Task;},30);
    var task=checker.CheckAsync("1.2.0",CancellationToken.None);
    Assert(task.Wait(2000),"Timeout did not bound stalled fetch.");Assert(task.Result.Status==UpdateCheckStatus.Error && task.Result.ErrorCode=="Timeout" && task.Result.ReleaseUrl==null,"Timeout was called up to date.");
   });
   Run(report,"Update checker: hostile successful payloads are error results",delegate {
    var r=Response(200,Json("v1.2.0",false,false,"file:///C:/payload.exe"));
    Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="InvalidResponse" && r.ReleaseUrl==null && r.LatestVersion==null,"Hostile successful payload affected result.");
   });
   Run(report,"Update checker: validated URL guard rejects null and mismatched versions",delegate {
    Assert(!UpdateChecker.IsOfficialReleaseUrl(null) && !UpdateChecker.IsOfficialReleaseUrl(""),"Missing URL accepted.");
    Assert(!UpdateChecker.IsOfficialReleaseUrl(Url("v1.2.0-beta")),"Unstable URL accepted.");
   });
   Run(report,"Update checker: UTC evaluation rejects local or unspecified check timestamps",delegate {
    var r=UpdateChecker.Evaluate("1.2.0",Json("v1.2.0"),DateTime.SpecifyKind(Checked,DateTimeKind.Unspecified));
    Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="InvalidTimestamp" && r.ReleaseUrl==null && r.CheckedUtc==new DateTime(0,DateTimeKind.Utc),"Unspecified timestamp was presented as UTC or pure evaluation read the clock.");
   });
   Run(report,"Update checker: zero and maximum supported canonical versions compare safely",delegate {
    Assert(UpdateChecker.Evaluate("0.0.0",Json("v0.0.1"),Checked).Status==UpdateCheckStatus.UpdateAvailable,"Zero semantic version failed.");
    Assert(UpdateChecker.Evaluate("2147483647.0.0",Json("v2147483647.0.0"),Checked).Status==UpdateCheckStatus.UpToDate,"Bounded maximum version failed.");
   });
   Run(report,"Strict JSON helper: escaped duplicate keys at nested depths are rejected",delegate {
    foreach(string json in new[]{"{\"outer\":{\"name\":1,\"na\\u006de\":2}}","{\"Rows\":[{\"IsComplete\":false,\"IsC\\u006fmplete\":true}]}","{\"Folders\":[],\"F\\u006flders\":[]}",null}) {
     bool rejected=false;try{UpdateChecker.ValidateStrictJson(json);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Strict helper accepted a missing payload or duplicate decoded key.");
    }
   });
   Run(report,"Strict JSON helper: valid snapshot metadata has no updater response limit",delegate {
    UpdateChecker.ValidateStrictJson("{\"SchemaVersion\":1,\"RootPath\":\"C:\\\\fixture\",\"Rows\":[{\"ObservedBytes\":42,\"IsComplete\":true,\"Note\":\"<script>inert metadata</script>\"}],\"Warnings\":[]}");
    UpdateChecker.ValidateStrictJson("{\"notes\":\""+new string('x',UpdateChecker.MaxResponseBytes+1)+"\"}");
   });
   return report;
  }
  private static UpdateChecker Fake(Func<CancellationToken,Task<UpdateFetchResponse>> fetch){return new UpdateChecker(fetch);}
  private static UpdateCheckResult Response(int code,string body){return Fake(delegate(CancellationToken t){return Task.FromResult(new UpdateFetchResponse(code,body));}).CheckAsync("1.2.0",CancellationToken.None).GetAwaiter().GetResult();}
  private static string Url(string tag){return "https://github.com/matinmt3/ClearGuard/releases/tag/"+tag;}
  private static string[] BadUrls(){return new[]{"http://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0","https://evil.example/matinmt3/ClearGuard/releases/tag/v1.2.0","https://github.com.evil.example/matinmt3/ClearGuard/releases/tag/v1.2.0","https://github.com@evil.example/matinmt3/ClearGuard/releases/tag/v1.2.0","https://user@github.com/matinmt3/ClearGuard/releases/tag/v1.2.0","https://github.com:443/matinmt3/ClearGuard/releases/tag/v1.2.0","https://github.com/matinmt3/Other/releases/tag/v1.2.0","https://github.com/other/ClearGuard/releases/tag/v1.2.0","https://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0?next=evil","https://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0#fragment","https://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0/","https://github.com/matinmt3/ClearGuard/releases/tag/%761.2.0","https://github.com/matinmt3/ClearGuard/releases/tag/../v1.2.0","https://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0\\evil","https://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0\n","HTTPS://github.com/matinmt3/ClearGuard/releases/tag/v1.2.0","https://github.com/matinmt3/clearguard/releases/tag/v1.2.0","file:///C:/payload.exe","javascript:alert(1)","\\\\server\\payload.exe","https://127.0.0.1/releases/tag/v1.2.0"};}
  private static string Json(string tag,bool draft=false,bool prerelease=false,string url=null,string body="Fixture notes") {
   return new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"tag_name",tag},{"draft",draft},{"prerelease",prerelease},{"html_url",url??Url(tag)},{"body",body}});
  }
  private static void Invalid(string json){var r=UpdateChecker.Evaluate("1.2.0",json,Checked);Assert(r.Status==UpdateCheckStatus.Error && r.ErrorCode=="InvalidResponse" && r.ReleaseUrl==null && r.LatestVersion==null,"Untrusted response was not fail-closed.");bool thrown=false;try{UpdateChecker.ParseReleaseJson(json);}catch(InvalidDataException){thrown=true;}Assert(thrown,"Pure parser did not reject malformed response.");}
  private static void Run(UpdateCheckerTestReport report,string name,Action test){try{test();report.Passed++;report.Tests.Add(new UpdateCheckerTestEntry{Name=name,Status="PASS"});}catch(Exception e){report.Failed++;report.Tests.Add(new UpdateCheckerTestEntry{Name=name,Status="FAIL",Detail=e.GetType().Name+": "+e.Message});}}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
#if UPDATE_CHECKER_TEST_MAIN
  public static int Main(){var r=RunTests();foreach(var test in r.Tests)Console.WriteLine(test.Status+" | "+test.Name+(test.Detail==null ? "" : " | "+test.Detail));Console.WriteLine("Passed="+r.Passed+" Failed="+r.Failed+" NetworkRequests="+r.OutboundNetworkRequests+" RealUserFilesModified="+r.RealUserFilesModified);return r.Failed==0?0:1;}
#endif
 }
}
