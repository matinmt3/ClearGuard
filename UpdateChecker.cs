using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace ClearGuard {
 public enum UpdateCheckStatus {UpdateAvailable,UpToDate,CurrentVersionNewer,Error,Cancelled}
 public sealed class UpdateCheckResult {
  public string CurrentVersion{get;internal set;} public string LatestVersion{get;internal set;} public UpdateCheckStatus Status{get;internal set;}
  public string Notes{get;internal set;} public string ReleaseUrl{get;internal set;} public DateTime CheckedUtc{get;internal set;}
  public string Message{get;internal set;} public string ErrorCode{get;internal set;}
 }
 public sealed class UpdateRelease {public string Tag{get;internal set;}public string Version{get;internal set;}public string Notes{get;internal set;}public string ReleaseUrl{get;internal set;}}
 public sealed class UpdateFetchResponse {public int StatusCode{get;private set;} public string Json{get;private set;}public UpdateFetchResponse(int statusCode,string json){StatusCode=statusCode;Json=json;}}
 public sealed class UpdateChecker {
  public const string LatestReleaseApiUrl="https://api.github.com/repos/matinmt3/ClearGuard/releases/latest";
  private const string OfficialReleasePrefix="https://github.com/matinmt3/ClearGuard/releases/tag/";
  public const int MaxResponseBytes=131072;
  public const int MaxNotesCharacters=4000;
  public const int TimeoutMilliseconds=10000;
  private static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);
  private static readonly Regex StableVersionPattern=new Regex(@"\A(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})\z",RegexOptions.CultureInvariant);
  private readonly Func<CancellationToken,Task<UpdateFetchResponse>> fetch;
  private readonly int timeoutMilliseconds;

  // Construction has no network, timer, background task, or telemetry side effects.
  public UpdateChecker():this(FetchLatestReleaseAsync,TimeoutMilliseconds){}
  public UpdateChecker(Func<CancellationToken,Task<UpdateFetchResponse>> fetch):this(fetch,TimeoutMilliseconds){}
  internal UpdateChecker(Func<CancellationToken,Task<UpdateFetchResponse>> fetch,int timeoutMilliseconds) {
   if(fetch==null)throw new ArgumentNullException("fetch");
   if(timeoutMilliseconds<1 || timeoutMilliseconds>TimeoutMilliseconds)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
   this.fetch=fetch;this.timeoutMilliseconds=timeoutMilliseconds;
  }

  // Call only in response to the user's explicit manual Check action. No startup/background polling.
  public async Task<UpdateCheckResult> CheckAsync(string currentVersion,CancellationToken token) {
   if(token.IsCancellationRequested)return Error(currentVersion,"Cancelled",UpdateCheckStatus.Cancelled,DateTime.UtcNow);
   Version current;
   if(!TryVersion(currentVersion,false,out current))return Error(currentVersion,"InvalidCurrentVersion",UpdateCheckStatus.Error,DateTime.UtcNow);
   string canonical=current.ToString(3);
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
    deadline.CancelAfter(timeoutMilliseconds);
    Task<UpdateFetchResponse> fetchTask=null;
    try {
     deadline.Token.ThrowIfCancellationRequested();
     fetchTask=fetch(deadline.Token);
     if(fetchTask==null)return Error(canonical,"InvalidResponse",UpdateCheckStatus.Error,DateTime.UtcNow);
     // Bound completion even if an injected/network boundary fails to observe cancellation.
     Task stop=Task.Delay(Timeout.Infinite,deadline.Token);
     await Task.WhenAny(fetchTask,stop).ConfigureAwait(false);
     deadline.Token.ThrowIfCancellationRequested();
     UpdateFetchResponse response=await fetchTask.ConfigureAwait(false);
     deadline.Token.ThrowIfCancellationRequested();
     if(response==null)return Error(canonical,"InvalidResponse",UpdateCheckStatus.Error,DateTime.UtcNow);
     if(response.StatusCode>=300 && response.StatusCode<400)return Error(canonical,"RedirectBlocked",UpdateCheckStatus.Error,DateTime.UtcNow);
     if(response.StatusCode==403 || response.StatusCode==429)return Error(canonical,"RateLimited",UpdateCheckStatus.Error,DateTime.UtcNow);
     if(response.StatusCode!=200)return Error(canonical,"HttpError",UpdateCheckStatus.Error,DateTime.UtcNow);
     UpdateCheckResult evaluated=Evaluate(canonical,response.Json,DateTime.UtcNow);
     deadline.Token.ThrowIfCancellationRequested();
     return evaluated;
    }
    catch(OperationCanceledException) {
     return Error(canonical,token.IsCancellationRequested ? "Cancelled" : "Timeout",token.IsCancellationRequested ? UpdateCheckStatus.Cancelled : UpdateCheckStatus.Error,DateTime.UtcNow);
    }
    catch(InvalidDataException) {return Error(canonical,"InvalidResponse",UpdateCheckStatus.Error,DateTime.UtcNow);}
    catch(Exception) {
     if(deadline.IsCancellationRequested)return Error(canonical,token.IsCancellationRequested ? "Cancelled" : "Timeout",token.IsCancellationRequested ? UpdateCheckStatus.Cancelled : UpdateCheckStatus.Error,DateTime.UtcNow);
     return Error(canonical,"NetworkError",UpdateCheckStatus.Error,DateTime.UtcNow);
    }
    finally {
     // End the unused delay and ensure a late failed boundary does not become an unobserved exception.
     deadline.Cancel();
     if(fetchTask!=null)ObserveLateFault(fetchTask);
    }
   }
  }

  public static UpdateCheckResult Evaluate(string currentVersion,string json,DateTime checkedUtc) {
   if(checkedUtc.Kind!=DateTimeKind.Utc)return Error(currentVersion,"InvalidTimestamp",UpdateCheckStatus.Error,new DateTime(0,DateTimeKind.Utc));
   Version current;
   if(!TryVersion(currentVersion,false,out current))return Error(currentVersion,"InvalidCurrentVersion",UpdateCheckStatus.Error,checkedUtc);
   string canonical=current.ToString(3);
   try {
    UpdateRelease release=ParseReleaseJson(json);Version latest;
    if(!TryVersion(release.Tag,true,out latest))return Error(canonical,"InvalidResponse",UpdateCheckStatus.Error,checkedUtc);
    int comparison=latest.CompareTo(current);
    UpdateCheckStatus status=comparison>0 ? UpdateCheckStatus.UpdateAvailable : comparison==0 ? UpdateCheckStatus.UpToDate : UpdateCheckStatus.CurrentVersionNewer;
    string message=status==UpdateCheckStatus.UpdateAvailable ? "نسخهٔ پایدار جدید موجود است؛ دریافت و نصب فقط به‌صورت دستی انجام می‌شود." : status==UpdateCheckStatus.UpToDate ? "نسخهٔ نصب‌شده با آخرین انتشار پایدار برابر است." : "نسخهٔ نصب‌شده از آخرین انتشار پایدار مخزن جدیدتر است.";
    return new UpdateCheckResult{CurrentVersion=canonical,LatestVersion=release.Version,Status=status,Notes=release.Notes,ReleaseUrl=release.ReleaseUrl,CheckedUtc=checkedUtc,Message=message};
   }
   catch(InvalidDataException){return Error(canonical,"InvalidResponse",UpdateCheckStatus.Error,checkedUtc);}
  }

  public static UpdateRelease ParseReleaseJson(string json) {
   try {
    if(String.IsNullOrWhiteSpace(json) || json.Length>MaxResponseBytes || StrictUtf8.GetByteCount(json)>MaxResponseBytes)throw InvalidPayload();
    // A bounded strict parser avoids type resolvers, duplicate-key ambiguity and permissive JSON.
    var root=new JsonReader(json).Read() as Dictionary<string,object>;
    if(root==null)throw InvalidPayload();
    object tagValue,urlValue,draftValue,prereleaseValue,bodyValue;
    if(!root.TryGetValue("tag_name",out tagValue) || !(tagValue is string) || !root.TryGetValue("html_url",out urlValue) || !(urlValue is string) || !root.TryGetValue("draft",out draftValue) || !(draftValue is bool) || !root.TryGetValue("prerelease",out prereleaseValue) || !(prereleaseValue is bool))throw InvalidPayload();
    Version version;string tag=(string)tagValue;
    if(!TryVersion(tag,true,out version) || (bool)draftValue || (bool)prereleaseValue)throw InvalidPayload();
    string official=OfficialReleasePrefix+tag;
    if(!String.Equals(urlValue as string,official,StringComparison.Ordinal) || !IsOfficialReleaseUrl(official))throw InvalidPayload();
    string body="";
    if(root.TryGetValue("body",out bodyValue) && bodyValue!=null){if(!(bodyValue is string))throw InvalidPayload();body=(string)bodyValue;}
    return new UpdateRelease{Tag=tag,Version=version.ToString(3),Notes=InertNotes(body),ReleaseUrl=official};
   }
   catch(InvalidDataException){throw;}
   catch(EncoderFallbackException){throw InvalidPayload();}
   catch(ArgumentException){throw InvalidPayload();}
  }

  public static bool IsOfficialReleaseUrl(string url) {
   if(url==null || !url.StartsWith(OfficialReleasePrefix,StringComparison.Ordinal))return false;
   Version version;return TryVersion(url.Substring(OfficialReleasePrefix.Length),true,out version);
  }

  // Shared local-input syntax guard. Each caller must enforce its own byte/length budget
  // before calling; the updater's smaller network response limit intentionally does not apply.
  internal static void ValidateStrictJson(string json) {
   if(json==null)throw InvalidPayload();
   new JsonReader(json).Read();
  }

  private static bool TryVersion(string raw,bool requireTag,out Version version) {
   version=null;if(String.IsNullOrEmpty(raw) || raw.Length>34)return false;
   bool tagged=raw[0]=='v';if(requireTag && !tagged)return false;
   string value=tagged ? raw.Substring(1) : raw;
   Match match=StableVersionPattern.Match(value);if(!match.Success)return false;
   int major,minor,patch;
   if(!Int32.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out major) || !Int32.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out minor) || !Int32.TryParse(match.Groups[3].Value,NumberStyles.None,CultureInfo.InvariantCulture,out patch))return false;
   version=new Version(major,minor,patch);return true;
  }

  private static string InertNotes(string value) {
   var text=new StringBuilder(Math.Min(value.Length,MaxNotesCharacters));
   for(int i=0;i<value.Length && text.Length<MaxNotesCharacters;i++) {
    char c=value[i];
    if(c=='\r'){if(i+1<value.Length && value[i+1]=='\n')i++;c='\n';}
    if((Char.IsControl(c) && c!='\n' && c!='\t') || (Char.GetUnicodeCategory(c)==UnicodeCategory.Format && c!='\u200c' && c!='\u200d'))continue;
    if(Char.IsHighSurrogate(c)) {
     if(i+1<value.Length && Char.IsLowSurrogate(value[i+1])){if(text.Length+2>MaxNotesCharacters)break;text.Append(c);text.Append(value[++i]);}
     continue;
    }
    if(Char.IsLowSurrogate(c))continue;
    text.Append(c);
   }
   // This remains plain text, including Markdown/HTML characters; the UI must assign Text only.
   return text.ToString();
  }

  private static UpdateCheckResult Error(string current,string code,UpdateCheckStatus status,DateTime checkedUtc) {
   Version version;string canonical=TryVersion(current,false,out version) ? version.ToString(3) : "";
   string message;
   switch(code) {
    case "Cancelled":message="بررسی نسخه لغو شد؛ هیچ فایلی دریافت یا نصب نشد.";break;
    case "Timeout":message="مهلت بررسی نسخه تمام شد. اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.";break;
    case "RateLimited":message="GitHub بررسی نسخه را محدود کرده است (مجوز یا سقف درخواست). کمی بعد دوباره تلاش کنید.";break;
    case "RedirectBlocked":message="GitHub پاسخ تغییرمسیر داد؛ برای ایمنی، مسیر دیگری باز نشد.";break;
    case "InvalidCurrentVersion":message="شمارهٔ نسخهٔ نصب‌شده معتبر نیست؛ مقایسه انجام نشد.";break;
    case "InvalidResponse":message="پاسخ انتشار GitHub معتبر یا کامل نیست؛ جدیدترین نسخه تأیید نشد.";break;
    case "HttpError":message="GitHub اطلاعات انتشار را برنگرداند. کمی بعد دوباره تلاش کنید.";break;
    case "InvalidTimestamp":message="زمان بررسی معتبر نیست؛ مقایسه انجام نشد.";break;
    default:message="ارتباط امن با GitHub برقرار نشد. اینترنت یا دسترسی شبکه را بررسی کنید.";break;
   }
   return new UpdateCheckResult{CurrentVersion=canonical,Status=status,CheckedUtc=checkedUtc,Message=message,ErrorCode=code,Notes=""};
  }

  private static async Task<UpdateFetchResponse> FetchLatestReleaseAsync(CancellationToken token) {
   token.ThrowIfCancellationRequested();
   // .NET Framework HttpWebRequest has no per-request TLS protocol selection. This is set
   // only after an explicit Check, not at startup. Normal Windows certificate validation stays on.
   if(ServicePointManager.ServerCertificateValidationCallback!=null)throw new InvalidOperationException("Default TLS validation is required.");
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   HttpWebRequest request=CreateFixedRequest();
   using(token.Register(delegate{request.Abort();})) {
    HttpWebResponse response;
    try {response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);}
    catch(WebException exception) {
     token.ThrowIfCancellationRequested();
     response=exception.Response as HttpWebResponse;
     if(response==null)throw;
    }
    using(response) {
     token.ThrowIfCancellationRequested();
     int status=(int)response.StatusCode;
     // Error/redirect bodies and Location values are neither read nor followed.
     if(status!=200)return new UpdateFetchResponse(status,null);
     if(response.ResponseUri==null || !String.Equals(response.ResponseUri.AbsoluteUri,LatestReleaseApiUrl,StringComparison.Ordinal))throw InvalidPayload();
     if(response.ContentLength>MaxResponseBytes || !String.IsNullOrEmpty(response.ContentEncoding))throw InvalidPayload();
     string media=(response.ContentType??"").Split(';')[0].Trim();
     if(!String.Equals(media,"application/json",StringComparison.OrdinalIgnoreCase) && !String.Equals(media,"application/vnd.github+json",StringComparison.OrdinalIgnoreCase))throw InvalidPayload();
     using(Stream input=response.GetResponseStream())using(var bytes=new MemoryStream()) {
      if(input==null)throw InvalidPayload();
      var buffer=new byte[4096];int total=0;
      while(true) {
       token.ThrowIfCancellationRequested();
       int count=await input.ReadAsync(buffer,0,Math.Min(buffer.Length,MaxResponseBytes-total+1),token).ConfigureAwait(false);
       if(count==0)break;
       total+=count;if(total>MaxResponseBytes)throw InvalidPayload();
       bytes.Write(buffer,0,count);
      }
      token.ThrowIfCancellationRequested();
      try{return new UpdateFetchResponse(status,StrictUtf8.GetString(bytes.ToArray()));}
      catch(DecoderFallbackException){throw InvalidPayload();}
     }
    }
   }
  }

  // Exposed only within this assembly for a no-network transport-policy test.
  internal static HttpWebRequest CreateFixedRequest() {
   var request=(HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
   request.Method="GET";request.AllowAutoRedirect=false;
   request.UserAgent="ClearGuard-Manual-Update-Checker/1.2.0";
   request.Accept="application/vnd.github+json";
   request.Timeout=TimeoutMilliseconds;request.ReadWriteTimeout=TimeoutMilliseconds;
   request.MaximumResponseHeadersLength=16;
   request.UseDefaultCredentials=false;request.Credentials=null;request.PreAuthenticate=false;
   request.CookieContainer=null;request.AutomaticDecompression=DecompressionMethods.None;
   return request;
  }

  private static void ObserveLateFault(Task<UpdateFetchResponse> task){task.ContinueWith(delegate(Task<UpdateFetchResponse> failed){var observed=failed.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
  private static InvalidDataException InvalidPayload(){return new InvalidDataException("Invalid stable-release payload.");}

  // Minimal strict JSON reader. No CLR type loading, dynamic code, network, or side effects.
  private sealed class JsonReader {
   private readonly string json;private int position;
   private static readonly object NumberValue=new object();
   internal JsonReader(string json){this.json=json;}
   internal object Read(){object value=Value(0);Space();if(position!=json.Length)throw InvalidPayload();return value;}
   private object Value(int depth) {
    if(depth>16)throw InvalidPayload();Space();if(position>=json.Length)throw InvalidPayload();char c=json[position];
    if(c=='{')return Object(depth+1);if(c=='[')return Array(depth+1);if(c=='"')return String();
    if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
    if(c=='-' || (c>='0' && c<='9')){Number();return NumberValue;}throw InvalidPayload();
   }
   private Dictionary<string,object> Object(int depth) {
    position++;Space();var fields=new Dictionary<string,object>(StringComparer.Ordinal);
    if(Take('}'))return fields;
    while(true) {
     Space();if(position>=json.Length || json[position]!='"')throw InvalidPayload();string key=String();
     Space();if(!Take(':') || fields.ContainsKey(key))throw InvalidPayload();fields.Add(key,Value(depth));
     Space();if(Take('}'))return fields;if(!Take(','))throw InvalidPayload();
    }
   }
   private List<object> Array(int depth) {
    position++;Space();var values=new List<object>();if(Take(']'))return values;
    while(true){values.Add(Value(depth));Space();if(Take(']'))return values;if(!Take(','))throw InvalidPayload();}
   }
   private string String() {
    if(!Take('"'))throw InvalidPayload();var value=new StringBuilder();
    while(position<json.Length) {
     char c=json[position++];if(c=='"'){string result=value.ToString();ValidateUnicode(result);return result;}if(c<' ')throw InvalidPayload();
     if(c!='\\'){value.Append(c);continue;}
     if(position>=json.Length)throw InvalidPayload();char escaped=json[position++];
     switch(escaped) {
      case '"':case '\\':case '/':value.Append(escaped);break;
      case 'b':value.Append('\b');break;case 'f':value.Append('\f');break;case 'n':value.Append('\n');break;case 'r':value.Append('\r');break;case 't':value.Append('\t');break;
      case 'u':
       if(position+4>json.Length)throw InvalidPayload();int code=0;
       for(int i=0;i<4;i++){char digit=json[position++];int n=digit>='0'&&digit<='9' ? digit-'0' : digit>='a'&&digit<='f' ? digit-'a'+10 : digit>='A'&&digit<='F' ? digit-'A'+10 : -1;if(n<0)throw InvalidPayload();code=(code<<4)|n;}
       value.Append((char)code);break;
      default:throw InvalidPayload();
     }
    }
    throw InvalidPayload();
   }
   private static void ValidateUnicode(string value){for(int i=0;i<value.Length;i++){char c=value[i];if(Char.IsHighSurrogate(c)){if(i+1>=value.Length || !Char.IsLowSurrogate(value[++i]))throw InvalidPayload();}else if(Char.IsLowSurrogate(c))throw InvalidPayload();}}
   private void Number() {
    Take('-');if(position>=json.Length)throw InvalidPayload();
    if(Take('0')){if(position<json.Length && Digit(json[position]))throw InvalidPayload();}
    else{if(json[position]<'1' || json[position]>'9')throw InvalidPayload();while(position<json.Length && Digit(json[position]))position++;}
    if(Take('.')){int start=position;while(position<json.Length && Digit(json[position]))position++;if(start==position)throw InvalidPayload();}
    if(position<json.Length && (json[position]=='e' || json[position]=='E')){position++;if(position<json.Length && (json[position]=='+' || json[position]=='-'))position++;int start=position;while(position<json.Length && Digit(json[position]))position++;if(start==position)throw InvalidPayload();}
   }
   private static bool Digit(char c){return c>='0' && c<='9';}
   private void Literal(string literal){for(int i=0;i<literal.Length;i++){if(position>=json.Length || json[position++]!=literal[i])throw InvalidPayload();}}
   private bool Take(char c){if(position<json.Length && json[position]==c){position++;return true;}return false;}
   private void Space(){while(position<json.Length){char c=json[position];if(c!=' ' && c!='\t' && c!='\r' && c!='\n')break;position++;}}
  }
 }
}
