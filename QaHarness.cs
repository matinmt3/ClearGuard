using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace ClearGuard {
 // Native WPF integration checks use synthetic rows and an isolated report root.
 // They never call a user-file mutation, scan real caches, or execute uninstall commands.
 public static class FullQaHarness {
  public sealed class UiTestEntry {public string Name{get;set;}public string Status{get;set;}public string Detail{get;set;}}
  public sealed class UiTestReport {
   public DateTime StartedUtc{get;set;}public DateTime CompletedUtc{get;set;}
   public int Passed{get;set;}public int Failed{get;set;}public int Skipped{get;set;}
   public bool RealUserFilesModified{get;set;}public string ExecutableSha256{get;set;}
   public List<UiTestEntry> Tests{get;set;}public List<string> Gaps{get;set;}
  }
  public static int Run(Window window,MainController controller,string outputDirectory) {
   var report=new UiTestReport{StartedUtc=DateTime.UtcNow,Tests=new List<UiTestEntry>(),Gaps=new List<string>(),RealUserFilesModified=false,ExecutableSha256=SafetyPolicy.HashFile(Assembly.GetExecutingAssembly().Location)};
   string fixture=Path.Combine(Environment.CurrentDirectory,"work","ClearGuardTests","ClearGuard-ui-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(fixture);ScanContext context=FixtureContext(fixture);SetField(controller,"context",context);
   window.ShowActivated=false;window.ShowInTaskbar=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;window.Top=-10000;if(!window.IsVisible)window.Show();Pump(window);
   Check(report,"Explicit Exit button is present and reachable",delegate{Button button=Find<Button>(window,"BtnExit");Assert(button.Visibility==Visibility.Visible&&button.IsEnabled,"Exit is hidden or disabled.");Assert(!String.IsNullOrWhiteSpace(Convert.ToString(button.Content)),"Exit lacks a label.");});
   Check(report,"Exit remains inside the visible window at minimum supported size",delegate{
    double width=window.Width,height=window.Height;
    try{window.Width=1080;window.Height=730;Pump(window);Click(window,"NavApps");Pump(window);Button exit=Find<Button>(window,"BtnExit");Rect bounds=exit.TransformToAncestor(window).TransformBounds(new Rect(0,0,exit.ActualWidth,exit.ActualHeight));Assert(exit.ActualWidth>=40&&exit.ActualHeight>=40&&bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=window.ActualWidth&&bounds.Bottom<=window.ActualHeight,"Minimum-size window clips the Exit action.");}
    finally{window.Width=width;window.Height=height;Pump(window);}
   });
   foreach(var target in new[]{new[]{"NavHome","HomePanel"},new[]{"NavCache","ResultsPanel"},new[]{"NavInventory","ResultsPanel"},new[]{"NavSources","ResultsPanel"},new[]{"NavOrganize","ResultsPanel"},new[]{"NavApps","AppsPanel"},new[]{"NavHistory","HistoryPanel"},new[]{"NavSafety","SafetyPanel"}}) {
    string nav=target[0],panel=target[1];Check(report,"Navigation "+nav+" selects exactly its own panel",delegate{
     Click(window,nav);Pump(window);foreach(string name in new[]{"HomePanel","ResultsPanel","HistoryPanel","SafetyPanel","AppsPanel"})Assert(Find<UIElement>(window,name).Visibility==(name==panel?Visibility.Visible:Visibility.Collapsed),name+" has incorrect visibility after "+nav+".");
     Assert(!String.IsNullOrWhiteSpace(Find<TextBlock>(window,"PageTitle").Text),"Page title is empty.");
    });
   }
   ScanResult synthetic=SyntheticScan(context,"Cache");
   Check(report,"Cache preview sorts bytes and clears all default selections",delegate{controller.PreviewScan(synthetic);Pump(window);var grid=Find<DataGrid>(window,"ResultsGrid");var visible=grid.Items.Cast<ScanRow>().ToList();Assert(visible.Count==3&&visible[0].Bytes>visible[1].Bytes&&visible[1].Bytes>visible[2].Bytes,"Default rows are not descending by bytes.");Assert(visible.All(x=>!x.Selected),"Preview auto-selected a deletion.");Assert(Find<Button>(window,"BtnApply").Visibility==Visibility.Visible,"Cache apply action is absent.");});
   Check(report,"Results size column sorts by numeric bytes rather than display text",delegate{
    DataGrid grid=Find<DataGrid>(window,"ResultsGrid");DataGridColumn size=grid.Columns.First(x=>Convert.ToString(x.Header)=="حجم");Assert(size.SortMemberPath=="Bytes","Size column sorts formatted text.");
    ICollectionView view=CollectionViewSource.GetDefaultView(grid.ItemsSource);view.SortDescriptions.Clear();view.SortDescriptions.Add(new SortDescription(size.SortMemberPath,ListSortDirection.Ascending));var values=view.Cast<ScanRow>().Select(x=>x.Bytes).ToList();Assert(values.SequenceEqual(values.OrderBy(x=>x)),"Actual view size sort is not numeric.");view.SortDescriptions.Clear();
   });
   Check(report,"Search matches path or app case-insensitively and clearing restores rows",delegate{
    TextBox search=Find<TextBox>(window,"SearchBox");search.Text="ONLY-MATCH";Pump(window);var grid=Find<DataGrid>(window,"ResultsGrid");Assert(grid.Items.Count==1&&((ScanRow)grid.Items[0]).Name=="Middle cache","Path query did not filter rows.");search.Text="specialapp";Pump(window);Assert(grid.Items.Count==1&&((ScanRow)grid.Items[0]).Name=="Small protected","Application query was not case-insensitive.");search.Text="";Pump(window);Assert(grid.Items.Count==3,"Clearing search did not restore all rows.");
   });
   Check(report,"Filtered select-safe does not select hidden cache rows",delegate{
    Find<TextBox>(window,"SearchBox").Text="only-match";Pump(window);Click(window,"BtnSelectSafe");Assert(synthetic.Rows.Single(x=>x.Name=="Middle cache").Selected&&!synthetic.Rows.Single(x=>x.Name=="Large cache").Selected&&!synthetic.Rows.Single(x=>x.Name=="Small protected").Selected,"Filtered select-safe selected a hidden or protected row.");Find<TextBox>(window,"SearchBox").Text="";Pump(window);
   });
   Check(report,"Select safe selects only eligible rows and details show exact row",delegate{
    Click(window,"BtnSelectSafe");var grid=Find<DataGrid>(window,"ResultsGrid");Assert(synthetic.Rows.Where(x=>x.Eligible).All(x=>x.Selected)&&synthetic.Rows.Where(x=>!x.Eligible).All(x=>!x.Selected),"Protected row was selected.");grid.SelectedItem=synthetic.Rows.First(x=>x.Name=="Middle cache");Pump(window);Assert(Find<TextBox>(window,"DetailPath").Text==synthetic.Rows.First(x=>x.Name=="Middle cache").Path,"Selected details use the wrong path.");Assert(Find<TextBlock>(window,"DetailEffect").Text.Contains("fixture effect"),"Selected effect is missing.");
   });
   Check(report,"Inventory and duplicate audit UI expose no deletion action",delegate{
    controller.PreviewScan(SyntheticScan(context,"Inventory"));Assert(Find<Button>(window,"BtnApply").Visibility==Visibility.Collapsed&&Find<Button>(window,"BtnSelectSafe").Visibility==Visibility.Collapsed&&Find<ComboBox>(window,"CleanupMode").Visibility==Visibility.Collapsed,"Read-only inventory exposes deletion controls.");Assert(Find<UIElement>(window,"InventoryOptions").Visibility==Visibility.Visible,"Inventory root options absent.");
    controller.PreviewScan(SyntheticScan(context,"Duplicate audit"));Assert(Find<Button>(window,"BtnApply").Visibility==Visibility.Collapsed,"Duplicate audit exposes deletion controls.");
   });
   Check(report,"Source and organizer UI cannot select permanent deletion mode",delegate{
    controller.PreviewScan(SyntheticScan(context,"sources"));Assert(Find<ComboBox>(window,"CleanupMode").Visibility==Visibility.Collapsed,"Source cleanup exposes permanent deletion.");
    controller.PreviewScan(SyntheticScan(context,"organizer"));Assert(Find<ComboBox>(window,"CleanupMode").Visibility==Visibility.Collapsed&&Convert.ToString(Find<Button>(window,"BtnApply").Content).Contains("دسته"),"Organizer uses a deletion action.");
   });
   Check(report,"Installed applications grid is read-only and cannot delete or uninstall",delegate{
    Click(window,"NavApps");DataGrid grid=Find<DataGrid>(window,"AppsGrid");Assert(grid.IsReadOnly&&!grid.CanUserDeleteRows&&!grid.CanUserAddRows,"Installed app rows can be edited or removed.");Assert(Find<Button>(window,"BtnApply").Visibility==Visibility.Collapsed&&Find<Button>(window,"BtnSelectSafe").Visibility==Visibility.Collapsed&&Find<ComboBox>(window,"CleanupMode").Visibility==Visibility.Collapsed,"Installed app section exposes cleanup controls.");
    Assert(!grid.Columns.Any(x=>Convert.ToString(x.Header).IndexOf("uninstall",StringComparison.OrdinalIgnoreCase)>=0||Convert.ToString(x.Header).Contains("حذف")),"Installed-app grid exposes uninstall.");
    Assert(grid.Columns.Any(x=>x.SortMemberPath=="SizeBytes"),"Installed size column is not numerically sortable.");
   });
   Check(report,"Installed app preview search and raw size sorting preserve unknown values",delegate{
    MethodInfo preview=typeof(MainController).GetMethod("PreviewApps",BindingFlags.Public|BindingFlags.Instance);Assert(preview!=null,"App preview API is absent.");object data=Activator.CreateInstance(preview.GetParameters()[0].ParameterType);PropertyInfo appsProperty=data.GetType().GetProperty("Apps");IList apps=(IList)appsProperty.GetValue(data,null);Type rowType=appsProperty.PropertyType.GetGenericArguments()[0];
    object large=MakeApp(rowType,"Large fixture program",4096),small=MakeApp(rowType,"Small fixture program",12),unknown=MakeApp(rowType,"Unknown fixture program",null);SetProperty(unknown,"Source","Microsoft Store");apps.Add(small);apps.Add(unknown);apps.Add(large);preview.Invoke(controller,new[]{data});Pump(window);
    DataGrid grid=Find<DataGrid>(window,"AppsGrid");Assert(grid.Items.Count==3,"Installed app preview lost an unknown-size row.");TextBox search=Find<TextBox>(window,"AppsSearch");search.Text="SMALL fixture";Pump(window);Assert(grid.Items.Count==1,"Installed app search failed.");search.Text="";Pump(window);Assert(grid.Items.Count==3,"App search clearing lost rows.");
    DataGridColumn size=grid.Columns.First(x=>x.SortMemberPath=="SizeBytes");ICollectionView view=CollectionViewSource.GetDefaultView(grid.ItemsSource);view.SortDescriptions.Clear();view.SortDescriptions.Add(new SortDescription(size.SortMemberPath,ListSortDirection.Descending));var known=view.Cast<object>().Select(x=>x.GetType().GetProperty("SizeBytes").GetValue(x,null)).Where(x=>x!=null).Select(Convert.ToInt64).ToList();Assert(known.SequenceEqual(known.OrderByDescending(x=>x)),"Installed app numeric sort failed.");view.SortDescriptions.Clear();
    Assert(Find<Button>(window,"BtnAppsExport").IsEnabled,"Installed app export disabled after preview.");
    ComboBox filter=Find<ComboBox>(window,"AppsTypeFilter");filter.SelectedItem=filter.Items.Cast<ComboBoxItem>().First(x=>Convert.ToString(x.Tag)=="store");Pump(window);Assert(grid.Items.Count==1,"Store type filter lost or included incorrect registrations.");grid.SelectedIndex=0;Pump(window);Assert(Find<TextBox>(window,"AppsDetail").Text.Contains("Unknown fixture program")&&Find<TextBox>(window,"AppsDetail").Text.Contains("نامشخص"),"App detail conceals unknown size or shows the wrong app.");Assert(!Find<Button>(window,"BtnAppsMeasure").IsEnabled,"Invalid install location exposes measurement.");filter.SelectedIndex=0;Pump(window);Assert(grid.Items.Count==3,"Resetting app type filter did not restore rows.");
   });
   Check(report,"Busy state blocks navigation and Stop requests cancellation",delegate{
    Click(window,"NavCache");var cancellation=new CancellationTokenSource();SetField(controller,"cancellation",cancellation);Invoke(controller,"Busy",true);Click(window,"NavApps");Assert(Find<UIElement>(window,"ResultsPanel").Visibility==Visibility.Visible&&Find<UIElement>(window,"AppsPanel").Visibility==Visibility.Collapsed,"Navigation switched while worker was active.");Click(window,"BtnCancel");Assert(cancellation.IsCancellationRequested,"Stop did not cancel worker.");Assert(!Find<Button>(window,"BtnApply").IsEnabled,"Apply remained enabled during worker.");Invoke(controller,"Busy",false);cancellation.Dispose();
   });
   Check(report,"Busy Exit cancels and waits for worker completion",delegate{
    Window other=NewWindow();var otherController=new MainController(other);SetField(otherController,"context",context);other.ShowActivated=false;other.ShowInTaskbar=false;other.Left=-10000;other.Top=-10000;other.Show();bool closed=false;other.Closed+=delegate{closed=true;};var cancellation=new CancellationTokenSource();SetField(otherController,"cancellation",cancellation);Invoke(otherController,"Busy",true);Click(other,"BtnExit");Pump(other);Assert(cancellation.IsCancellationRequested&&!closed&&other.IsVisible,"Busy Exit closed before worker journal could finish.");Assert((bool)Field(otherController,"closeRequested"),"Busy Exit did not remember exit intent.");Invoke(otherController,"Busy",false);Pump(other);Assert(closed&&!other.IsVisible,"Deferred Exit did not close after worker finished.");cancellation.Dispose();
   });
   Check(report,"Idle Exit button closes the window",delegate{bool closed=false;window.Closed+=delegate{closed=true;};Click(window,"BtnExit");Pump(window);Assert(closed&&!window.IsVisible,"Explicit Exit did not close idle window.");});
   report.CompletedUtc=DateTime.UtcNow;report.Gaps.Add("Automated native WPF checks use generated rows and off-screen windows, not a full manual interaction pass on every Windows configuration.");report.Gaps.Add("Actual uninstall is intentionally absent. Real personal-file cleanup and organizer moves were not invoked by UI checks.");Format.SaveJson(Path.Combine(outputDirectory,"ui-integration.json"),report);return report.Failed==0?0:1;
  }
  private static ScanResult SyntheticScan(ScanContext context,string kind){var result=new ScanResult{Kind=kind,Root=context.UserRoot,CompletedUtc=DateTime.UtcNow};result.Rows.Add(new ScanRow{Name="Small protected",App="SpecialApp",Path=Path.Combine(context.UserRoot,"small.tmp"),Bytes=12,Eligible=false,Selected=true,Effect="protected fixture"});result.Rows.Add(new ScanRow{Name="Large cache",App="node-gyp",Path=Path.Combine(context.LocalAppData,"large.tmp"),Bytes=5L*1024*1024*1024,SafeBytes=64,Eligible=true,Selected=true,Effect="fixture effect"});result.Rows.Add(new ScanRow{Name="Middle cache",App="node-gyp",Path=Path.Combine(context.LocalAppData,"only-match.tmp"),Bytes=2L*1024*1024,SafeBytes=32,Eligible=true,Selected=true,Effect="fixture effect",BlockReason="fixture condition"});return result;}
  private static ScanContext FixtureContext(string root){var context=new ScanContext{UserRoot=root,LocalAppData=Path.Combine(root,"AppData","Local"),Desktop=Path.Combine(root,"Desktop"),Documents=Path.Combine(root,"Documents"),Downloads=Path.Combine(root,"Downloads"),ReportDirectory=Path.Combine(root,"Reports"),IsFixture=true};Directory.CreateDirectory(context.Desktop);Directory.CreateDirectory(context.ReportDirectory);return context;}
  private static object MakeApp(Type type,string name,long? size){object row=Activator.CreateInstance(type);SetProperty(row,"Name",name);SetProperty(row,"SizeBytes",size);SetProperty(row,"Source","Registry");SetProperty(row,"Publisher","Fixture-only publisher");SetProperty(row,"SizeKind",size.HasValue?"Estimated":"Unknown");return row;}
  private static void SetProperty(object target,string name,object value){PropertyInfo property=target.GetType().GetProperty(name);if(property!=null&&property.CanWrite)property.SetValue(target,value,null);}
  private static Window NewWindow(){using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearGuard.MainWindow.xaml"))return (Window)XamlReader.Load(stream);}
  private static T Find<T>(Window window,string name)where T:class{T value=window.FindName(name)as T;if(value==null)throw new InvalidOperationException("Missing required UI element: "+name);return value;}
  private static void Click(Window window,string name){Find<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
  private static void Pump(Window window){window.Dispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));if(window.IsVisible)window.UpdateLayout();}
  private static object Field(object target,string name){FieldInfo field=target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);if(field==null)throw new InvalidOperationException("Missing state field: "+name);return field.GetValue(target);}
  private static void SetField(object target,string name,object value){FieldInfo field=target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);if(field==null)throw new InvalidOperationException("Missing state field: "+name);field.SetValue(target,value);}
  private static void Invoke(object target,string name,params object[] args){MethodInfo method=target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance);if(method==null)throw new InvalidOperationException("Missing controller method: "+name);method.Invoke(target,args);}
  private static void Check(UiTestReport report,string name,Action test){try{test();report.Passed++;report.Tests.Add(new UiTestEntry{Name=name,Status="PASS",Detail="Verified on generated rows and isolated native WPF windows."});}catch(Exception ex){Exception error=ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex;report.Failed++;report.Tests.Add(new UiTestEntry{Name=name,Status="FAIL",Detail=error.GetType().Name+": "+error.Message});}}
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 }
}
