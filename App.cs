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
    if(args.Length>=3&&args[0]=="--scan-inventory"){Format.SaveJson(args[2],InventoryScanner.Scan(args[1],CancellationToken.None,null));return 0;}
    if(args.Length>=2&&(args[0]=="--scan-caches"||args[0]=="--scan-desktop")) {
     ScanContext ctx=ScanContext.Current(); ScanResult scan=args[0]=="--scan-caches"?CacheScanner.Scan(ctx,CancellationToken.None,delegate(string s){}):DesktopScanner.Scan(ctx,CancellationToken.None,delegate(string s){});
     Format.SaveJson(args[1],scan); return 0;
    }
    Application application=new Application();
    using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearGuard.MainWindow.xaml")) {
     Window window=(Window)XamlReader.Load(s); MainController controller=new MainController(window);
     if(args.Length>=3&&args[0]=="--render-scan"){var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};ScanResult scan=j.Deserialize<ScanResult>(File.ReadAllText(args[1]));controller.PreviewScan(scan);Render(window,args[2]);return 0;}
     if(args.Length>=2&&args[0]=="--ui-smoke") {
      Directory.CreateDirectory(args[1]);var states=new List<string>();foreach(string name in new[]{"NavHome","NavCache","NavInventory","NavSources","NavOrganize","NavHistory","NavSafety"}){((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render(window,Path.Combine(args[1],name+".png"));states.Add(name+":PASS");}Format.SaveJson(Path.Combine(args[1],"ui-smoke.json"),new{Pages=states,DestructiveOperations=0,Width=(int)window.ActualWidth,Height=(int)window.ActualHeight});return 0;
     }
     if(args.Length>=2&&args[0]=="--render") {
      Render(window,args[1]);return 0;
     }
     application.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e){MessageBox.Show("عملیات متوقف شد و موارد نامطمئن حذف نشدند.\n"+e.Exception.Message,"ClearGuard",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
     application.Run(window);return 0;
    }
   } catch(Exception e) {try {string output=args.Length>=2?args[args.Length-1]+".error.txt":Path.Combine(Path.GetTempPath(),"ClearGuard-startup-error.txt");File.WriteAllText(output,e.ToString());}catch{} if(args.Length==0)MessageBox.Show(e.Message,"ClearGuard",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
  }
  private static void Render(Window window,string path){
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
  private string section="home";
  private List<HistoryItem> histories=new List<HistoryItem>();
  private Dictionary<string,ScanResult> lastScans=new Dictionary<string,ScanResult>();
  private DateTime lastProgress=DateTime.MinValue;
  private T Get<T>(string name) where T:class{return window.FindName(name) as T;}
  private void Bind(string name,Action action){Get<Button>(name).Click+=delegate{action();};}
  public MainController(Window w) {
   window=w;Get<DataGrid>("ResultsGrid").ItemsSource=rows;Get<TextBox>("RootPath").Text=context.UserRoot;
   Bind("NavHome",delegate{Navigate("home");});Bind("NavCache",delegate{Navigate("cache");});Bind("NavInventory",delegate{Navigate("inventory");});Bind("NavSources",delegate{Navigate("source");});Bind("NavOrganize",delegate{Navigate("organize");});Bind("NavHistory",delegate{Navigate("history");});Bind("NavSafety",delegate{Navigate("safety");});
   Bind("BtnStart",delegate{Navigate("cache");Scan();});Bind("BtnExplore",delegate{Navigate("inventory");Scan();});Bind("BtnScan",Scan);Bind("BtnWholeC",delegate{Get<TextBox>("RootPath").Text="C:\\";});
   Bind("BtnCancel",delegate{if(cancellation!=null)cancellation.Cancel();});Bind("BtnExport",Export);Bind("BtnDuplicates",ScanDuplicates);
   Bind("BtnStorage",delegate{Open("ms-settings:storagesense");});Bind("BtnRecycleBin",delegate{Open("shell:RecycleBinFolder");});Bind("BtnReportFolder",delegate{Directory.CreateDirectory(context.ReportDirectory);Open(context.ReportDirectory);});
   Bind("BtnApply",Apply);Bind("BtnRestore",Restore);
   Bind("BtnSelectSafe",delegate{foreach(ScanRow row in rows)row.Selected=row.Eligible;SelectionStatus();});
   Get<TextBox>("SearchBox").TextChanged+=delegate{ICollectionView view=CollectionViewSource.GetDefaultView(rows);string query=Get<TextBox>("SearchBox").Text.Trim();view.Filter=delegate(object o){ScanRow r=(ScanRow)o;return String.IsNullOrEmpty(query)||(r.Name+" "+r.Path+" "+r.App+" "+r.Status).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;};};
   Get<DataGrid>("ResultsGrid").SelectionChanged+=delegate{ScanRow r=Get<DataGrid>("ResultsGrid").SelectedItem as ScanRow;if(r==null)return;Get<TextBlock>("DetailTitle").Text=r.Name+"  ·  "+r.Size+"  ·  "+r.App;Get<TextBox>("DetailPath").Text=r.Path;Get<TextBlock>("DetailEffect").Text=r.Effect+(!String.IsNullOrEmpty(r.Version)?"\nنسخهٔ manifest: "+r.Version:"")+(!String.IsNullOrEmpty(r.BlockReason)?"\nدلیل / شرط: "+r.BlockReason:"")+(!String.IsNullOrEmpty(r.Keeper)?"\nنسخهٔ حفظ‌شده: "+r.Keeper:"")+"\nحجم مجاز: "+r.SafeSize+" · محافظت‌شده: "+r.ProtectedCount;};
   window.Closing+=delegate(object sender,CancelEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(window,"ابتدا عملیات را با «توقف» متوقف کنید و منتظر ثبت گزارش بمانید.","عملیات در حال اجرا",MessageBoxButton.OK,MessageBoxImage.Information);}};
   RefreshDisk();Navigate("home");
  }
  public void PreviewScan(ScanResult scan){string target=scan.Kind=="Cache"?"cache":scan.Kind=="sources"?"source":scan.Kind=="organizer"?"organize":"inventory";lastScans[target]=scan;Navigate(target);if(rows.Count>0)Get<DataGrid>("ResultsGrid").SelectedIndex=0;Status("پیش‌نمایش اسکن واقعی؛ هیچ حذف یا جابه‌جایی انجام نمی‌شود.");}
  private void SetVisibility(string name,bool visible){Get<UIElement>(name).Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
  private void Navigate(string target){if(busy){Status("اسکن یا عملیات در حال اجراست؛ ابتدا توقف کنید.");return;}section=target;foreach(string item in new[]{"HomePanel","ResultsPanel","HistoryPanel","SafetyPanel"})SetVisibility(item,false);
   bool result=target=="cache"||target=="inventory"||target=="source"||target=="organize";
   SetVisibility("HomePanel",target=="home");SetVisibility("ResultsPanel",result);SetVisibility("HistoryPanel",target=="history");SetVisibility("SafetyPanel",target=="safety");SetVisibility("InventoryOptions",target=="inventory");
   SetVisibility("BtnApply",result&&target!="inventory");SetVisibility("BtnSelectSafe",result&&target!="inventory");SetVisibility("CleanupMode",target=="cache");
   var titles=new Dictionary<string,string>{{"home","پاک‌سازی، با خیال راحت"},{"cache","کش‌های قابل بازسازی"},{"inventory","چه چیزی فضا را گرفته؟"},{"source","سورس‌ها، نسخه‌ها و تکراری‌ها"},{"organize","دسکتاپ منظم، با قابلیت بازگشت"},{"history","تاریخچه و بازگردانی"},{"safety","محافظت، قبل از پاک‌سازی"}};
   var hints=new Dictionary<string,string>{{"home","اول ببینید چه چیزی فضا گرفته؛ بعد آگاهانه تصمیم بگیرید."},{"cache","مسیرهای شناخته‌شده؛ بررسی مجدد قبل از هر حذف."},{"inventory","اسکن فقط خواندنی است؛ در این بخش امکان حذف وجود ندارد."},{"source","نام یا تاریخ، دلیل حذف نیست؛ فقط تکراری قطعی با نگهداری یک نسخه."},{"organize","پیش‌نمایش دسته‌بندی؛ پوشه‌های پروژه و میان‌برها در جای خود می‌مانند."},{"history","رسید قابل بررسی برای هر عملیات؛ بازگردانی بدون بازنویسی."},{"safety","قواعد ثابت محافظت از رسانه، پروژه، تاریخچه و سیستم."}};
   Get<TextBlock>("PageTitle").Text=titles[target];Get<TextBlock>("PageSubtitle").Text=hints[target];Get<TextBlock>("ResultHint").Text=hints[target];
   Get<TextBlock>("MetricCaption").Text=target=="organize"?"قابل دسته‌بندی در اسکن":target=="inventory"?"حذف در این بخش غیرفعال":"قابل پاک‌سازی در اسکن";
   Get<Button>("BtnApply").Content=target=="organize"?"بررسی دسته‌بندی‌ها ←":"بررسی انتخاب‌ها ←";
   foreach(string item in new[]{"NavHome","NavCache","NavInventory","NavSources","NavOrganize","NavHistory","NavSafety"})Get<Button>(item).Background=Brushes.Transparent;
   string nav=new Dictionary<string,string>{{"home","NavHome"},{"cache","NavCache"},{"inventory","NavInventory"},{"source","NavSources"},{"organize","NavOrganize"},{"history","NavHistory"},{"safety","NavSafety"}}[target];Get<Button>(nav).Background=(Brush)window.FindResource("Stroke");
   current=lastScans.ContainsKey(target)?lastScans[target]:null;ShowResult(current);
   if(target=="history")LoadHistory();
  }
  private void ShowResult(ScanResult result){rows.Clear();Get<TextBox>("SearchBox").Text="";if(result!=null)foreach(ScanRow r in result.Rows.OrderByDescending(x=>x.Bytes)){r.Selected=false;r.PropertyChanged+=delegate{SelectionStatus();};rows.Add(r);}Get<TextBlock>("SafeValue").Text=result==null?"اسکن نشده":Format.Bytes(rows.Where(x=>x.Eligible).Sum(x=>x.SafeBytes));Get<TextBlock>("SafeCaption").Text=result==null?"ابتدا اسکن انجام دهید.":rows.Count+" مورد · "+rows.Count(x=>x.Eligible)+" مورد مجاز";Get<Button>("BtnApply").IsEnabled=rows.Any(x=>x.Eligible);Get<Button>("BtnExport").IsEnabled=result!=null;if(result!=null)Get<TextBlock>("ResultHint").Text=result.Rows.Count+" مورد، مرتب‌شده از بزرگ به کوچک"+(result.Warnings.Count>0?" · "+result.Warnings.Count+" هشدار (در گزارش)":"");Get<TextBlock>("DetailTitle").Text="یک ردیف را برای دیدن جزئیات انتخاب کنید.";Get<TextBox>("DetailPath").Text="";Get<TextBlock>("DetailEffect").Text="";}
  private void SelectionStatus(){if(busy)return;List<ScanRow> selected=rows.Where(x=>x.Selected&&x.Eligible).ToList();Status(selected.Count+" انتخاب · "+Format.Bytes(selected.Sum(x=>x.SafeBytes))+" · برای ادامه، انتخاب‌ها را بررسی کنید.");}
  private void RefreshDisk(){try{DriveInfo d=new DriveInfo("C");Get<TextBlock>("FreeValue").Text=Format.Bytes(d.AvailableFreeSpace);Get<ProgressBar>("DiskBar").Value=100.0*d.AvailableFreeSpace/d.TotalSize;Get<TextBlock>("DiskCaption").Text=Format.Bytes(d.AvailableFreeSpace)+" آزاد از "+Format.Bytes(d.TotalSize);}catch{Get<TextBlock>("FreeValue").Text="ناموجود";}}
  private void Status(string s){Get<TextBlock>("StatusText").Text=s;}
  private void Progress(string message){DateTime now=DateTime.UtcNow;if((now-lastProgress).TotalMilliseconds<180)return;lastProgress=now;window.Dispatcher.BeginInvoke(new Action(delegate{Status(message);}));}
  private void Busy(bool value){busy=value;SetVisibility("WorkBar",value);Get<ProgressBar>("WorkBar").IsIndeterminate=value;SetVisibility("BtnCancel",value);Get<Button>("BtnScan").IsEnabled=!value;Get<Button>("BtnExport").IsEnabled=!value&&current!=null;Get<Button>("BtnApply").IsEnabled=!value&&rows.Any(x=>x.Eligible);Get<Button>("BtnSelectSafe").IsEnabled=!value;Get<Button>("BtnRestore").IsEnabled=!value;}
  private Task<T> RunWorker<T>(Func<T> work){TaskCompletionSource<T> tcs=new TaskCompletionSource<T>();Thread thread=new Thread(delegate(){try{tcs.SetResult(work());}catch(OperationCanceledException){tcs.SetCanceled();}catch(Exception e){tcs.SetException(e);}});thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;}
  private async void Scan(){if(busy)return;string active=section;if(active!="cache"&&active!="source"&&active!="inventory"&&active!="organize")return;string root=Get<TextBox>("RootPath").Text;try{if(active=="inventory"){root=Path.GetFullPath(root);if(!Directory.Exists(root)){MessageBox.Show(window,"مسیر معتبر نیست.","ClearGuard");return;}}cancellation=new CancellationTokenSource();Busy(true);Status("اسکن خواندنی آغاز شد؛ هیچ فایلی حذف نمی‌شود.");ScanResult scan=await RunWorker(delegate{if(active=="cache")return CacheScanner.Scan(context,cancellation.Token,Progress);if(active=="source")return DesktopScanner.Scan(context,cancellation.Token,Progress);if(active=="organize")return Organizer.Preview(context,cancellation.Token,Progress);return InventoryScanner.Scan(root,cancellation.Token,Progress);});current=scan;lastScans[active]=scan;ShowResult(scan);Status("اسکن کامل شد · "+scan.Rows.Count+" مورد · "+scan.Warnings.Count+" هشدار / محدودیت");}catch(OperationCanceledException){Status("اسکن متوقف شد؛ هیچ فایلی حذف نشد.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private async void ScanDuplicates(){if(busy||section!="inventory")return;try{string root=Path.GetFullPath(Get<TextBox>("RootPath").Text);cancellation=new CancellationTokenSource();Busy(true);Status("اسکن تکراری‌ها با SHA-256؛ برای فایل‌های حجیم ممکن است زمان‌بر باشد.");ScanResult scan=await RunWorker(delegate{return DuplicateScanner.Scan(root,cancellation.Token,Progress);});current=scan;lastScans["inventory"]=scan;ShowResult(scan);Status("تکراری‌های قطعی فقط برای گزارش مشخص شدند؛ حذف در این بخش غیرفعال است.");}catch(OperationCanceledException){Status("اسکن تکراری‌ها متوقف شد؛ هیچ فایلی حذف نشد.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private async void Apply(){if(busy||current==null)return;List<ScanRow> selected=rows.Where(x=>x.Selected&&x.Eligible).ToList();if(selected.Count==0){MessageBox.Show(window,"ابتدا موارد مجاز مورد نظر را انتخاب کنید.","ClearGuard");return;}bool permanent=section=="cache"&&Get<ComboBox>("CleanupMode").SelectedIndex==1;string active=section;string summary=selected.Count+" مورد · "+Format.Bytes(selected.Sum(x=>x.SafeBytes))+"\n\n"+String.Join("\n",selected.Select(x=>x.Path))+"\n\n"+(active=="organize"?"فایل‌های انتخاب‌شده جابه‌جا می‌شوند؛ فایل موجود بازنویسی نمی‌شود.":permanent?"حذف دائمی فقط برای فایل‌های کشِ مجاز انجام می‌شود. قابل بازگردانی نیست.":"فایل‌های مجاز به Recycle Bin منتقل می‌شوند؛ فضای C فوراً آزاد نمی‌شود.")+"\nمسیر، هش، رسانه و قفل بودن دوباره بررسی خواهد شد.";
   if(!Confirm(summary,permanent?"پاک کن":"تأیید"))return;
   try{cancellation=new CancellationTokenSource();Busy(true);OperationReport report=await RunWorker(delegate{if(active=="cache")return CleanupEngine.Run(context,selected,permanent,cancellation.Token,Progress);if(active=="source")return DesktopCleanup.Run(context,selected,cancellation.Token,Progress);return Organizer.Apply(context,selected,cancellation.Token,Progress);});Persist(report);lastScans.Remove(active);current=null;ShowResult(null);Status("عملیات ثبت شد · "+Format.Bytes(report.ProcessedBytes)+" پردازش‌شده · فضای آزاد "+Format.Bytes(report.FreeAfter));MessageBox.Show(window,"حجم دقیق فایل‌های پردازش‌شده: "+report.ProcessedBytes.ToString("N0")+" bytes\nفضای C قبل: "+Format.Bytes(report.FreeBefore)+"\nبعد: "+Format.Bytes(report.FreeAfter)+"\nتغییر واقعی فضای آزاد: "+(report.FreeAfter-report.FreeBefore).ToString("N0")+" bytes\n\n"+(active=="organize"?"جابه‌جایی روی همان درایو؛ فایل‌ها در پوشهٔ دسته‌بندی‌شده موجودند و قابل بازگردانی‌اند.":report.Permanent?"حذف دائمی کش‌های مجاز":"انتقال قابل بازگردانی؛ سطل زباله تخلیه نشده است.")+"\nجزئیات موفق، قفل‌شده و ردشده در تاریخچه ذخیره شد.","گزارش عملیات",MessageBoxButton.OK,MessageBoxImage.Information);}catch(OperationCanceledException){Status("عملیات متوقف شد؛ رسید مرحله‌های انجام‌شده در تاریخچه محفوظ است.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private bool Confirm(string message,string word){Window dialog=new Window{Title="بررسی و تأیید عملیات",Owner=window,Width=680,Height=570,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(Brush)window.FindResource("Canvas"),Foreground=(Brush)window.FindResource("Ink"),FontFamily=new FontFamily("Segoe UI"),FontSize=16,FlowDirection=FlowDirection.RightToLeft};Grid grid=new Grid{Margin=new Thickness(24)};grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});TextBlock text=new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap};ScrollViewer scroll=new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};grid.Children.Add(scroll);StackPanel entry=new StackPanel{Margin=new Thickness(0,14,0,14)};entry.Children.Add(new TextBlock{Text="برای ادامه دقیقاً بنویسید: «"+word+"»"});TextBox input=new TextBox{Margin=new Thickness(0,8,0,0),Height=42,FontSize=18};entry.Children.Add(input);Grid.SetRow(entry,1);grid.Children.Add(entry);Button ok=new Button{Content="اجرای انتخاب‌ها",Height=46,Background=(Brush)window.FindResource("Accent"),Foreground=(Brush)window.FindResource("Canvas"),IsEnabled=false};input.TextChanged+=delegate{ok.IsEnabled=input.Text.Trim()==word;};ok.Click+=delegate{dialog.DialogResult=true;};Grid.SetRow(ok,2);grid.Children.Add(ok);dialog.Content=grid;return dialog.ShowDialog()==true;}
  private void Persist(OperationReport report){Directory.CreateDirectory(context.ReportDirectory);string path=Path.Combine(context.ReportDirectory,report.Id+".json");Format.SaveJson(path,report);ReportExport.Operation(path+".html",report);}
  private void LoadHistory(){histories.Clear();var seen=new HashSet<string>(StringComparer.Ordinal);if(Directory.Exists(context.ReportDirectory))foreach(string file in Directory.GetFiles(context.ReportDirectory,"*.json").OrderBy(x=>Path.GetFileName(x).StartsWith("operation-",StringComparison.Ordinal)).ThenByDescending(x=>x)){try{OperationReport r=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<OperationReport>(File.ReadAllText(file));if(r==null||r.Entries==null||String.IsNullOrEmpty(r.Id)||!seen.Add(r.Id))continue;histories.Add(new HistoryItem{File=file,Report=r});}catch{}}Get<ListBox>("HistoryList").ItemsSource=histories.OrderByDescending(x=>x.Report.StartedUtc).ToList();}
  private async void Restore(){HistoryItem h=Get<ListBox>("HistoryList").SelectedItem as HistoryItem;if(h==null||busy)return;if(h.Report.Permanent){MessageBox.Show(window,"حذف دائمی قابل بازگردانی نیست.","ClearGuard");return;}if(h.Report.Kind!=null&&h.Report.Kind.IndexOf("restore",StringComparison.OrdinalIgnoreCase)>=0){MessageBox.Show(window,"این مورد خودِ گزارش بازگردانی است.","ClearGuard");return;}if(!Confirm("بازگردانی فقط موارد همین رسید؛ فایل موجود یا فایل تغییرکرده بازنویسی نمی‌شود.","تأیید"))return;try{Busy(true);cancellation=new CancellationTokenSource();OperationReport restored=await RunWorker(delegate{if(h.Report.Kind=="organize"||h.Report.Kind=="organize-move")return Organizer.Restore(context,h.Report,cancellation.Token,Progress);OperationReport r=new OperationReport{Kind="restore",Root=context.UserRoot,FreeBefore=Format.FreeC()};foreach(OperationEntry item in h.Report.Entries){if(cancellation.IsCancellationRequested)break;Progress("بازگردانی: "+item.OriginalPath);try{if(!SafetyPolicy.IsWithin(item.OriginalPath,context.UserRoot))throw new IOException("مسیر رسید خارج از محدودهٔ کاربر است.");OperationEntry e=RecycleService.Restore(item);r.Entries.Add(e);if(String.Equals(e.Status,"Restored",StringComparison.OrdinalIgnoreCase))r.ProcessedBytes+=e.Bytes;}catch(Exception ex){r.Entries.Add(new OperationEntry{OriginalPath=item.OriginalPath,Status="Skipped",Detail=ex.Message});}}r.FreeAfter=Format.FreeC();r.CompletedUtc=DateTime.UtcNow;return r;});Persist(restored);LoadHistory();Status("گزارش بازگردانی ثبت شد؛ "+restored.Entries.Count+" نتیجه.");}catch(Exception e){ShowError(e);}finally{Busy(false);RefreshDisk();}}
  private void Export(){if(current==null)return;SaveFileDialog save=new SaveFileDialog{Filter="گزارش JSON|*.json",FileName="ClearGuard-"+section+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"};if(save.ShowDialog(window)!=true)return;try{Format.SaveJson(save.FileName,current);ReportExport.Scan(Path.ChangeExtension(save.FileName,"html"),current);ReportExport.Csv(Path.ChangeExtension(save.FileName,"csv"),current);Status("گزارش JSON، HTML و CSV ذخیره شد.");Open(Path.ChangeExtension(save.FileName,"html"));}catch(Exception e){ShowError(e);}}
  private void ShowError(Exception e){Status("متوقف / رد شده: "+e.Message);MessageBox.Show(window,e.Message+"\nهیچ مورد نامطمئنی نباید حذف شود. گزارش تاریخچه را بررسی کنید.","ClearGuard",MessageBoxButton.OK,MessageBoxImage.Warning);}
  private void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){ShowError(e);}}
 }
 public class HistoryItem {public string File;public OperationReport Report;public override string ToString(){return Report.StartedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")+"  ·  "+Report.Kind+"  ·  "+Format.Bytes(Report.ProcessedBytes)+"  ·  "+Report.Entries.Count+" نتیجه";}}
}
