using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using Mizu.Widget;
using Mizu.Widget.Controls;
using Mizu.Core.Services;
using Mizu.Core;
class Program {
static IEnumerable<T> Desc<T>(DependencyObject parent) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T item)yield return item;foreach(var nested in Desc<T>(child))yield return nested;} }
[STAThread] static int Main() {
try { return Run(); }
catch(Exception ex) {
Console.Error.WriteLine(ex.ToString());
try { System.IO.File.WriteAllText("ui-check-error.log",ex.ToString()); } catch { }
System.Windows.Application.Current?.Shutdown(1); return 1;
}
}
static RenderTargetBitmap Capture(FrameworkElement element) {
var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*2),(int)Math.Ceiling(element.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(element);return bitmap;
}
static void Pump(int milliseconds) {
var frame=new System.Windows.Threading.DispatcherFrame();
var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(milliseconds)};
timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
}
static int Run() {
var app=new System.Windows.Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };int checks=0;
void Check(bool result,string message){if(!result)throw new Exception(message);checks++;}
foreach(var theme in new[]{"light","dark"}) foreach(var radius in new[]{0.0,16.0,30.0,64.0}) foreach(var compact in new[]{false,true}) {
var settings=new AppSettings { Theme=theme,CornerRadius=radius,CompactMode=compact,ShowTodo=true,WeatherDaily=true,ForecastDays=7,ForecastHours=12,Todos=[new(){Text="Test todo"}] };
var surface=new WidgetSurface(WidgetState.Preview(),settings);var height=surface.Layout.Height;
surface.Width=356;surface.Height=height;surface.Measure(new(356,height));surface.Arrange(new(0,0,356,height));surface.UpdateLayout();
foreach(double dpi in new[]{1.0,1.25,1.5,2.0}) {
int w=(int)Math.Ceiling(356*dpi),h=(int)Math.Ceiling(height*dpi);
var bitmap=new RenderTargetBitmap(w,h,96*dpi,96*dpi,PixelFormats.Pbgra32);bitmap.Render(surface);var pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);
if(radius>0) foreach(var p in new[]{(0,0),(w-1,0),(0,h-1),(w-1,h-1)})Check(pixels[(p.Item2*w+p.Item1)*4+3]==0,$"Corner {theme} R{radius} compact{compact} DPI{dpi}");
Check(pixels[((h/2)*w+w/2)*4+3]>0,"Material present");
}
}
var settingsLive=new AppSettings { ShowTodo=true,Todos=[new(){Text="First"},new(){Text="Second"}],WeatherDaily=true };
var main=new MainWindow(settingsLive,WidgetState.Preview(),preview:true,persistSettings:false);main.Show();main.UpdateLayout();
var grid=(Grid)main.VisualContent;
var toolbar=(StackPanel)typeof(MainWindow).GetField("toolbar",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
Check(toolbar.Opacity==0 && !toolbar.IsHitTestVisible,"Toolbar is absent and cannot intercept clicks before hover");
main.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});Check(toolbar.IsHitTestVisible,"Hover enables controls");
main.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});Check(!toolbar.IsHitTestVisible,"Leaving removes invisible hit targets");
var first=Desc<CheckBox>(grid).First();first.IsChecked=true;first.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
main.UpdateLayout(); Check(settingsLive.Todos[0].Completed && Desc<CheckBox>(grid).Count()==1,"Todo completion updates settings and hides completed rows");
var input=Desc<TextBox>(grid).Single();input.Text="New inline task";
var add=Desc<Button>(grid).Single(b=>b.ToolTip is ToolTip hint && Equals(hint.Content,"添加待办"));add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
main.UpdateLayout(); Check(settingsLive.Todos.Count==3 && settingsLive.Todos[2].Text=="New inline task","Inline add persists a new task");
settingsLive.ShowWeather=settingsLive.ShowDevices=settingsLive.ShowMemory=settingsLive.ShowTemperature=false;settingsLive.ShowTodo=false;main.ApplySettings();main.UpdateLayout();
Check(grid.ActualHeight==WidgetLayout.Calculate(settingsLive).Height && Desc<TextBox>(grid).Count()==0,"Disabling modules reflows window and removes todo editor");
var dialog=new SettingsWindow(main);dialog.Show();dialog.UpdateLayout();var tabs=Desc<TabControl>(dialog).Single();Check(tabs.Items.Count==7,"Seven settings pages");
tabs.SelectedIndex=0;dialog.UpdateLayout();
var sliders=Desc<Slider>(dialog).ToArray(); sliders[0].Value=35;dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);Check(Math.Abs(settingsLive.GlassOpacity-.35)<.001,"Opacity control applies immediately");
sliders[1].Value=64;dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);Check(settingsLive.CornerRadius==64 && ((RectangleGeometry)grid.Clip).RadiusX==64,"Radius control updates visual and content clipping together");
sliders[2].Value=123.45;Pump(55);
var animated=(ScaleTransform)grid.RenderTransform;
Check(!sliders[2].IsSnapToTickEnabled && Math.Abs(settingsLive.Scale-1.2345)<.0001,"Scale slider supports fractional percentages");

Check(animated.ScaleX>1 && animated.ScaleX<1.2345 && animated.HasAnimatedProperties,"Scale has intermediate animation frames");
sliders[2].Value=86.3;Pump(25);sliders[2].Value=110.7;Pump(240);
Check(Math.Abs(main.Width-356*1.107)<1 && Math.Abs(animated.ScaleX-1.107)<.0001,"Interrupted resize settles on latest requested size");
Check(!animated.HasAnimatedProperties && grid.CacheMode is null,"Settled widget restores crisp vector rendering");
sliders[3].Value=125;Pump(35);
Check(Math.Abs(settingsLive.TextScale-1.25)<.001,"Font size control updates widget typography");
Desc<ComboBox>(dialog).Last().SelectedIndex=4;
Check(settingsLive.TextWeight==700,"Font weight control applies bold typography");
Desc<ComboBox>(dialog).First().SelectedIndex=1;Check(settingsLive.Theme=="dark","Theme selection applies immediately");
tabs.SelectedIndex=2;dialog.UpdateLayout();var nowButton=Desc<Button>(dialog).Single(b=>Equals(b.Content,"只看当前"));nowButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
Check(settingsLive.WeatherCurrent && !settingsLive.WeatherTodayRange && !settingsLive.WeatherHourly && !settingsLive.WeatherDaily,"Weather preset changes actual displayed sections");
for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;dialog.UpdateLayout();Check(Desc<ScrollViewer>(dialog).Any(),$"Settings page {i} renders");}

foreach(var id in new[]{"navy","pink","lavender"}) {
settingsLive.Theme=id;main.ApplySettings();main.UpdateLayout();
Check(HoverHints.GetPalette(main.Surface).Id==id,"New theme reaches widget and popup targets: "+id);
}
var beforeIds=main.Surface.VisibleDevices.Select(d=>d.Id).ToArray();
main.State.Devices=main.State.Devices.Select(d=>d with {Percent=d.Percent-1}).ToArray();
Check(beforeIds.SequenceEqual(main.Surface.VisibleDevices.Select(d=>d.Id)),"Battery-only refresh preserves visible device order");
main.State.Devices=main.State.Devices.Select((d,i)=>d with {State=i==0?LinkState.Offline:LinkState.Online}).ToArray();
Check(main.Surface.VisibleDevices.First().State==LinkState.Online,"Confirmed active connections move ahead of old offline readings");
var receiverMouse = main.State.Devices.First(d=>d.Kind==PeripheralKind.Mouse);
main.State.Devices = main.State.Devices.Select(d=>d with { State=LinkState.Offline }).Append(receiverMouse with { Id="new-bt-mouse",Link=LinkKind.Bluetooth,State=LinkState.Online }).ToArray();
main.State.DevicePage=1;typeof(MainWindow).GetMethod("OnStateChanged",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null)!.Invoke(main,[]);
Check(main.State.DevicePage==0 && main.Surface.VisibleDevices.First().Id=="new-bt-mouse","New Bluetooth connection is brought onto the visible page");
main.State.Devices = main.State.Devices.Select(d=>d with { State=d.Id==receiverMouse.Id?LinkState.Online:LinkState.Offline }).ToArray();
main.State.DevicePage=1;typeof(MainWindow).GetMethod("OnStateChanged",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null)!.Invoke(main,[]);
Check(main.State.DevicePage==0 && main.Surface.VisibleDevices.First().Id==receiverMouse.Id,"Returning to the receiver also follows the newly active connection");
settingsLive.Theme="dark";main.ApplySettings();main.UpdateLayout();
// Open the actual Popup to verify cross-tree theme binding and resizing.
HoverHints.Set(main.Surface,"Alto Keys K98M\n无线接收器 · 已连接");
var hover=(ToolTip)main.Surface.ToolTip;
hover.IsOpen=true;hover.UpdateLayout();
Check(hover.ActualWidth>0 && hover.ActualWidth<=286,"Opened hover hint fits compact width");
hover.Dispatcher.Invoke(() => {},System.Windows.Threading.DispatcherPriority.ApplicationIdle);

var darkColor=((SolidColorBrush)hover.Background).Color;
var darkHint=Capture(hover);
Check(darkColor.R<100 && darkColor.G<100,"Popup inherits dark palette through PlacementTarget");
HoverHints.Set(main.Surface,"更长的设备名称和一段用于检查自动换行的连接信息，信息应留在圆角提示内。\n蓝牙 · 已连接");
Check(ReferenceEquals(main.Surface.ToolTip,hover) && hover.IsOpen,"Changing sections updates the open hint without popup flicker");
hover.UpdateLayout();Check(hover.ActualWidth<=286,"Long device names wrap within popup bounds");
settingsLive.Theme="light";main.ApplySettings();hover.UpdateLayout();
hover.Dispatcher.Invoke(() => {},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
var lightColor=((SolidColorBrush)hover.Background).Color;
Check(lightColor.R>200 && lightColor.G>200,"Open popup follows live theme changes");
Check(ToolTipService.GetInitialShowDelay(main.Surface)>=700 && ToolTipService.GetBetweenShowDelay(main.Surface)==0,"Brief pointer passes do not immediately show hints");

if(Environment.GetEnvironmentVariable("MIZU_HOVER_RENDER") is { Length: >0 } imagePath) {
hover.Content="Alto Keys K98M\n无线接收器 · 已连接"; hover.UpdateLayout();
hover.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);hover.UpdateLayout();
var lightHint=Capture(hover); var scene=new DrawingVisual();
using(var draw=scene.RenderOpen()) {
draw.DrawRectangle(Art.Brush("#E6EADF"),null,new Rect(0,0,360,230));
draw.DrawRectangle(Art.Brush("#283C32"),null,new Rect(360,0,360,230));
Art.Text(draw,"悬停提示 · 示例信息",26,22,13,Art.Brush("#697B61"));
Art.Text(draw,"墨绿",390,22,13,Art.Brush("#AABBA4"));
draw.DrawImage(lightHint,new Rect(28,96,lightHint.PixelWidth/2.0,lightHint.PixelHeight/2.0));
draw.DrawImage(darkHint,new Rect(390,96,darkHint.PixelWidth/2.0,darkHint.PixelHeight/2.0));
}
var render=new RenderTargetBitmap(1440,460,192,192,PixelFormats.Pbgra32);render.Render(scene);
var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using var file=System.IO.File.Create(imagePath);png.Save(file);
}
main.Surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
Check(!hover.IsOpen,"Pointer leave closes the surface hint");
main.Settings.ShowNetwork = true; main.Settings.ShowMedia = true;
main.State.Media = new("A very long music title for truncation and layout verification", "Example artist", true, true, true, false, "Example session");
main.State.Network = new(true, "Example adapter", 1048576, 2048, 1.7, 24, 60, "1.1.1.1");
main.ApplySettings(); main.UpdateLayout();
var mediaButtons = Desc<Button>(main).Where(b => new[] { "上一首", "暂停", "下一首" }.Contains(System.Windows.Automation.AutomationProperties.GetName(b))).ToArray();
Check(mediaButtons.Length == 3, "Media has three accessible controls");
Check(mediaButtons.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "下一首").IsEnabled == false, "Unsupported media commands are disabled");
Check(main.Surface.Layout.Sections.Any(s => s.Kind == WidgetSection.Network) && main.Surface.Layout.Sections.Any(s => s.Kind == WidgetSection.Media), "Both new modules reflow into widget");
main.Settings.NetworkProbeTargets = ["192.0.2.1", "2001:db8::1"]; main.Settings.ShowNetworkProbeIp = true;
main.State.Network = main.State.Network! with { Targets = [new("192.0.2.1",0,12,30),new("2001:db8::1",10,25,30)] };main.ApplySettings();main.UpdateLayout();
Check(main.Surface.Layout.Sections.Single(s=>s.Kind==WidgetSection.Network).Height==132 && main.Surface.NetworkHint.Contains("192.0.2.1"),"Multiple network probe rows reserve their IP headings");
main.Settings.ShowNetworkProbeIp=false;main.ApplySettings();main.UpdateLayout();
Check(main.Surface.Layout.Sections.Single(s=>s.Kind==WidgetSection.Network).Height==100 && !main.Surface.NetworkHint.Contains("192.0.2.1") && !main.Surface.NetworkHint.Contains("2001:db8::1"),"Hiding probe IPs redacts both display headings and hover details");
tabs.SelectedIndex=1;typeof(SettingsWindow).GetMethod("BuildProbeList",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[]);dialog.UpdateLayout();
var probeInput=Desc<TextBox>(dialog).Single(t=>string.IsNullOrEmpty(t.Text));probeInput.Text="203.0.113.8";
Desc<Button>(dialog).Single(b=>Equals(b.Content,"添加 IP")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));dialog.UpdateLayout();
Check(main.Settings.NetworkProbeTargets!.Count==3 && main.Settings.NetworkProbeTargets.Contains("203.0.113.8"),"Settings add a custom probe through the actual control");
Desc<Button>(dialog).First(b=>b.ToolTip is ToolTip t && Equals(t.Content,"删除探测 IP")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));dialog.UpdateLayout();
Check(main.Settings.NetworkProbeTargets!.Count==2,"Settings remove a configured probe through its row control");
main.Settings.ShowMedia = false; main.Settings.ShowNetwork = false; main.ApplySettings(); main.UpdateLayout();
Check(!main.Surface.Layout.Sections.Any(s => s.Kind is WidgetSection.Media or WidgetSection.Network), "Disabling modules releases their layout space");
main.Settings.ShowTodo = true; main.Settings.Todos.Clear(); main.ApplySettings(); main.UpdateLayout();
Check(!main.Surface.Layout.Sections.Any(s => s.Kind == WidgetSection.Todo), "Empty todo has no body section or reserved footer");
var footer = (FrameworkElement)typeof(MainWindow).GetField("todoEditorHost",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(main)!;
Check(footer.Opacity == 0 && !footer.IsHitTestVisible, "Footer starts hidden with no invisible hit targets");
var baseFooterHeight = WidgetLayout.Calculate(main.Settings).Height;
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]); Pump(45);
Check(main.TodoReveal > 0 && main.TodoReveal < 1, "Hover has intermediate expansion frames");
Pump(320); main.UpdateLayout();
Check(footer.IsHitTestVisible && Math.Abs(main.VisualContent.ActualHeight - baseFooterHeight - 44) < .1, "Hover expands exactly one editor row");
var emptyInput = Desc<TextBox>(footer).Single();
main.Activate(); emptyInput.Focus(); Pump(40);
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]);Pump(500);
Check(emptyInput.IsKeyboardFocusWithin && main.TodoReveal > .999, "Typing keeps the editor expanded after pointer leave");
Keyboard.ClearFocus();Pump(500);
Check(main.TodoReveal < .001, "Losing input focus allows the footer to collapse");
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]);Pump(320);
emptyInput.Text = "First task from hover footer";
emptyInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); main.UpdateLayout();
Check(main.Settings.Todos.Count == 1 && main.Surface.Layout.Sections.Single(s => s.Kind == WidgetSection.Todo).Height == 32, "A task reserves only its own row");
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]); Pump(500);main.UpdateLayout();
Check(main.TodoReveal < .001 && Math.Abs(main.VisualContent.ActualHeight - WidgetLayout.Calculate(main.Settings).Height) < .1, "Leave removes editor height even with tasks present");
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]);Pump(40);
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]);Pump(200);
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]);Pump(350);
Check(Math.Abs(main.TodoReveal-1)<.001, "Interrupted footer motion settles at the latest gesture");
main.Settings.Todos.Clear(); main.ApplySettings(); main.UpdateLayout();
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]);Pump(500);

tabs.SelectedIndex = 0; dialog.UpdateLayout();
var themeBox = Desc<ComboBox>(dialog).Single(b => b.Items.Contains("深蓝"));
var themeImages = new List<RenderTargetBitmap>();
foreach (var theme in ThemePalette.All) {
    themeBox.SelectedIndex = Array.IndexOf(ThemePalette.All, theme); dialog.UpdateLayout();
    Check(((SolidColorBrush)dialog.Background).Color == Art.Brush(theme.Base).Color, "Settings background follows " + theme.Id);
    themeBox.IsDropDownOpen = true; dialog.UpdateLayout(); Pump(15);
    var option = (ComboBoxItem)themeBox.ItemContainerGenerator.ContainerFromIndex(0);
    Check(((SolidColorBrush)option.Foreground).Color == Art.Brush(theme.Text).Color, "Dropdown popup follows " + theme.Id);
    themeBox.IsDropDownOpen = false;
    tabs.SelectedIndex = 2; dialog.UpdateLayout();
    var editorBox = Desc<TextBox>(dialog).First();
    Check(((SolidColorBrush)editorBox.Foreground).Color == Art.Brush(theme.Text).Color, "Input text has themed contrast " + theme.Id);
    tabs.SelectedIndex = 0; dialog.UpdateLayout();
    if (theme.Id is "navy" or "pink" or "lavender") themeImages.Add(Capture((FrameworkElement)dialog.Content));
}
if (Environment.GetEnvironmentVariable("MIZU_SETTINGS_THEME_GALLERY") is { } gallery) {
    var galleryVisual = new DrawingVisual(); using(var d = galleryVisual.RenderOpen())
        for(int i=0;i<themeImages.Count;i++) d.DrawImage(themeImages[i],new Rect(i*740,0,740,740));
    var result = new RenderTargetBitmap(2220,740,96,96,PixelFormats.Pbgra32);result.Render(galleryVisual);
    var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using var stream=System.IO.File.Create(gallery);encoder.Save(stream);
}
main.Settings.ShowPoetry = true; main.State.Poetry = new("海内存知己，天涯若比邻。", "王勃", "送杜少府之任蜀州", DateTimeOffset.UtcNow); main.ApplySettings();main.UpdateLayout();
Check(main.Surface.Layout.Sections.Any(s => s.Kind == WidgetSection.Poetry), "Poetry appears as an independent section");
if (Environment.GetEnvironmentVariable("MIZU_POETRY_PREVIEW") is { } poemPreview) {
    main.Settings.ShowWeather = main.Settings.ShowDevices = main.Settings.ShowCpu = main.Settings.ShowMemory = main.Settings.ShowTemperature = main.Settings.ShowNetwork = main.Settings.ShowMedia = false;
    main.Settings.Theme = "light"; main.Settings.CornerRadius = 30; main.Settings.TextScale = 1; main.Settings.TextWeight = 400; main.Settings.Scale = 1; main.Settings.SmoothResize = false;
    main.State.Poetry = main.State.Poetry! with { Dynasty = "唐代" }; main.ApplySettings(); main.UpdateLayout();
    var samples = new List<RenderTargetBitmap>();
    typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]); Pump(500); samples.Add(Capture(main.VisualContent));
    typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]); Pump(500); samples.Add(Capture(main.VisualContent));
    main.Settings.Todos.Add(new() { Text = "读完这一页书" }); main.ApplySettings(); main.UpdateLayout();
    typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]); Pump(500); samples.Add(Capture(main.VisualContent));
    var scene = new DrawingVisual(); using(var d=scene.RenderOpen()) {
        d.DrawRectangle(Art.Brush("#DADDD5"),null,new Rect(0,0,1140,240));
        var titles = new[] { "未悬停 · 收起", "悬停 · 展开添加", "已有待办 · 收起输入栏" };
        for(int i=0;i<3;i++) { Art.Text(d,titles[i],20+i*380,15,13,Art.Brush("#40483F")); d.DrawImage(samples[i],new Rect(12+i*380,45,samples[i].PixelWidth/2.0,samples[i].PixelHeight/2.0)); }
    }
    var image = new RenderTargetBitmap(1710,360,144,144,PixelFormats.Pbgra32); image.Render(scene);
    var png = new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=System.IO.File.Create(poemPreview);png.Save(file);
}
var fallbackFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mizu-offline-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(fallbackFolder);
try {
    var fallbackState = new WidgetState();
    var delayedFailure = new FailingPoetryHandler();
    var fallbackEngine = new WidgetEngine(new AppSettings { ShowPoetry = true }, fallbackState, () => {}, new PoetryService(delayedFailure, System.IO.Path.Combine(fallbackFolder,"poetry.json")));
    var operation = fallbackEngine.RefreshPoetryAsync(true); var budget = System.Diagnostics.Stopwatch.StartNew();
    while(delayedFailure.Calls == 0 && budget.ElapsedMilliseconds < 3000) Pump(10);
    Check(fallbackState.Poetry?.Source == "内置公版诗库" && !fallbackState.PoetryFailed, "Preloaded poetry is visible before the network finishes");
    delayedFailure.Release.SetResult(true);
    while(!operation.IsCompleted && budget.ElapsedMilliseconds < 3000) Pump(10);
    operation.GetAwaiter().GetResult();
    Check(fallbackState.PoetryFailed && fallbackState.Poetry?.Source == "内置公版诗库", "Network failure reaches the independent local poetry corpus");
    var previousPoem = fallbackState.Poetry!.Content;
    operation = fallbackEngine.RefreshPoetryAsync(true); budget.Restart();while(!operation.IsCompleted && budget.ElapsedMilliseconds<3000)Pump(10);operation.GetAwaiter().GetResult();
    Check(fallbackState.Poetry!.Content != previousPoem, "Offline refresh changes the selected poem");
    Check(delayedFailure.Calls == 1, "Offline refresh does not wait for another failed network request during backoff");
    fallbackEngine.DisposeAsync().AsTask().GetAwaiter().GetResult();
} finally { System.IO.Directory.Delete(fallbackFolder,true); }
dialog.Close();main.Hide();Console.WriteLine($"PASS {checks} WPF checks: rounded alpha, hover hit targets, todo add/complete, reflow and all settings pages. No driver installed.");app.Shutdown();return 0;
}
}

sealed class FailingPoetryHandler : System.Net.Http.HttpMessageHandler {
    public int Calls;
    public System.Threading.Tasks.TaskCompletionSource<bool> Release = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token) {
        Calls++; await Release.Task.WaitAsync(token);
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
