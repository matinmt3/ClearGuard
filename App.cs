using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.ComponentModel;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ClearGuard {
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   try {
    if(args.Length>=3&&args[0]=="--self-test") return SafetyTests.Run(args[1],args[2]);
    if(args.Length>=2&&args[0]=="--check-updates"){Version v=Assembly.GetExecutingAssembly().GetName().Version;Format.SaveJson(args[1],new UpdateChecker().CheckAsync(v.ToString(3),CancellationToken.None).GetAwaiter().GetResult());return 0;}
    if(args.Length>=3&&args[0]=="--scan-inventory"){Format.SaveJson(args[2],InventoryScanner.Scan(args[1],CancellationToken.None,null));return 0;}
    if(args.Length>=2&&args[0]=="--scan-apps"){Format.SaveJson(args[1],InstalledAppsScanner.Scan(CancellationToken.None,null));return 0;}
    if(args.Length>=2&&(args[0]=="--scan-caches"||args[0]=="--scan-desktop")) {
     ScanContext ctx=ScanContext.Current(); ScanResult scan=args[0]=="--scan-caches"?CacheScanner.Scan(ctx,CancellationToken.None,delegate(string s){}):DesktopScanner.Scan(ctx,CancellationToken.None,delegate(string s){});
     Format.SaveJson(args[1],scan); return 0;
    }
    Application application=new Application();
    using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearGuard.MainWindow.xaml")) {
     Window window=(Window)XamlReader.Load(s); MainController controller=new MainController(window);
     if(args.Length>=3&&args[0]=="--render-scan"){var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};ScanResult scan=j.Deserialize<ScanResult>(File.ReadAllText(args[1]));controller.PreviewScan(scan);Render(window,args[2]);return 0;}
     if(args.Length>=2&&args[0]=="--ui-smoke") {
      Directory.CreateDirectory(args[1]);var states=new List<string>();foreach(string name in new[]{"NavHome","NavCache","NavInventory","NavApps","NavSources","NavOrganize","NavHistory","NavSafety","NavDashboard","NavProtected","NavUpdates"}){((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render(window,Path.Combine(args[1],name+".png"));states.Add(name+":PASS");}Format.SaveJson(Path.Combine(args[1],"ui-smoke.json"),new{Pages=states,DestructiveOperations=0,Width=(int)window.ActualWidth,Height=(int)window.ActualHeight});return FullQaHarness.Run(window,controller,args[1]);
     }
     if(args.Length>=2&&args[0]=="--ui-checks")return FullQaHarness.Run(window,controller,args[1]);
     if(args.Length>=3&&args[0]=="--render-apps"){var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};controller.PreviewApps(j.Deserialize<InstalledAppsResult>(File.ReadAllText(args[1])));Render(window,args[2],true);return 0;}
     if(args.Length>=3&&args[0]=="--render-dashboard"){var j=new JavaScriptSerializer{MaxJsonLength=1024*1024};var preview=j.Deserialize<DashboardPreview>(File.ReadAllText(args[1]));controller.PreviewDashboard(preview.Snapshot,preview.Comparison);Render(window,args[2],true);return 0;}
     if(args.Length>=3&&args[0]=="--render-updates"){controller.PreviewUpdates(UpdateChecker.Evaluate(Assembly.GetExecutingAssembly().GetName().Version.ToString(3),File.ReadAllText(args[1]),DateTime.UtcNow));Render(window,args[2],true);return 0;}
     if(args.Length>=2&&args[0]=="--render") {
      Render(window,args[1],true);return 0;
     }
     application.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e){MessageBox.Show("عملیات متوقف شد و موارد نامطمئن حذف نشدند.\n"+e.Exception.Message,"ClearGuard",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
     application.Run(window);return 0;
    }
   } catch(Exception e) {try {string output=args.Length>=2?args[args.Length-1]+".error.txt":Path.Combine(Path.GetTempPath(),"ClearGuard-startup-error.txt");File.WriteAllText(output,e.ToString());}catch{} if(args.Length==0)MessageBox.Show(e.Message,"ClearGuard",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
  }
  private static void Render(Window window,string path,bool syntheticDisk=false){
      if(syntheticDisk){((TextBlock)window.FindName("FreeValue")).Text="64.00 GiB";((ProgressBar)window.FindName("DiskBar")).Value=25;((TextBlock)window.FindName("DiskCaption")).Text="نمونهٔ مصنوعی: 64 GiB آزاد از 256 GiB";}
      window.ShowActivated=false;window.ShowInTaskbar=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;window.Top=-10000;if(!window.IsVisible)window.Show();
      window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate{}));
      window.UpdateLayout();
      RenderTargetBitmap bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);PngBitmapEncoder enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream f=File.Create(path))enc.Save(f);
  }
 }
 public class MainController {
  private Window window;
  private ScanContext context=ScanContext.Current();
  private ScanResult current;
  private ObservableCollection<ScanRow> rows=new ObservableCollection<ScanRow>();
  private CancellationTokenSource cancellation;
  private bool busy;
  private bool closeRequested;
  private InstalledAppsResult installedCurrent;
  private ObservableCollection<InstalledAppRow> installedRows=new ObservableCollection<InstalledAppRow>();
  private UpdateChecker updateChecker=new UpdateChecker();
  private UpdateCheckResult updateResult;
  private SpaceSnapshot dashboardSnapshot;
  private SpaceDashboardComparison dashboardComparison;
  private Func<string,bool> confirmProtectionRemoval;
  private string section="home";
  private List<HistoryItem> histories=new List<HistoryItem>();
  private Dictionary<string,ScanResult> lastScans=new Dictionary<string,ScanResult>();
  private DateTime lastProgress=DateTime.MinValue;
  private T Get<T>(string name) where T:class{return window.FindName(name) as T;}
  private void Bind(string name,Action action){Get<Button>(name).Click+=delegate{action();};}
  public MainController(Window w):this(w,ScanContext.Current()){}
  public MainController(Window w,ScanContext scanContext) {
   context=scanContext;window=w;Get<DataGrid>("ResultsGrid").ItemsSource=rows;Get<TextBox>("RootPath").Text=context.UserRoot;
   Bind("NavHome",delegate{Navigate("home");});Bind("NavCache",delegate{Navigate("cache");});Bind("NavInventory",delegate{Navigate("inventory");});Bind("NavSources",delegate{Navigate("source");});Bind("NavOrganize",delegate{Navigate("organize");});Bind("NavHistory",delegate{Navigate("history");});Bind("NavSafety",delegate{Navigate("safety");});
   Bind("NavApps",delegate{Navigate("apps");});Bind("BtnExit",RequestExit);Bind("BtnAppsScan",ScanApps);Bind("BtnAppsMeasure",MeasureApp);Bind("BtnAppsExport",ExportApps);
   Bind("NavDashboard",delegate{Navigate("dashboard");});Bind("NavProtected",delegate{Navigate("protected");});Bind("NavUpdates",delegate{Navigate("updates");});
   Bind("BtnDashboardScan",ScanDashboard);Bind("BtnUpdateCheck",CheckUpdates);Bind("BtnUpdateOpen",OpenUpdateRelease);
   Bind("BtnProtectedAdd",AddProtection);Bind("BtnProtectedRemove",RemoveProtection);
   confirmProtectionRemoval=delegate(string path){return MessageBox.Show(window,"فقط محافظت شخصی این مسیر برداشته می‌شود؛ هیچ فایلی حذف نمی‌شود و قواعد ثابت همچنان فعال‌اند.\n\n"+path,"برداشتن محافظت شخصی",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;};
   Get<ListBox>("ProtectedList").SelectionChanged+=delegate{RefreshProtectionActions();};
   Get<TextBox>("ProtectedPathInput").KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;AddProtection();}};
   Get<TextBox>("DashboardRoot").Text=context.UserRoot;
   Get<DataGrid>("DashboardGrid").SelectionChanged+=delegate{ShowDashboardDetail();};
   Get<TextBlock>("UpdateCurrentVersion").Text="ClearGuard "+CurrentVersion();
   Get<DataGrid>("AppsGrid").ItemsSource=installedRows;
   Get<TextBox>("AppsSearch").TextChanged+=delegate{FilterApps();};Get<ComboBox>("AppsTypeFilter").SelectionChanged+=delegate{FilterApps();};
   Get<DataGrid>("AppsGrid").SelectionChanged+=delegate{ShowAppDetail();};
   Bind("BtnStart",delegate{Navigate("cache");Scan();});Bind("BtnExplore",delegate{Navigate("inventory");Scan();});Bind("BtnScan",Scan);Bind("BtnWholeC",delegate{Get<TextBox>("RootPath").Text="C:\\";});
   Bind("BtnCancel",delegate{if(cancellation!=null)cancellation.Cancel();});Bind("BtnExport",Export);Bind("BtnDuplicates",ScanDuplicates);
   Bind("BtnStorage",delegate{Open("ms-settings:storagesense");});Bind("BtnRecycleBin",delegate{Open("shell:RecycleBinFolder");});Bind("BtnReportFolder",delegate{Directory.CreateDirectory(context.ReportDirectory);Open(context.ReportDirectory);});
   Bind("BtnApply",Apply);Bind("BtnRestore",Restore);
   Bind("BtnSelectSafe",delegate{if(busy)return;var visible=new HashSet<ScanRow>(CollectionViewSource.GetDefaultView(rows).Cast<ScanRow>());foreach(ScanRow row in rows)row.Selected=row.Eligible&&visible.Contains(row);SelectionStatus();});
   Get<TextBox>("SearchBox").TextChanged+=delegate{ICollectionView view=CollectionViewSource.GetDefaultView(rows);string query=Get<TextBox>("SearchBox").Text.Trim();view.Filter=delegate(object o){ScanRow r=(ScanRow)o;return String.IsNullOrEmpty(query)||(r.Name+" "+r.Path+" "+r.App+" "+r.Status).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;};};
   Get<DataGrid>("ResultsGrid").SelectionChanged+=delegate{ScanRow r=Get<DataGrid>("ResultsGrid").SelectedItem as ScanRow;if(r==null)return;Get<TextBlock>("DetailTitle").Text=r.Name+"  ·  "+r.Size+"  ·  "+r.App;Get<TextBox>("DetailPath").Text=r.Path;Get<TextBlock>("DetailEffect").Text=r.Effect+(!String.IsNullOrEmpty(r.Version)?"\nنسخهٔ manifest: "+r.Version:"")+(!String.IsNullOrEmpty(r.BlockReason)?"\nدلیل / شرط: "+r.BlockReason:"")+(!String.IsNullOrEmpty(r.Keeper)?"\nنسخهٔ حفظ‌شده: "+r.Keeper:"")+"\nحجم مجاز: "+r.SafeSize+" · محافظت‌شده: "+r.ProtectedCount;};
   window.Closing+=delegate(object sender,CancelEventArgs e){if(busy){e.Cancel=true;DeferExit();}};
   RefreshDisk();Navigate("home");
  }
  public void PreviewScan(ScanResult scan){string target=scan.Kind=="Cache"?"cache":scan.Kind=="sources"?"source":scan.Kind=="organizer"?"organize":"inventory";lastScans[target]=scan;Navigate(target);if(rows.Count>0)Get<DataGrid>("ResultsGrid").SelectedIndex=0;Status("پیش‌نمایش اسکن واقعی؛ هیچ حذف یا جابه‌جایی انجام نمی‌شود.");}
  public void PreviewApps(InstalledAppsResult result){installedCurrent=result;Navigate("apps");ShowApps(result);Status("فهرست برنامه‌ها فقط خواندنی است؛ حذف نصب وجود ندارد.");}
  public void RequestExit(){if(busy){DeferExit();return;}window.Close();}
  private void DeferExit(){closeRequested=true;if(cancellation!=null)cancellation.Cancel();Status("خروج درخواست شد؛ منتظر توقف امن و ثبت نتیجهٔ عملیات هستیم…");}
  private void SetVisibility(string name,bool visible){Get<UIElement>(name).Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
  private void Navigate(string target){if(busy){Status("اسکن یا عملیات در حال اجراست؛ ابتدا توقف کنید.");return;}section=target;foreach(string item in new[]{"HomePanel","ResultsPanel","AppsPanel","HistoryPanel","SafetyPanel","DashboardPanel","ProtectedPanel","UpdatesPanel"})SetVisibility(item,false);
   bool result=target=="cache"||target=="inventory"||target=="source"||target=="organize";
   SetVisibility("HomePanel",target=="home");SetVisibility("ResultsPanel",result);SetVisibility("HistoryPanel",target=="history");SetVisibility("SafetyPanel",target=="safety");SetVisibility("InventoryOptions",target=="inventory");
   SetVisibility("AppsPanel",target=="apps");
   SetVisibility("DashboardPanel",target=="dashboard");SetVisibility("ProtectedPanel",target=="protected");SetVisibility("UpdatesPanel",target=="updates");
   SetVisibility("BtnApply",result&&target!="inventory");SetVisibility("BtnSelectSafe",result&&target!="inventory");SetVisibility("CleanupMode",target=="cache");
   var titles=new Dictionary<string,string>{{"home","پاک‌سازی، با خیال راحت"},{"cache","کش‌های قابل بازسازی"},{"inventory","چه چیزی فضا را گرفته؟"},{"apps","برنامه‌های نصب‌شده"},{"source","سورس‌ها، نسخه‌ها و تکراری‌ها"},{"organize","دسکتاپ منظم، با قابلیت بازگشت"},{"history","تاریخچه و بازگردانی"},{"safety","محافظت، قبل از پاک‌سازی"}};
   var hints=new Dictionary<string,string>{{"home","اول ببینید چه چیزی فضا گرفته؛ بعد آگاهانه تصمیم بگیرید."},{"cache","مسیرهای شناخته‌شده؛ بررسی مجدد قبل از هر حذف."},{"inventory","اسکن فقط خواندنی است؛ در این بخش امکان حذف وجود ندارد."},{"apps","فهرست و حجم برنامه‌ها؛ فقط خواندنی، بدون حذف نصب یا تعمیر."},{"source","نام یا تاریخ، دلیل حذف نیست؛ فقط تکراری قطعی با نگهداری یک نسخه."},{"organize","پیش‌نمایش دسته‌بندی؛ پوشه‌های پروژه و میان‌برها در جای خود می‌مانند."},{"history","رسید قابل بررسی برای هر عملیات؛ بازگردانی بدون بازنویسی."},{"safety","قواعد ثابت محافظت از رسانه، پروژه، تاریخچه و سیستم."}};
   titles["dashboard"]="فضا کجا و چقدر رشد کرده؟";titles["protected"]="پوشه‌های همیشه محافظت‌شده";titles["updates"]="نسخهٔ جدید، با کنترل شما";
   hints["dashboard"]="نمودار خواندنی و مقایسهٔ اسکن‌ها؛ حجم زیاد، مجوز حذف نیست.";hints["protected"]="محافظت شخصی فقط به خط قرمزهای ثابت اضافه می‌شود.";hints["updates"]="بررسی دستی GitHub؛ بدون دانلود، نصب یا اجرای خودکار.";
   Get<TextBlock>("PageTitle").Text=titles[target];Get<TextBlock>("PageSubtitle").Text=hints[target];Get<TextBlock>("ResultHint").Text=hints[target];
   Get<TextBlock>("MetricCaption").Text=target=="organize"?"قابل دسته‌بندی در اسکن":target=="inventory"?"حذف در این بخش غیرفعال":"قابل پاک‌سازی در اسکن";
   Get<Button>("BtnApply").Content=target=="organize"?"بررسی دسته‌بندی‌ها ←":"بررسی انتخاب‌ها ←";
   foreach(string item in new[]{"NavHome","NavCache","NavInventory","NavApps","NavSources","NavOrganize","NavHistory","NavSafety","NavDashboard","NavProtected","NavUpdates"})Get<Button>(item).Background=Brushes.Transparent;
   string nav=new Dictionary<string,string>{{"home","NavHome"},{"cache","NavCache"},{"inventory","NavInventory"},{"apps","NavApps"},{"source","NavSources"},{"organize","NavOrganize"},{"history","NavHistory"},{"safety","NavSafety"},{"dashboard","NavDashboard"},{"protected","NavProtected"},{"updates","NavUpdates"}}[target];Get<Button>(nav).Background=(Brush)window.FindResource("Stroke");
   current=lastScans.ContainsKey(target)?lastScans[target]:null;ShowResult(current);
   if(target=="history")LoadHistory();
   if(target=="apps")ShowApps(installedCurrent);
   if(target=="dashboard")ShowDashboard(dashboardSnapshot,dashboardComparison);
   if(target=="protected")RefreshProtectedFolders();
  }
  private void ResultSelectionChanged(object sender,PropertyChangedEventArgs e){SelectionStatus();}
  private void ShowResult(ScanResult result){foreach(ScanRow old in rows)old.PropertyChanged-=ResultSelectionChanged;rows.Clear();Get<TextBox>("SearchBox").Text="";if(result!=null)foreach(ScanRow r in result.Rows.OrderByDescending(x=>x.Bytes)){r.Selected=false;r.PropertyChanged-=ResultSelectionChanged;r.PropertyChanged+=ResultSelectionChanged;rows.Add(r);}Get<TextBlock>("SafeValue").Text=result==null?"اسکن نشده":Format.Bytes(rows.Where(x=>x.Eligible).Sum(x=>x.SafeBytes));Get<TextBlock>("SafeCaption").Text=result==null?"ابتدا اسکن انجام دهید.":rows.Count+" مورد · "+rows.Count(x=>x.Eligible)+" مورد مجاز";Get<Button>("BtnApply").IsEnabled=rows.Any(x=>x.Eligible);Get<Button>("BtnExport").IsEnabled=result!=null;if(result!=null)Get<TextBlock>("ResultHint").Text=result.Rows.Count+" مورد، مرتب‌شده از بزرگ به کوچک"+(result.Warnings.Count>0?" · "+result.Warnings.Count+" هشدار (در گزارش)":"");Get<TextBlock>("DetailTitle").Text="یک ردیف را برای دیدن جزئیات انتخاب کنید.";Get<TextBox>("DetailPath").Text="";Get<TextBlock>("DetailEffect").Text="";}
  private void SelectionStatus(){if(busy)return;List<ScanRow> selected=rows.Where(x=>x.Selected&&x.Eligible).ToList();Status(selected.Count+" انتخاب · "+Format.Bytes(selected.Sum(x=>x.SafeBytes))+" · برای ادامه، انتخاب‌ها را بررسی کنید.");}
  private void RefreshDisk(){try{DriveInfo d=new DriveInfo("C");Get<TextBlock>("FreeValue").Text=Format.Bytes(d.AvailableFreeSpace);Get<ProgressBar>("DiskBar").Value=100.0*d.AvailableFreeSpace/d.TotalSize;Get<TextBlock>("DiskCaption").Text=Format.Bytes(d.AvailableFreeSpace)+" آزاد از "+Format.Bytes(d.TotalSize);}catch{Get<TextBlock>("FreeValue").Text="ناموجود";}}
  private void Status(string s){Get<TextBlock>("StatusText").Text=s;}
  private void Progress(string message){DateTime now=DateTime.UtcNow;if((now-lastProgress).TotalMilliseconds<180)return;lastProgress=now;window.Dispatcher.BeginInvoke(new Action(delegate{Status(message);}));}
  private void Busy(bool value){busy=value;SetVisibility("WorkBar",value);Get<ProgressBar>("WorkBar").IsIndeterminate=value;SetVisibility("BtnCancel",value);Get<Button>("BtnScan").IsEnabled=!value;Get<Button>("BtnExport").IsEnabled=!value&&current!=null;Get<Button>("BtnApply").IsEnabled=!value&&rows.Any(x=>x.Eligible);Get<Button>("BtnSelectSafe").IsEnabled=!value;Get<Button>("BtnRestore").IsEnabled=!value;Get<Button>("BtnAppsScan").IsEnabled=!value;Get<Button>("BtnAppsExport").IsEnabled=!value&&installedCurrent!=null;Get<Button>("BtnDashboardScan").IsEnabled=!value;Get<TextBox>("DashboardRoot").IsEnabled=!value;Get<Button>("BtnUpdateCheck").IsEnabled=!value;Get<Button>("BtnUpdateOpen").IsEnabled=!value&&updateResult!=null&&(updateResult.Status==UpdateCheckStatus.UpdateAvailable||updateResult.Status==UpdateCheckStatus.UpToDate)&&UpdateChecker.IsOfficialReleaseUrl(updateResult.ReleaseUrl);RefreshProtectionActions();ShowAppDetail();if(!value&&closeRequested)window.Dispatcher.BeginInvoke(new Action(delegate{if(!busy)window.Close();}));}
  private Task<T> RunWorker<T>(Func<T> work){TaskCompletionSource<T> tcs=new TaskCompletionSource<T>();Thread thread=new Thread(delegate(){try{tcs.SetResult(work());}catch(OperationCanceledException){tcs.SetCanceled();}catch(Exception e){tcs.SetException(e);}});thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;}
  private static string CurrentVersion(){Version version=Assembly.GetExecutingAssembly().GetName().Version;return version.Major+"."+version.Minor+"."+version.Build;}
  private async void CheckUpdates(){
   if(busy)return;
   updateResult=null;Get<Button>("BtnUpdateOpen").IsEnabled=false;Get<TextBox>("UpdateNotes").Text="";
   cancellation=new CancellationTokenSource();Busy(true);Get<TextBlock>("UpdateStatus").Text="در حال بررسی دستی نسخهٔ منتشرشده در GitHub…";
   try{updateResult=await updateChecker.CheckAsync(CurrentVersion(),cancellation.Token);ShowUpdateResult(updateResult);}
   catch(OperationCanceledException){Get<TextBlock>("UpdateStatus").Text="بررسی متوقف شد؛ وضعیت به‌روز بودن مشخص نیست.";}
   catch(Exception){Get<TextBlock>("UpdateStatus").Text="بررسی ناموفق بود؛ وضعیت به‌روز بودن مشخص نیست. دوباره تلاش کنید.";}
   finally{Busy(false);}
  }
  private void ShowUpdateResult(UpdateCheckResult result){
   bool verified=result!=null&&(result.Status==UpdateCheckStatus.UpdateAvailable||result.Status==UpdateCheckStatus.UpToDate)&&UpdateChecker.IsOfficialReleaseUrl(result.ReleaseUrl);
   Get<Button>("BtnUpdateOpen").IsEnabled=!busy&&verified;
   Get<TextBox>("UpdateNotes").Text=result==null?"":result.Notes??"";
   Get<TextBlock>("UpdateStatus").Text=result==null?"هنوز بررسی نشده است.":result.Message+(String.IsNullOrEmpty(result.LatestVersion)?"":"\nنسخهٔ منتشرشده: "+result.LatestVersion)+"\nزمان بررسی: "+result.CheckedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
   if(result!=null)Status(result.Message);
  }
  private void OpenUpdateRelease(){if(busy||updateResult==null||!Get<Button>("BtnUpdateOpen").IsEnabled||!UpdateChecker.IsOfficialReleaseUrl(updateResult.ReleaseUrl))return;Open(updateResult.ReleaseUrl);}
  public void PreviewUpdates(UpdateCheckResult result){Navigate("updates");updateResult=result;ShowUpdateResult(result);Status("پیش‌نمایش با دادهٔ مصنوعی؛ هیچ درخواست شبکه یا نصب انجام نمی‌شود.");}
  private SpaceDashboardService DashboardService(){return new SpaceDashboardService(Path.Combine(context.LocalAppData,"ClearGuard","Dashboard"));}
  private async void ScanDashboard(){
   if(busy)return;
   string root=Get<TextBox>("DashboardRoot").Text;
   try{
    cancellation=new CancellationTokenSource();Busy(true);Status("اسکن خواندنی داشبورد؛ ثبت وضعیت فقط در داده‌های محلی ClearGuard…");
    var service=DashboardService();
    SpaceSnapshot snapshot=await RunWorker(delegate{return service.Scan(root,cancellation.Token);});
    if(cancellation.IsCancellationRequested||snapshot.Status=="Cancelled"){Status("اسکن داشبورد متوقف شد؛ خط مبنای قبلی حفظ شد.");return;}
    SpaceDashboardComparison comparison=await RunWorker(delegate{return service.CompareWithLatest(snapshot);});
    cancellation.Token.ThrowIfCancellationRequested();
    await RunWorker(delegate{return service.SaveSnapshot(snapshot,cancellation.Token);});
    ShowDashboard(snapshot,comparison);Status("وضعیت محلی ثبت شد؛ "+(snapshot.IsComplete?"اسکن کامل":"اسکن ناقص؛ حجم مشاهده‌شده حداقل است")+". هیچ حذف یا جابه‌جایی انجام نشد.");
   }catch(OperationCanceledException){Status("اسکن داشبورد متوقف شد؛ خط مبنای قبلی حفظ شد.");}
   catch(Exception e){Get<TextBlock>("DashboardHint").Text="اسکن یا ثبت وضعیت ناموفق بود: "+e.Message;Status("داشبورد ناموفق؛ نتیجهٔ قبلی با نتیجهٔ نامعتبر جایگزین نشد.");}
   finally{Busy(false);RefreshDisk();}
  }
  public void PreviewDashboard(SpaceSnapshot snapshot,SpaceDashboardComparison comparison){dashboardSnapshot=snapshot;dashboardComparison=comparison;Navigate("dashboard");if(snapshot!=null)Get<TextBox>("DashboardRoot").Text=snapshot.RootPath;ShowDashboard(snapshot,comparison);Status("پیش‌نمایش خواندنی داشبورد؛ هیچ اسکن، ذخیره یا پاک‌سازی انجام نمی‌شود.");}
  private void ShowDashboard(SpaceSnapshot snapshot,SpaceDashboardComparison comparison){
   dashboardSnapshot=snapshot;dashboardComparison=comparison;
   Get<DataGrid>("DashboardGrid").ItemsSource=snapshot==null?null:(comparison==null?snapshot.Rows:comparison.Rows).OrderByDescending(x=>x.ObservedBytes).ToList();
   Get<TextBlock>("MetricCaption").Text="حجم منطقیِ مشاهده‌شده";
   Get<TextBlock>("SafeValue").Text=snapshot==null?"اسکن نشده":Format.Bytes(snapshot.TotalBytes);
   Get<TextBlock>("SafeCaption").Text=snapshot==null?"داشبورد فقط خواندنی است.":snapshot.IsComplete?"اسکن کامل محدوده؛ برابر فضای اشغال‌شدهٔ کل C نیست.":"اسکن ناقص؛ حجم مشاهده‌شده فقط حداقل است.";
   Get<TextBlock>("DashboardHint").Text=snapshot==null?"ابتدا یک محدوده را اسکن کنید. اسکن دومِ کاملِ همان محدوده امکان مقایسه می‌دهد.":"محدوده: "+snapshot.RootPath+" · "+snapshot.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")+"\n"+(comparison==null?"خط مبنا؛ رشد هنوز محاسبه نشده.":comparison.Reason)+(snapshot.IsComplete?"":"\nنتیجه ناقص است؛ تغییر حجم قطعی نمایش داده نمی‌شود.");
   ShowDashboardDetail();
  }
  private void ShowDashboardDetail(){
   if(dashboardSnapshot==null){Get<TextBox>("DashboardDetail").Text="حجم زیاد مجوز حذف نیست؛ در این بخش امکان پاک‌سازی وجود ندارد.";return;}
   SpaceDashboardRow row=Get<DataGrid>("DashboardGrid").SelectedItem as SpaceDashboardRow;
   Get<TextBox>("DashboardDetail").Text=(row==null?"حجم منطقی فایل‌ها در محدودهٔ انتخاب‌شده؛ فایل‌های لینک‌شده دنبال نمی‌شوند.":row.Path+"\r\n"+row.SizeDisplay+" · تغییر: "+row.DeltaDisplay)+"\r\n"+String.Join("\r\n",dashboardSnapshot.Warnings.Take(5));
  }
  private void RefreshProtectedFolders(){
   var store=context.ProtectedFolders;store.Refresh();Get<ListBox>("ProtectedList").ItemsSource=store.Folders.ToList();
   Get<TextBlock>("ProtectedHint").Text=String.IsNullOrEmpty(store.LoadError)?store.Folders.Count+" پوشهٔ شخصی محافظت‌شده. محتوا دست‌نخورده می‌ماند؛ تنظیمات فقط محلی‌اند.":"تنظیمات قابل اعتماد نیست؛ تغییر و عملیات فایل مسدود است. "+store.LoadError;
   Get<TextBlock>("ProtectedStorePath").Text=store.SettingsFile;RefreshProtectionActions();
  }
  private void RefreshProtectionActions(){bool allowed=!busy&&context.ProtectedFolders.CanModify;Get<Button>("BtnProtectedAdd").IsEnabled=allowed;Get<Button>("BtnProtectedRemove").IsEnabled=allowed&&Get<ListBox>("ProtectedList").SelectedItem is string;Get<TextBox>("ProtectedPathInput").IsEnabled=!busy;}
  private void InvalidateMutableScans(){foreach(string target in new[]{"cache","source","organize"})lastScans.Remove(target);foreach(ScanRow row in rows)row.Selected=false;}
  private void AddProtection(){
   if(busy)return;string reason;
   if(context.ProtectedFolders.TryAdd(Get<TextBox>("ProtectedPathInput").Text,out reason)){Get<TextBox>("ProtectedPathInput").Text="";InvalidateMutableScans();RefreshProtectedFolders();Status("محافظت شخصی ثبت شد؛ برای پاک‌سازی دوباره اسکن کنید.");}
   else{RefreshProtectedFolders();Get<TextBlock>("ProtectedHint").Text=reason;Status("محافظت ثبت نشد؛ هیچ فایل شخصی تغییر نکرد.");}
  }
  private void RemoveProtection(){
   if(busy)return;string selected=Get<ListBox>("ProtectedList").SelectedItem as string;if(selected==null||!context.ProtectedFolders.CanModify||!confirmProtectionRemoval(selected))return;string reason;
   if(context.ProtectedFolders.TryRemove(selected,out reason)){InvalidateMutableScans();RefreshProtectedFolders();Status("فقط محافظت شخصی برداشته شد؛ قواعد ثابت همچنان فعال‌اند.");}
   else{RefreshProtectedFolders();Get<TextBlock>("ProtectedHint").Text=reason;}
  }
  private void ShowApps(InstalledAppsResult result){installedRows.Clear();Get<TextBox>("AppsSearch").Text="";Get<ComboBox>("AppsTypeFilter").SelectedIndex=0;if(result!=null)foreach(InstalledAppRow row in result.Apps.OrderByDescending(x=>x.SizeBytes.HasValue).ThenByDescending(x=>x.SizeBytes).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase))installedRows.Add(row);Get<Button>("BtnAppsExport").IsEnabled=result!=null&&!busy;RefreshAppsMetrics();ShowAppDetail();}
  private void RefreshAppsMetrics(){if(section!="apps")return;Get<TextBlock>("MetricCaption").Text="حجم‌های ثبت‌شده (غیرقطعی)";int known=installedRows.Count(x=>x.SizeBytes.HasValue);Get<TextBlock>("SafeValue").Text=installedCurrent==null?"اسکن نشده":Format.Bytes(installedRows.Where(x=>x.SizeBytes.HasValue).Sum(x=>x.SizeBytes.Value));Get<TextBlock>("SafeCaption").Text=installedCurrent==null?"فهرست فقط با درخواست شما خوانده می‌شود.":installedRows.Count+" برنامه · "+(installedRows.Count-known)+" حجم نامشخص";Get<TextBlock>("AppsHint").Text=installedCurrent==null?"برای دیدن برنامه‌ها، اسکن / تازه‌سازی را بزنید.":"تخمین ویندوز یا اندازهٔ منطقی پوشه؛ فایل‌های مشترک، فشرده‌سازی و داده‌های خارج از پوشه باعث تفاوت با فضای واقعی می‌شوند. "+installedCurrent.Warnings.Count+" هشدار / محدودیت (در گزارش)";}
  private void FilterApps(){ICollectionView view=CollectionViewSource.GetDefaultView(installedRows);string query=Get<TextBox>("AppsSearch").Text.Trim();ComboBoxItem option=Get<ComboBox>("AppsTypeFilter").SelectedItem as ComboBoxItem;string kind=option==null?"all":Convert.ToString(option.Tag);view.Filter=delegate(object item){InstalledAppRow r=(InstalledAppRow)item;bool store=(r.Source??"").IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0;return (kind=="all"||(kind=="store"?store:!store))&&(query.Length==0||(r.Name+" "+r.Version+" "+r.Publisher+" "+r.InstallLocation+" "+r.Source).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);};}
  private void ShowAppDetail(){InstalledAppRow row=Get<DataGrid>("AppsGrid").SelectedItem as InstalledAppRow;Get<Button>("BtnAppsMeasure").IsEnabled=!busy&&row!=null&&row.CanMeasure;Get<TextBox>("AppsDetail").Text=row==null?"یک برنامه را برای مشاهدهٔ مسیر و منشأ حجم انتخاب کنید.":row.Name+" · "+row.Version+" · "+row.SizeDisplay+"\r\nمسیر نصب: "+row.InstallLocation+"\r\nمنبع: "+row.Source+" · "+row.Architecture+" · نوع حجم: "+row.SizeKindDisplay+"\r\n"+row.SizeNote+(String.IsNullOrEmpty(row.MeasureBlockReason)?"":"\r\nاندازه‌گیری: "+row.MeasureBlockReason);}
  private async void ScanApps(){if(busy)return;try{cancellation=new CancellationTokenSource();Busy(true);Status("خواندن برنامه‌های نصب‌شده از ویندوز؛ بدون حذف نصب یا تعمیر…");installedCurrent=await RunWorker(delegate{return InstalledAppsScanner.Scan(cancellation.Token,Progress);});ShowApps(installedCurrent);Status("فهرست آماده است · "+installedRows.Count+" برنامه · "+installedCurrent.HiddenComponentCount+" مؤلفهٔ پنهان · "+installedCurrent.Warnings.Count+" محدودیت");}catch(OperationCanceledException){Status("اسکن برنامه‌ها متوقف شد؛ چیزی تغییر نکرد.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private async void MeasureApp(){if(busy)return;InstalledAppRow selected=Get<DataGrid>("AppsGrid").SelectedItem as InstalledAppRow;if(selected==null||!selected.CanMeasure)return;try{cancellation=new CancellationTokenSource();Busy(true);Status("اندازه‌گیری خواندنیِ پوشهٔ نصب انتخاب‌شده…");InstalledAppRow measured=await RunWorker(delegate{return InstalledAppsScanner.Measure(selected,cancellation.Token);});int index=installedRows.IndexOf(selected);if(index>=0)installedRows[index]=measured;int saved=installedCurrent.Apps.IndexOf(selected);if(saved>=0)installedCurrent.Apps[saved]=measured;Get<DataGrid>("AppsGrid").SelectedItem=measured;RefreshAppsMetrics();ShowAppDetail();Status("اندازه‌گیری پایان یافت؛ "+measured.SizeDisplay+" · "+measured.SizeKind);}catch(OperationCanceledException){Status("اندازه‌گیری متوقف شد؛ حجم قبلی باقی ماند.");}catch(Exception e){ShowError(e);}finally{Busy(false);}}
  private void ExportApps(){if(installedCurrent==null||busy)return;SaveFileDialog save=new SaveFileDialog{Filter="گزارش JSON|*.json",FileName="ClearGuard-installed-apps-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"};if(save.ShowDialog(window)!=true)return;try{ReportExport.SaveNewAppsBundle(save.FileName,installedCurrent);Status("گزارش برنامه‌ها در JSON، HTML و CSV ذخیره شد؛ گزارش ممکن است مسیرهای خصوصی داشته باشد.");Open(Path.ChangeExtension(save.FileName,"html"));}catch(Exception e){ShowError(e);}}
  private async void Scan(){if(busy)return;string active=section;if(active!="cache"&&active!="source"&&active!="inventory"&&active!="organize")return;string root=Get<TextBox>("RootPath").Text;try{if(active=="inventory"){root=Path.GetFullPath(root);if(!Directory.Exists(root)){MessageBox.Show(window,"مسیر معتبر نیست.","ClearGuard");return;}}cancellation=new CancellationTokenSource();Busy(true);Status("اسکن خواندنی آغاز شد؛ هیچ فایلی حذف نمی‌شود.");ScanResult scan=await RunWorker(delegate{if(active=="cache")return CacheScanner.Scan(context,cancellation.Token,Progress);if(active=="source")return DesktopScanner.Scan(context,cancellation.Token,Progress);if(active=="organize")return Organizer.Preview(context,cancellation.Token,Progress);return InventoryScanner.Scan(root,cancellation.Token,Progress);});current=scan;lastScans[active]=scan;ShowResult(scan);Status("اسکن کامل شد · "+scan.Rows.Count+" مورد · "+scan.Warnings.Count+" هشدار / محدودیت");}catch(OperationCanceledException){Status("اسکن متوقف شد؛ هیچ فایلی حذف نشد.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private async void ScanDuplicates(){if(busy||section!="inventory")return;try{string root=Path.GetFullPath(Get<TextBox>("RootPath").Text);cancellation=new CancellationTokenSource();Busy(true);Status("اسکن تکراری‌ها با SHA-256؛ برای فایل‌های حجیم ممکن است زمان‌بر باشد.");ScanResult scan=await RunWorker(delegate{return DuplicateScanner.Scan(root,cancellation.Token,Progress);});current=scan;lastScans["inventory"]=scan;ShowResult(scan);Status("تکراری‌های قطعی فقط برای گزارش مشخص شدند؛ حذف در این بخش غیرفعال است.");}catch(OperationCanceledException){Status("اسکن تکراری‌ها متوقف شد؛ هیچ فایلی حذف نشد.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private async void Apply(){if(busy||current==null)return;List<ScanRow> selected=rows.Where(x=>x.Selected&&x.Eligible).ToList();if(selected.Count==0){MessageBox.Show(window,"ابتدا موارد مجاز مورد نظر را انتخاب کنید.","ClearGuard");return;}bool permanent=section=="cache"&&Get<ComboBox>("CleanupMode").SelectedIndex==1;string active=section;string summary=selected.Count+" مورد · "+Format.Bytes(selected.Sum(x=>x.SafeBytes))+"\n\n"+String.Join("\n",selected.Select(x=>x.Path))+"\n\n"+(active=="organize"?"فایل‌های انتخاب‌شده جابه‌جا می‌شوند؛ فایل موجود بازنویسی نمی‌شود.":permanent?"حذف دائمی فقط برای فایل‌های کشِ مجاز انجام می‌شود. قابل بازگردانی نیست.":"فایل‌های مجاز به Recycle Bin منتقل می‌شوند؛ فضای C فوراً آزاد نمی‌شود.")+"\nمسیر، هش، رسانه و قفل بودن دوباره بررسی خواهد شد.";
   if(!Confirm(summary,permanent?"پاک کن":"تأیید"))return;
   try{cancellation=new CancellationTokenSource();Busy(true);OperationReport report=await RunWorker(delegate{if(active=="cache")return CleanupEngine.Run(context,selected,permanent,cancellation.Token,Progress);if(active=="source")return DesktopCleanup.Run(context,selected,cancellation.Token,Progress);return Organizer.Apply(context,selected,cancellation.Token,Progress);});Persist(report);lastScans.Remove(active);current=null;ShowResult(null);Status("عملیات ثبت شد · "+Format.Bytes(report.ProcessedBytes)+" پردازش‌شده · فضای آزاد "+Format.Bytes(report.FreeAfter));if(!closeRequested)MessageBox.Show(window,"حجم دقیق فایل‌های پردازش‌شده: "+report.ProcessedBytes.ToString("N0")+" bytes\nفضای C قبل: "+Format.Bytes(report.FreeBefore)+"\nبعد: "+Format.Bytes(report.FreeAfter)+"\nتغییر واقعی فضای آزاد: "+(report.FreeAfter-report.FreeBefore).ToString("N0")+" bytes\n\n"+(active=="organize"?"جابه‌جایی روی همان درایو؛ فایل‌ها در پوشهٔ دسته‌بندی‌شده موجودند و قابل بازگردانی‌اند.":report.Permanent?"حذف دائمی کش‌های مجاز":"انتقال قابل بازگردانی؛ سطل زباله تخلیه نشده است.")+"\nجزئیات موفق، قفل‌شده و ردشده در تاریخچه ذخیره شد.","گزارش عملیات",MessageBoxButton.OK,MessageBoxImage.Information);}catch(OperationCanceledException){Status("عملیات متوقف شد؛ رسید مرحله‌های انجام‌شده در تاریخچه محفوظ است.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private bool Confirm(string message,string word){Window dialog=new Window{Title="بررسی و تأیید عملیات",Owner=window,Width=680,Height=570,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(Brush)window.FindResource("Canvas"),Foreground=(Brush)window.FindResource("Ink"),FontFamily=new FontFamily("Segoe UI"),FontSize=16,FlowDirection=FlowDirection.RightToLeft};Grid grid=new Grid{Margin=new Thickness(24)};grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});TextBlock text=new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap};ScrollViewer scroll=new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};grid.Children.Add(scroll);StackPanel entry=new StackPanel{Margin=new Thickness(0,14,0,14)};entry.Children.Add(new TextBlock{Text="برای ادامه دقیقاً بنویسید: «"+word+"»"});TextBox input=new TextBox{Margin=new Thickness(0,8,0,0),Height=42,FontSize=18};entry.Children.Add(input);Grid.SetRow(entry,1);grid.Children.Add(entry);Button ok=new Button{Content="اجرای انتخاب‌ها",Height=46,Background=(Brush)window.FindResource("Accent"),Foreground=(Brush)window.FindResource("Canvas"),IsEnabled=false};input.TextChanged+=delegate{ok.IsEnabled=input.Text.Trim()==word;};ok.Click+=delegate{dialog.DialogResult=true;};Grid.SetRow(ok,2);grid.Children.Add(ok);dialog.Content=grid;return dialog.ShowDialog()==true;}
  private void Persist(OperationReport report){Directory.CreateDirectory(context.ReportDirectory);string path=Path.Combine(context.ReportDirectory,report.Id+".json");Format.SaveJson(path,report);ReportExport.Operation(path+".html",report);}
  private void LoadHistory(){histories.Clear();var seen=new HashSet<string>(StringComparer.Ordinal);if(Directory.Exists(context.ReportDirectory))foreach(string file in Directory.GetFiles(context.ReportDirectory,"*.json").OrderBy(x=>Path.GetFileName(x).StartsWith("operation-",StringComparison.Ordinal)).ThenByDescending(x=>x)){try{OperationReport r=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<OperationReport>(File.ReadAllText(file));if(r==null||r.Entries==null||String.IsNullOrEmpty(r.Id)||!seen.Add(r.Id))continue;histories.Add(new HistoryItem{File=file,Report=r});}catch{}}Get<ListBox>("HistoryList").ItemsSource=histories.OrderByDescending(x=>x.Report.StartedUtc).ToList();}
  private void VerifyRestoreProtection(string path){
   context.ProtectedFolders.Refresh();
   if(!SafetyPolicy.IsWithin(path,context.UserRoot))throw new IOException("مسیر رسید خارج از محدودهٔ کاربر است.");
   string reason;
   if(new SafetyPolicy(context).IsProtectedOperationPath(path,true,out reason))throw new IOException(reason);
  }
  private async void Restore(){
   HistoryItem h=Get<ListBox>("HistoryList").SelectedItem as HistoryItem;
   if(h==null||busy)return;
   if(h.Report.Permanent){MessageBox.Show(window,"حذف دائمی قابل بازگردانی نیست.","ClearGuard");return;}
   if(h.Report.Kind!=null&&h.Report.Kind.IndexOf("restore",StringComparison.OrdinalIgnoreCase)>=0){MessageBox.Show(window,"این مورد خودِ گزارش بازگردانی است.","ClearGuard");return;}
   if(!Confirm("بازگردانی فقط موارد همین رسید؛ فایل موجود یا فایل تغییرکرده بازنویسی نمی‌شود.","تأیید"))return;
   try{
    Busy(true);cancellation=new CancellationTokenSource();
    OperationReport restored=await RunWorker(delegate{
     if(h.Report.Kind=="organize"||h.Report.Kind=="organize-move")return Organizer.Restore(context,h.Report,cancellation.Token,Progress);
     OperationReport r=new OperationReport{Kind="restore",Root=context.UserRoot,FreeBefore=Format.FreeC()};
     foreach(OperationEntry item in h.Report.Entries){
      if(cancellation.IsCancellationRequested)break;
      Progress("بازگردانی: "+item.OriginalPath);
      try{
       VerifyRestoreProtection(item.OriginalPath);
       OperationEntry e=RecycleService.Restore(item);r.Entries.Add(e);
       if(String.Equals(e.Status,"Restored",StringComparison.OrdinalIgnoreCase))r.ProcessedBytes+=e.Bytes;
      }catch(Exception ex){r.Entries.Add(new OperationEntry{OriginalPath=item.OriginalPath,Status="Skipped",Detail=ex.Message});}
     }
     r.FreeAfter=Format.FreeC();r.CompletedUtc=DateTime.UtcNow;return r;
    });
    Persist(restored);LoadHistory();Status("گزارش بازگردانی ثبت شد؛ "+restored.Entries.Count+" نتیجه.");
   }catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}
  }
  private void Export(){if(current==null||busy)return;SaveFileDialog save=new SaveFileDialog{Filter="گزارش JSON|*.json",FileName="ClearGuard-"+section+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"};if(save.ShowDialog(window)!=true)return;try{ReportExport.SaveNewScanBundle(save.FileName,current);Status("گزارش JSON، HTML و CSV ذخیره شد.");Open(Path.ChangeExtension(save.FileName,"html"));}catch(Exception e){ShowError(e);}}
  private void ShowError(Exception e){Status("متوقف / رد شده: "+e.Message);if(!closeRequested)MessageBox.Show(window,e.Message+"\nهیچ مورد نامطمئنی نباید حذف شود. گزارش تاریخچه را بررسی کنید.","ClearGuard",MessageBoxButton.OK,MessageBoxImage.Warning);}
  private void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){ShowError(e);}}
 }
 public sealed class DashboardPreview {public SpaceSnapshot Snapshot{get;set;}public SpaceDashboardComparison Comparison{get;set;}}
 public class HistoryItem {public string File;public OperationReport Report;public override string ToString(){return Report.StartedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")+"  ·  "+Report.Kind+"  ·  "+Format.Bytes(Report.ProcessedBytes)+"  ·  "+Report.Entries.Count+" نتیجه";}}
}
