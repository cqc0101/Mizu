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
static IEnumerable<string> DrawingText(Drawing? drawing) {
if(drawing is GlyphRunDrawing glyph && glyph.GlyphRun.Characters is { } characters)yield return new string(characters.ToArray());
if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var text in DrawingText(child))yield return text;
}
static IEnumerable<GlyphRun> DrawingGlyphs(Drawing? drawing) {
return GlyphPaint(drawing).Select(glyph => glyph.GlyphRun);
}
static IEnumerable<GlyphRunDrawing> GlyphPaint(Drawing? drawing) {
if(drawing is GlyphRunDrawing glyph)yield return glyph;
if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var run in GlyphPaint(child))yield return run;
}
static void Pump(int milliseconds) {
var frame=new System.Windows.Threading.DispatcherFrame();
var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(milliseconds)};
timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
}
static int Run() {
var app=new System.Windows.Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };int checks=0;
void Check(bool result,string message){if(!result)throw new Exception(message);checks++;}
var reachable=typeof(MainWindow).GetMethod("HasReachableWorkArea",BindingFlags.NonPublic|BindingFlags.Static)!;
bool OnScreen(System.Drawing.Rectangle window,params System.Drawing.Rectangle[] areas)=>(bool)reachable.Invoke(null,new object[]{window,areas})!;
Check(!OnScreen(new System.Drawing.Rectangle(2600,200,360,480),new System.Drawing.Rectangle(0,0,1920,1080)),"Disconnected monitor placement is detected");
Check(!OnScreen(new System.Drawing.Rectangle(-350,20,360,480),new System.Drawing.Rectangle(0,0,1920,1080)),"Tiny clipped overlap cannot strand the widget");
Check(OnScreen(new System.Drawing.Rectangle(-40,20,360,480),new System.Drawing.Rectangle(0,0,1920,1080)),"A useful portion remains reachable across a negative-coordinate display edge");
Check(OnScreen(new System.Drawing.Rectangle(2700,200,360,480),new System.Drawing.Rectangle(0,0,1920,1080),new System.Drawing.Rectangle(2560,0,1440,900)),"Widgets on a connected secondary display stay in place");
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
double Luma(Color color) {
    double Channel(byte value) { double c = value / 255.0; return c < .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
    return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
}
double Contrast(Color a, Color b) {
    double first = Luma(a), second = Luma(b);
    return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
}
foreach (var theme in ThemePalette.All)
foreach (double opacity in new[] { .15, .35, .95 })
{
    var settings = new AppSettings
    {
        Theme = theme.Id, GlassOpacity = opacity, CornerRadius = 32, ShowWeather = false, ShowDevices = false,
        ShowCpu = false, ShowMemory = false, ShowTemperature = false, ShowNetwork = true, ShowNetworkProbeIp = true,
        NetworkProbeTargets = ["1.1.1.1"]
    };
    var surface = new WidgetSurface(new WidgetState(), settings) { Width = 356 };
    surface.Height = surface.Layout.Height;
    surface.Measure(new(356, surface.Height)); surface.Arrange(new(0, 0, 356, surface.Height));
    var bitmap = Capture(surface);
    byte[] Pixel(int x, int y) {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(2 * x, 2 * y, 1, 1), pixel, 4, 0);
        return pixel;
    }
    int probeY = (int)surface.Layout.Sections.Single().Top + 60;
    var sample = Pixel(178, probeY);
    var ink = GlyphPaint(VisualTreeHelper.GetDrawing(surface))
        .Single(glyph => glyph.GlyphRun.Characters is { } chars && string.Concat(chars) == "1.1.1.1");
    var inkColor = ((SolidColorBrush)ink.ForegroundBrush).Color;
    foreach (bool whiteWallpaper in new[] { false, true })
    {
        int behind = whiteWallpaper ? 255 - sample[3] : 0;
        var composite = Color.FromRgb((byte)(sample[2] + behind), (byte)(sample[1] + behind), (byte)(sample[0] + behind));
        double ratio = Contrast(composite, inkColor);
        Check(ratio >= 4.5, $"Small text on {theme.Id} at {opacity:P0} over {(whiteWallpaper ? "white" : "black")} measures {ratio:F2}:1");
    }
    if (opacity <= .35)
    {
        Check(sample[3] > Pixel(3, probeY)[3] + 48 && Pixel(0, 0)[3] == 0, $"Low-opacity {theme.Id} retains translucent edges and clear corners");
        int outerSlope = Pixel(7, probeY)[3] - Pixel(3, probeY)[3];
        int middleSlope = Pixel(16, probeY)[3] - Pixel(12, probeY)[3];
        int innerSlope = Pixel(27, probeY)[3] - Pixel(23, probeY)[3];
        Check(middleSlope > Math.Max(Math.Abs(outerSlope), Math.Abs(innerSlope)) + 2,
            $"The low-opacity {theme.Id} feather eases in and out ({outerSlope}, {middleSlope}, {innerSlope})");
    }
}
var settingsLive=new AppSettings { ShowTodo=true,Todos=[new(){Text="First"},new(){Text="Second"}],WeatherDaily=true };
var gpuSettings = new AppSettings { ShowWeather = false, ShowDevices = false, ShowCpu = false, ShowMemory = false, ShowTemperature = false };
var gpuState = new WidgetState { GpuMemory = new GpuMemorySnapshot(1073741824L, null, 4 * 1073741824L, "NVIDIA") };
var gpuSurface = new WidgetSurface(gpuState, gpuSettings) { Width = 356 };
Check(!gpuSurface.Layout.Sections.Any(s => s.Kind == WidgetSection.GpuMemory), "GPU memory remains hidden by default");
gpuSettings.ShowGpuMemory = true;
gpuSurface.Height = gpuSurface.Layout.Height;
gpuSurface.Measure(new Size(356, gpuSurface.Height)); gpuSurface.Arrange(new Rect(0, 0, 356, gpuSurface.Height));
Capture(gpuSurface);
var gpuText = string.Concat(DrawingText(VisualTreeHelper.GetDrawing(gpuSurface)));
Check(gpuSurface.Layout.Sections.Single().Kind == WidgetSection.GpuMemory &&
    gpuText.Contains("1/4 GB") && gpuText.Contains("共享内存") && gpuText.Contains("—"),
    "GPU memory can render independently and uses an explicit placeholder for unsupported shared memory");
gpuSettings.VramDisplayMode = MemoryDisplayMode.Percentage;
gpuSettings.ShowVramTemperature = true;
gpuState.GpuMemory = gpuState.GpuMemory! with { SharedBytes = 1073741824L, SharedCapacityBytes = 8 * 1073741824L, VramCelsius = 68 };
gpuSurface.InvalidateVisual(); gpuSurface.UpdateLayout(); Capture(gpuSurface);
gpuText = string.Concat(DrawingText(VisualTreeHelper.GetDrawing(gpuSurface)));
Check(gpuText.Contains("25%") && gpuText.Contains("12.5%") && gpuText.Contains("68 °C") &&
    gpuSurface.GpuMetricAt(250) == HardwareMetric.VramTemperature, "GPU percentage and memory temperature render together in separate columns: " + gpuText);
gpuSettings.ShowGpuMemory = false;
gpuState.GpuMemory = null;
gpuSurface.InvalidateVisual(); gpuSurface.UpdateLayout(); Capture(gpuSurface);
gpuText = string.Concat(DrawingText(VisualTreeHelper.GetDrawing(gpuSurface)));
Check(gpuSurface.Layout.Sections.Single().Kind == WidgetSection.GpuMemory && gpuText.Contains("显存温度") &&
    gpuText.Contains("—") && !gpuText.Contains("共享内存"), "VRAM temperature can render without usage and shows a placeholder without a sensor");
var ramState = new WidgetState { System = new(25, 50, 45, "") { MemoryUsedBytes = 16 * 1073741824L, MemoryTotalBytes = 32 * 1073741824L } };
var ramSettings = new AppSettings { ShowWeather = false, ShowDevices = false, RamDisplayMode = MemoryDisplayMode.Capacity, TextScale = 1.4 };
var ramSurface = new WidgetSurface(ramState, ramSettings) { Width = 356 };
ramSurface.Height = ramSurface.Layout.Height;
ramSurface.Measure(new Size(356, ramSurface.Height)); ramSurface.Arrange(new Rect(0, 0, 356, ramSurface.Height));
Capture(ramSurface);
Check(string.Concat(DrawingText(VisualTreeHelper.GetDrawing(ramSurface))).Contains("16/32 GB"), "RAM capacities fit the three-column view at a large text scale");
var wiredUiSettings=new AppSettings();var wiredUiState=new WidgetState {Devices=[new Peripheral("wired-test","HHKB Professional",PeripheralKind.Keyboard,LinkKind.Wired,LinkState.Online,null,null,"test",DateTimeOffset.UtcNow){IdentifiedWiredKeyboard=true}]};
var wiredSurface=new WidgetSurface(wiredUiState,wiredUiSettings);
Check(wiredSurface.VisibleDevices.Count==0,"Recognized wired keyboards remain hidden when independent switch is off");
wiredUiSettings.ShowWiredKeyboards=true;Check(wiredSurface.VisibleDevices.Single().Name=="HHKB Professional" && wiredSurface.Layout.Sections.Single(s=>s.Kind==WidgetSection.Devices).Height==78,"Independent wired switch adds one readable keyboard row");
wiredUiSettings.ShowWiredKeyboards=false;Check(wiredSurface.VisibleDevices.Count==0,"Disabling wired switch immediately removes its row despite sleep history");
wiredUiSettings.ShowWiredKeyboards=true;wiredUiSettings.ShowWeather=false;wiredUiSettings.ShowCpu=wiredUiSettings.ShowMemory=wiredUiSettings.ShowTemperature=false;
wiredUiState.Devices=wiredUiState.Devices.Select(d=>d with {Percent=83,Charging=true}).ToArray();
wiredSurface.Width=356;wiredSurface.Height=wiredSurface.Layout.Height;wiredSurface.Measure(new(356,wiredSurface.Height));wiredSurface.Arrange(new Rect(0,0,356,wiredSurface.Height));wiredSurface.UpdateLayout();Capture(wiredSurface);
var wiredText=DrawingText(VisualTreeHelper.GetDrawing(wiredSurface)).ToArray();
Check(wiredText.Any(t=>t.Contains("有线连接")) && !wiredText.Any(t=>t.Contains("83%")),"Wired rendering uses connection status instead of a reported battery percentage");
var weatherSettings=new AppSettings {ShowDevices=false,ShowCpu=false,ShowMemory=false,ShowTemperature=false,WeatherHourly=false,WeatherDaily=false};
var weatherWindow=new MainWindow(weatherSettings,WidgetState.Preview(),preview:true,persistSettings:false);weatherWindow.Show();Pump(60);var weatherHeight=weatherWindow.VisualContent.Height;
weatherSettings.WeatherHourly=true;weatherSettings.ForecastHours=10;weatherWindow.ApplySettings();Pump(70);
Check(weatherWindow.VisualContent.Height>weatherHeight && weatherWindow.VisualContent.Height<weatherHeight+120,"Weather expansion has intermediate heights");
Check((double)weatherWindow.Surface.GetValue(WidgetSurface.WeatherProgressProperty)>0 && (double)weatherWindow.Surface.GetValue(WidgetSurface.WeatherProgressProperty)<1,"Weather content crossfades during geometry transition");
weatherSettings.WeatherHourly=false;weatherSettings.WeatherDaily=true;weatherSettings.ForecastDays=4;weatherWindow.ApplySettings();Pump(350);
Check(Math.Abs(weatherWindow.VisualContent.Height-WidgetLayout.Calculate(weatherSettings,0).Height)<.1 && Math.Abs(weatherWindow.Width-356)<.1,"Interrupted weather switching settles at latest layout without changing scale");
var expandedWeatherHeight = weatherWindow.VisualContent.Height;
weatherSettings.ShowWeather = false; weatherWindow.ApplySettings(); Pump(85);
var fadingWeather = weatherWindow.Surface.Layout.Sections.Single(s => s.Kind == WidgetSection.Weather);
Check(fadingWeather.Opacity is > 0 and < 1 && fadingWeather.Height > 0 && weatherWindow.VisualContent.Height < expandedWeatherHeight,
    "Turning weather off keeps its content in the fading frame while the panel shrinks");
Check(DrawingText(VisualTreeHelper.GetDrawing(weatherWindow.Surface)).Any(t => t.Contains("°")), "Outgoing weather temperature remains drawn while fading");
Pump(320);
Check(!weatherWindow.Surface.Layout.Sections.Any(s => s.Kind == WidgetSection.Weather), "Weather is removed at the end of the transition");
weatherSettings.ShowWeather = weatherSettings.ShowClock = true; weatherWindow.ApplySettings(); Pump(330);
weatherSettings.SectionOrder.Remove(WidgetSection.Clock);
weatherSettings.SectionOrder.Insert(weatherSettings.SectionOrder.IndexOf(WidgetSection.Weather) + 1, WidgetSection.Clock);
weatherWindow.ApplySettings();
bool reorderingWithoutOverlap = true, sawFade = false;
for (int i = 0; i < 10; i++)
{
    Pump(27);
    var frame = weatherWindow.Surface.Layout.Sections.Where(s => s.Opacity > .001).OrderBy(s => s.Top).ToArray();
    reorderingWithoutOverlap &= frame.Zip(frame.Skip(1)).All(pair => pair.First.Top + pair.First.Height <= pair.Second.Top + .01);
    sawFade |= frame.Any(s => s.Opacity is > .001 and < .999);
}
Check(reorderingWithoutOverlap && sawFade, "Module order transition fades without letting visible rows overlap");
Pump(100);
Check(weatherWindow.Surface.Layout.Sections[0].Kind == WidgetSection.Weather, "After the transition the widget uses the saved module order");
weatherWindow.Hide();
var rowDevice=new Peripheral("row-mouse","Mouse A9",PeripheralKind.Mouse,LinkKind.Receiver,LinkState.Online,70,false,"test",DateTimeOffset.UtcNow);
var rowState=new WidgetState {Devices=[rowDevice]};
var rowWindow=new MainWindow(new AppSettings {ShowWeather=false,ShowCpu=false,ShowMemory=false,ShowTemperature=false,HideInactiveDevices=true},rowState,preview:true,persistSettings:false);
rowWindow.Show();rowWindow.UpdateLayout();var oneDeviceHeight=rowWindow.VisualContent.Height;
Check(rowWindow.Surface.Layout.Sections.Single(s=>s.Kind==WidgetSection.Devices).Height==78,"A single visible device does not leave a second row blank");
rowState.Devices=[rowDevice,rowDevice with {Id="row-keyboard",Name="Keyboard K98M",Kind=PeripheralKind.Keyboard}];
typeof(MainWindow).GetMethod("OnStateChanged",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null)!.Invoke(rowWindow,[]);Pump(70);
Check(rowWindow.VisualContent.Height>oneDeviceHeight && rowWindow.VisualContent.Height<oneDeviceHeight+78,"Device arrival has intermediate layout positions");
Pump(320);Check(Math.Abs(rowWindow.VisualContent.Height-oneDeviceHeight-78)<.1,"Keyboard arrival settles at exactly two device rows");rowWindow.Hide();
var main=new MainWindow(settingsLive,WidgetState.Preview(),preview:true,persistSettings:false);main.Show();main.UpdateLayout();
var nativeType=typeof(MainWindow).Assembly.GetType("Mizu.Widget.NativeWindow")!;
var readBounds=nativeType.GetMethod("GetWindowRect",BindingFlags.NonPublic|BindingFlags.Static)!;
foreach(var position in new[]{new Point(220,140),new Point(-120,60)}) {
    main.Left=position.X;main.Top=position.Y;main.UpdateLayout();
    object?[] nativeArgs={new System.Windows.Interop.WindowInteropHelper(main).Handle,null};
    Check((bool)readBounds.Invoke(null,nativeArgs)!,"Native bounds can be read");
    var bounds=(System.Drawing.Rectangle)nativeArgs[1]!;
    var dpi=VisualTreeHelper.GetDpi(main);
    Check(Math.Abs(bounds.Width-main.ActualWidth*dpi.DpiScaleX)<=2 && Math.Abs(bounds.Height-main.ActualHeight*dpi.DpiScaleY)<=2,"Native dimensions are independent of window coordinates");
}
settingsLive.ClickThrough=true;main.ApplySettings();main.Hide();main.ToggleVisible();
Check(main.IsVisible && !settingsLive.ClickThrough,"Tray recovery shows an interactive widget");
var lockControl=Desc<Button>(main).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="锁定面板");
lockControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));main.UpdateLayout();
Check(settingsLive.PanelLocked && !main.Surface.IsHitTestVisible,"Lock blocks the content while leaving the window available to unlock");
Check(Desc<Button>(main).Where(b=>b.Parent is StackPanel p && p.Parent==main.VisualContent).Count(b=>b.Visibility==Visibility.Visible)==1,"Only the unlock control remains in the locked toolbar");
main.ApplySettings();
Check(settingsLive.PanelLocked && lockControl.IsEnabled && lockControl.Visibility==Visibility.Visible,"Settings reflow preserves the accessible unlock control");
lockControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(!settingsLive.PanelLocked && main.Surface.IsHitTestVisible,"Unlock restores content interaction");
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
settingsLive.ShowWeather=settingsLive.ShowDevices=settingsLive.ShowMemory=settingsLive.ShowTemperature=false;settingsLive.ShowTodo=false;main.ApplySettings();Pump(330);main.UpdateLayout();
Check(grid.ActualHeight==WidgetLayout.Calculate(settingsLive).Height && Desc<TextBox>(grid).Count()==0,"Disabling modules reflows window and removes todo editor");
var dialog=new SettingsWindow(main);dialog.Show();dialog.UpdateLayout();var tabs=Desc<TabControl>(dialog).Single();Check(tabs.Items.Count==10,"Ten settings pages including vocabulary, About and sync");
tabs.SelectedIndex=9;dialog.UpdateLayout();
Check(Desc<TextBlock>(dialog).Any(t=>t.Text=="调试与界面预览不联网同步。"),"Sync is off in preview windows");
tabs.SelectedIndex=7;dialog.UpdateLayout();
TextBox WordInput(string name)=>Desc<TextBox>(dialog).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)==name);
WordInput("新单词").Text="serene";WordInput("新单词释义").Text="宁静的";
Desc<Button>(dialog).Single(b=>Equals(b.Content,"添加单词")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(settingsLive.Vocabulary.Single().Word=="serene" && settingsLive.Vocabulary.Single().Meaning=="宁静的","Vocabulary controls add a word and meaning");
var showWords=Desc<CheckBox>(dialog).Single(c=>Equals(c.Content,"在组件上显示单词本"));showWords.IsChecked=true;main.UpdateLayout();
Check(main.Surface.TargetLayout.Sections.Any(s=>s.Kind==WidgetSection.Vocabulary),"Vocabulary toggle adds its section");
var columnSlider = Desc<Slider>(dialog).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "显示列数");
var rowSlider = Desc<Slider>(dialog).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "显示行数");
settingsLive.VocabularyVisibleRows = WidgetLayout.MaximumVocabularyRows;
main.ApplySettings();
Check(rowSlider.Maximum == main.Surface.TargetLayout.VocabularyRowLimit && rowSlider.Value == rowSlider.Maximum &&
    settingsLive.VocabularyVisibleRows == WidgetLayout.MaximumVocabularyRows &&
    Desc<TextBlock>(dialog).Any(t => t.Text.StartsWith("当前上限")), "The settings show the current row cap and preserve the requested rows");
settingsLive.VocabularyVisibleRows = 1;
columnSlider.Value = 2;
Pump(480); main.UpdateLayout();
Check(settingsLive.VocabularyColumns == 2 && main.Surface.TargetLayout.VocabularyColumns == 2 &&
    main.Width > WidgetLayout.BaseWidth * settingsLive.Scale &&
    Math.Abs(main.Width - main.Surface.TargetLayout.Width * settingsLive.Scale) < 1, "The column setting visibly widens the widget");
columnSlider.Value = 1;
Pump(480); main.UpdateLayout();
Check(Math.Abs(main.Width - WidgetLayout.BaseWidth * settingsLive.Scale) < 1, "Returning to one column restores the base width");
Check(Desc<Button>(dialog).Any(b => Equals(b.Content, "导出单词本")), "The settings include vocabulary export");
var meaningSize = Desc<Slider>(dialog).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "中文释义字号");
Pump(330);
Capture(main.VisualContent);
GlyphRun VocabularyGlyph(char firstChar) => DrawingGlyphs(VisualTreeHelper.GetDrawing(main.Surface))
    .First(run => run.Characters is { Count: > 0 } chars && chars[0] == firstChar);
double englishSize = VocabularyGlyph('s').FontRenderingEmSize;
double chineseSize = VocabularyGlyph('宁').FontRenderingEmSize;
double defaultRowHeight = main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Vocabulary).Height;
meaningSize.Value = meaningSize.Maximum;
Pump(330); main.UpdateLayout(); Capture(main.VisualContent);
var adjustedRow = main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Vocabulary);
Check(settingsLive.VocabularyMeaningFontSize == meaningSize.Maximum && VocabularyGlyph('宁').FontRenderingEmSize > chineseSize &&
    VocabularyGlyph('s').FontRenderingEmSize == englishSize && adjustedRow.Height > defaultRowHeight,
    "The actual Chinese font-size slider expands the definition and its row while keeping the English glyph size");
meaningSize.Value = AppSettings.DefaultVocabularyMeaningFontSize;
Pump(330);
WordInput("新单词").Text="SERENE";Desc<Button>(dialog).Single(b=>Equals(b.Content,"添加单词")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(settingsLive.Vocabulary.Count==1,"Duplicate word does not replace existing notes");
var deleteWord = Desc<Button>(dialog).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="删除单词");
Check(deleteWord.Content is IconView && deleteWord.Width == deleteWord.Height, "Vocabulary delete uses a square icon button");
deleteWord.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(settingsLive.Vocabulary.Count==0 && !main.Surface.TargetLayout.Sections.Any(s=>s.Kind==WidgetSection.Vocabulary),"Deleting the final word removes its reserved space");
tabs.SelectedIndex=8;dialog.UpdateLayout();
Check(Desc<TextBlock>(dialog).Any(t=>t.Text=="v0.6.0") && Desc<TextBlock>(dialog).Any(t=>t.Text=="v0.5.3"),"Packaged version history contains current and prior releases");
var autoUpdates=Desc<CheckBox>(dialog).Single(c=>Equals(c.Content,"自动检查更新"));
var previewUpdates=Desc<CheckBox>(dialog).Single(c=>Equals(c.Content,"接收预览版更新"));
autoUpdates.IsChecked=false;previewUpdates.IsChecked=false;
Check(!settingsLive.AutoCheckUpdates && !settingsLive.ReceivePreviewUpdates,"Update preferences apply from the actual About controls");
autoUpdates.IsChecked=true;previewUpdates.IsChecked=true;
Check(settingsLive.AutoCheckUpdates && settingsLive.ReceivePreviewUpdates,"Both update preferences can be reenabled");
Desc<Button>(dialog).Single(b=>Equals(b.Content,"检查更新")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(Desc<TextBlock>(dialog).Any(t=>t.Text.Contains("不联网检查更新")),"Preview UI does not make real update requests");
dialog.ShowUpdates();Check(tabs.SelectedIndex==8,"Tray update entry selects the About update panel");
tabs.SelectedIndex=0;dialog.UpdateLayout();
var sliders=Desc<Slider>(dialog).Where(s=>s.Maximum!=720).ToArray(); sliders[0].Value=35;dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);Check(Math.Abs(settingsLive.GlassOpacity-.35)<.001,"Opacity control applies immediately");
sliders[1].Value=64;dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);Check(settingsLive.CornerRadius==64 && ((RectangleGeometry)grid.Clip).RadiusX==64,"Radius control updates visual and content clipping together");
sliders[2].Value=113.45;Pump(55);
var animated=(ScaleTransform)grid.RenderTransform;
Check(!sliders[2].IsSnapToTickEnabled && Math.Abs(settingsLive.Scale-1.1345)<.0001,"Scale slider supports fractional percentages");

Check(animated.ScaleX>1 && animated.ScaleX<1.1345 && animated.HasAnimatedProperties,"Scale has intermediate animation frames");
sliders[2].Value=86.3;Pump(25);sliders[2].Value=110.7;Pump(240);
Check(Math.Abs(main.Width-356*1.107)<1 && Math.Abs(animated.ScaleX-1.107)<.0001,
    $"Interrupted resize settles on latest requested size (width {main.Width:F2}, scale {animated.ScaleX:F5}, configured {settingsLive.Scale:F5})");
Check(!animated.HasAnimatedProperties && grid.CacheMode is null,"Settled widget restores crisp vector rendering");
settingsLive.ShowNetwork=true;settingsLive.NetworkProbeTargets=Enumerable.Repeat("1.1.1.1",8).ToList();settingsLive.ShowVocabulary=true;settingsLive.VocabularyVisibleRows=8;
settingsLive.Vocabulary=Enumerable.Range(0,8).Select(i=>new VocabularyEntry{Word="word"+i}).ToList();main.ApplySettings();
sliders[2].Value=80;Pump(240);sliders[2].Value=120;Pump(260);
Check(Math.Abs(main.Width-356*1.2)<1 && Math.Abs(animated.ScaleX-1.2)<.001,"Tall content can still grow from eighty to one hundred twenty percent");
var stableWidth=main.Width;settingsLive.ShowMedia=true;main.ApplySettings();Pump(220);settingsLive.ShowMedia=false;main.ApplySettings();Pump(220);
Check(Math.Abs(main.Width-stableWidth)<.1 && Math.Abs(animated.ScaleX-1.2)<.001,"Media toggle preserves user-selected scale and width");
settingsLive.ShowNetwork=false;settingsLive.NetworkProbeTargets=["1.1.1.1"];settingsLive.Vocabulary.Clear();settingsLive.ShowVocabulary=false;main.ApplySettings();
var originalTheme=settingsLive.Theme;settingsLive.Theme="navy";main.ApplySettings();
Check(((SolidColorBrush)dialog.Background).Color==Art.Brush(ThemePalette.Get("navy").Base).Color,"Externally rotated theme immediately reaches the open settings window");
settingsLive.Theme=originalTheme;main.ApplySettings();
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
main.ApplySettings(); Pump(330); main.UpdateLayout();
var mediaButtons = Desc<Button>(main).Where(b => new[] { "上一首", "暂停", "下一首" }.Contains(System.Windows.Automation.AutomationProperties.GetName(b))).ToArray();
Check(mediaButtons.Length == 3, "Media has three accessible controls");
Check(mediaButtons.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "下一首").IsEnabled == false, "Unsupported media commands are disabled");
Check(main.Surface.TargetLayout.Sections.Any(s => s.Kind == WidgetSection.Network) && main.Surface.TargetLayout.Sections.Any(s => s.Kind == WidgetSection.Media), "Both new modules reflow into widget");
main.Settings.NetworkProbeTargets = ["192.0.2.1", "2001:db8::1"]; main.Settings.ShowNetworkProbeIp = true;
main.State.Network = main.State.Network! with { Targets = [new("192.0.2.1",0,12,30),new("2001:db8::1",10,25,30)] };main.ApplySettings();Pump(330);main.UpdateLayout();
Check(main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Network).Height==164 && main.Surface.NetworkHint.Contains("192.0.2.1"),"Multiple network probe rows reserve title, labels and IP headings");
main.Settings.ShowNetworkProbeIp=false;main.ApplySettings();Pump(330);main.UpdateLayout();
Check(main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Network).Height==124 && !main.Surface.NetworkHint.Contains("192.0.2.1") && !main.Surface.NetworkHint.Contains("2001:db8::1"),"Hiding probe IPs redacts both display headings and hover details");
tabs.SelectedIndex=1;typeof(SettingsWindow).GetMethod("BuildProbeList",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[]);dialog.UpdateLayout();
var probeInput=Desc<TextBox>(dialog).Single(t=>string.IsNullOrEmpty(t.Text));probeInput.Text="203.0.113.8";
Desc<Button>(dialog).Single(b=>Equals(b.Content,"添加 IP")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));dialog.UpdateLayout();
Check(main.Settings.NetworkProbeTargets!.Count==3 && main.Settings.NetworkProbeTargets.Contains("203.0.113.8"),"Settings add a custom probe through the actual control");
Desc<Button>(dialog).First(b=>b.ToolTip is ToolTip t && Equals(t.Content,"删除探测 IP")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));dialog.UpdateLayout();
Check(main.Settings.NetworkProbeTargets!.Count==2,"Settings remove a configured probe through its row control");
main.Settings.ShowMedia = false; main.Settings.ShowNetwork = false; main.ApplySettings(); main.UpdateLayout();
Check(!main.Surface.TargetLayout.Sections.Any(s => s.Kind is WidgetSection.Media or WidgetSection.Network), "Disabling modules releases their layout space");
main.Settings.ShowClock=true;main.Settings.ShowTodo=true;main.Settings.ShowVocabulary=true;
main.Settings.Vocabulary=[new(){Word="serene",Meaning="宁静的"},new(){Word="gentle",Meaning="温柔的"},new(){Word="bloom",Meaning="绽放"}];
main.Settings.VocabularyVisibleRows=2;main.ApplySettings();Pump(330);main.UpdateLayout();
Check(main.Surface.TargetLayout.Sections[0].Kind==WidgetSection.Clock && main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Vocabulary).Height==132,"Clock and two word groups occupy separate sections");
var scrollVocabulary = typeof(WidgetSurface).GetMethod("ScrollVocabulary", BindingFlags.NonPublic | BindingFlags.Instance)!;
double WordOffset() => (double)main.Surface.GetValue(WidgetSurface.VocabularyOffsetProperty);
Point WordPosition(string word) => DrawingGlyphs(VisualTreeHelper.GetDrawing(main.Surface))
    .First(run => run.Characters is { } characters && string.Concat(characters) == word).BaselineOrigin;
double WordBaseline(string word) => WordPosition(word).Y;
double initialBaseline = WordBaseline("gentle");
scrollVocabulary.Invoke(main.Surface, [-120]);
Pump(45);
double intermediate = WordOffset();
Check(intermediate is > 0 and < 1 && WordBaseline("gentle") < initialBaseline, "Wheel input moves rendered words through intermediate vertical positions");
scrollVocabulary.Invoke(main.Surface, [120]);
Pump(230);
Check(WordOffset() == 0 && Math.Abs(WordBaseline("gentle") - initialBaseline) < .01, "Reversing an in-progress scroll returns smoothly to the first row");
scrollVocabulary.Invoke(main.Surface, [-1200]);
Pump(330);
double settledOffset = WordOffset();
double viewportHeight = main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Vocabulary).Height;
Check(Math.Abs(settledOffset - 1) < .001 && viewportHeight == 132,
    $"Fast scrolling stops on the final row and keeps the vocabulary viewport height: {settledOffset}, {viewportHeight}");
main.Settings.VocabularyColumns = 2;main.ApplySettings();Pump(330);main.UpdateLayout();
var topLeftWord = WordPosition("serene");
var topRightWord = WordPosition("gentle");
var secondRowWord = WordPosition("bloom");
Check(topRightWord.X > topLeftWord.X && topRightWord.Y == topLeftWord.Y &&
    secondRowWord.X == topLeftWord.X && secondRowWord.Y > topLeftWord.Y && WordOffset() == 0,
    "The rendered grid holds two words in the top row and the remaining word in the next row");
main.Settings.VocabularyColumns = 1;main.ApplySettings();Pump(330);
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]);Pump(320);
Button AddChoice(string name)=>Desc<Button>(main).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name);
Check(AddChoice("添加事件").IsVisible && AddChoice("添加单词").IsVisible,"Both enabled add actions appear side by side");
AddChoice("添加单词").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));main.UpdateLayout();
var inlineWord=Desc<TextBox>(main).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="单词");
var inlineMeaning=Desc<TextBox>(main).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="注释");
main.Activate();inlineWord.Focus();Pump(260);inlineWord.Text="quiet";
inlineWord.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});Pump(20);
Check(inlineMeaning.IsKeyboardFocusWithin,"Word Enter moves focus to the separate note row");
inlineMeaning.Text="安静的";inlineMeaning.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});Pump(260);
Check(main.TodoReveal>.99 && main.VisualContent.Height-WidgetLayout.Calculate(main.Settings).Height>80,"Two-row word editor reserves its full animated height");
Check(main.Settings.Vocabulary.Any(w=>w.Word=="quiet" && w.Meaning=="安静的"),"Hover editor saves words without opening settings");
main.Settings.VocabularyHideMeaning=true;main.ApplySettings();Pump(330);main.UpdateLayout();Capture(main.VisualContent);
Check(main.Surface.TargetLayout.Sections.Single(s=>s.Kind==WidgetSection.Vocabulary).Height==84,"Hidden Chinese meanings shrink the displayed groups");
main.Settings.ShowTodo=false;main.ApplySettings();
Check(!Desc<Button>(main).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="添加事件"),"Disabled todo feature removes its hover action");
main.Settings.ShowClock=false;main.Settings.ShowVocabulary = false; main.Settings.ShowTodo = true; main.Settings.Todos.Clear(); main.ApplySettings(); main.UpdateLayout();
Keyboard.ClearFocus();typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[false]);Pump(500);
main.Settings.HideInactiveDevices=true;Check(main.Surface.VisibleDevices.All(d=>d.DisplayDimmed.HasValue),"Channel filtering retains the timed sleep display policy");main.Settings.HideInactiveDevices=false;
tabs.SelectedIndex=1;dialog.UpdateLayout();
typeof(SettingsWindow).GetMethod("MoveSection",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,new object[]{WidgetSection.Vocabulary,WidgetSection.Weather,false});
Check(main.Settings.SectionOrder.IndexOf(WidgetSection.Vocabulary)<main.Settings.SectionOrder.IndexOf(WidgetSection.Weather),"Reordering settings updates the persisted module order");
Pump(230);
var orderBefore=main.Settings.SectionOrder.ToArray();var dragKind=orderBefore[0];
var lastOrderSlot=orderBefore.Length - 1;
var orderList=Desc<ListBox>(dialog).Single();
typeof(SettingsWindow).GetMethod("BeginOrderPreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[dragKind]);
typeof(SettingsWindow).GetMethod("PreviewSectionOrder",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[dragKind,lastOrderSlot]);
Check(main.Settings.SectionOrder.SequenceEqual(orderBefore) && Equals(((ListBoxItem)orderList.Items[lastOrderSlot]).Tag,dragKind),"Drag preview changes visible order without saving settings");
Check(orderList.Items.Cast<ListBoxItem>().Any(row=>row.RenderTransform is TranslateTransform offset && offset.HasAnimatedProperties),"Displaced sorting rows have movement animations");
typeof(SettingsWindow).GetMethod("FinishOrderPreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[false]);Pump(230);
Check(orderList.Items.Cast<ListBoxItem>().Select(row=>(WidgetSection)row.Tag).SequenceEqual(orderBefore),"Cancel restores original order");
typeof(SettingsWindow).GetMethod("BeginOrderPreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[dragKind]);
typeof(SettingsWindow).GetMethod("PreviewSectionOrder",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[dragKind,lastOrderSlot]);
typeof(SettingsWindow).GetMethod("FinishOrderPreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,[true]);Pump(330);
Check(main.Settings.SectionOrder.Last()==dragKind,"Drop commits previewed order");
Check(!main.Surface.TargetLayout.Sections.Any(s => s.Kind == WidgetSection.Todo), "Empty todo has no body section or reserved footer");
var footer = (FrameworkElement)typeof(MainWindow).GetField("todoEditorHost",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(main)!;
Check(footer.Opacity == 0 && !footer.IsHitTestVisible, "Footer starts hidden with no invisible hit targets");
var baseFooterHeight = WidgetLayout.Calculate(main.Settings).Height;
var revealSamples = new List<double> { main.TodoReveal };
var revealDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(MainWindow.TodoRevealProperty, typeof(MainWindow));
EventHandler observeReveal = (_, _) => revealSamples.Add(main.TodoReveal);
revealDescriptor.AddValueChanged(main, observeReveal);
typeof(MainWindow).GetMethod("SetHover",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[true]); Pump(45);
Check(main.TodoReveal > 0 && main.TodoReveal < 1, "Hover has intermediate expansion frames");
Pump(320); main.UpdateLayout();
revealDescriptor.RemoveValueChanged(main, observeReveal);
Check(revealSamples.Count > 2 && revealSamples.Zip(revealSamples.Skip(1), (a,b) => b >= a - .00001).All(x=>x), "Footer expansion never flashes back to its base value at animation completion");
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
Check(main.Settings.Todos.Count == 1 && main.Surface.TargetLayout.Sections.Single(s => s.Kind == WidgetSection.Todo).Height == 32, "A task reserves only its own row");
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
Check(main.Surface.TargetLayout.Sections.Any(s => s.Kind == WidgetSection.Poetry), "Poetry appears as an independent section");
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
var historySettings = new AppSettings { ShowGpuMemory = true, TemperatureEnabled = true };
var historyState = new WidgetState();
var historyEnd = DateTimeOffset.UtcNow;
for (int i = 0; i <= 1800; i++) {
    var time = historyEnd.AddSeconds((i - 1800) * 2);
    double cpu = 22 + Math.Sin(i * .027) * 8 + Math.Max(0, Math.Sin(i * .123)) * 24;
    historyState.HardwareHistory.RecordSystem(time, new(i is >= 1580 and < 1590 ? null : cpu, 42 + Math.Sin(i * .009) * 4, 51 + cpu * .23, ""), historySettings);
    if (i % 5 == 0) historyState.HardwareHistory.RecordGpu(time, new((long)((410 + Math.Sin(i * .019) * 95) * 1048576), (long)((110 + Math.Sin(i * .011) * 30) * 1048576), null, "Preview data"), historySettings);
}
var historyMain = new MainWindow(historySettings, historyState, preview:true, persistSettings:false);
historyMain.OpenHardwareHistory();
Pump(60);
var historyWindow = app.Windows.OfType<HardwareHistoryWindow>().Single();
var historyChart = Desc<HardwareHistoryChart>(historyWindow).Single();
Check(historyChart.Metric == HardwareMetric.Cpu && historyChart.Samples.Count > 400, "History entry opens live fifteen-minute CPU data");
historyMain.OpenHardwareHistory(HardwareMetric.GpuDedicated);
Check(app.Windows.OfType<HardwareHistoryWindow>().Count() == 1 && historyChart.Metric == HardwareMetric.GpuDedicated, "Hardware entry reuses the existing window and selects the clicked metric");
Desc<Button>(historyWindow).Single(b=>Equals(b.Content,"5 分钟")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
Check(historyChart.Range == TimeSpan.FromMinutes(5) && historyChart.Samples.Count <= 151, "Range selector changes the actual displayed samples");
var historyImages = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "artifacts", "history-preview");
System.IO.Directory.CreateDirectory(historyImages);
foreach (var themeId in new[] {"light","dark","navy","pink","lavender"}) {
    historySettings.Theme = themeId; historyMain.ApplySettings();
    historyWindow.SelectMetric(HardwareMetric.Cpu);
    Desc<Button>(historyWindow).Single(b=>Equals(b.Content,"15 分钟")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    Pump(180); historyWindow.UpdateLayout();
    Check(historyChart.Palette.Id == themeId, "Open history follows theme " + themeId);
    var historyRoot = (Grid)historyWindow.Content; var margin = historyRoot.Margin;
    var scene = new DrawingVisual();
    double imageWidth = historyRoot.ActualWidth + margin.Left + margin.Right, imageHeight = historyRoot.ActualHeight + margin.Top + margin.Bottom;
    using (var drawing = scene.RenderOpen()) {
        drawing.DrawRectangle(historyWindow.Background, null, new Rect(0, 0, imageWidth, imageHeight));
        drawing.DrawRectangle(new VisualBrush(historyRoot), null, new Rect(margin.Left, margin.Top, historyRoot.ActualWidth, historyRoot.ActualHeight));
    }
    var historyBitmap = new RenderTargetBitmap((int)(imageWidth * 2), (int)(imageHeight * 2), 192, 192, PixelFormats.Pbgra32); historyBitmap.Render(scene);
    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(historyBitmap));
    using var output = System.IO.File.Create(System.IO.Path.Combine(historyImages, themeId + ".png")); png.Save(output);
}
historyWindow.Width = historyWindow.MinWidth; historyWindow.Height = historyWindow.MinHeight; Pump(50);
foreach (var metric in Enum.GetValues<HardwareMetric>()) {
    historyWindow.SelectMetric(metric); historyWindow.UpdateLayout();
    Check(historyChart.ActualWidth > 500 && historyChart.ActualHeight >= 190, "All metrics render at the minimum window size: " + metric);
    var root = (FrameworkElement)historyWindow.Content;
    var historyFooter = Desc<TextBlock>(root).Single(t=>t.Text.StartsWith("悬停曲线"));
    Check(historyFooter.TranslatePoint(new Point(0, historyFooter.ActualHeight), root).Y <= root.ActualHeight + 1, "The minimum window does not clip its footer: " + metric);
    Capture(historyWindow);
}
historyWindow.SelectMetric(HardwareMetric.Cpu);
historyState.HardwareHistory.RecordSystem(DateTimeOffset.UtcNow, new(71, 45, 63, ""), historySettings);
Check(historyChart.Samples.Last().Value == 71, "An open history curve receives new samples without reopening");
historySettings.RecordHardwareHistory = false; historyMain.ApplySettings();
Check(Desc<TextBlock>(historyWindow).Any(t=>t.Text.StartsWith("记录已暂停")), "Paused recording is visible in the history window");
var pausedNow = historyChart.Now; Pump(25);
historyState.HardwareHistory.RecordSystem(DateTimeOffset.UtcNow, new(95, 95, 95, ""), historySettings);
bool pausedTimelineAdvances = historyChart.Now > pausedNow;
historyWindow.MinHeight = 280; historyWindow.Height = 360; Pump(50); historyWindow.UpdateLayout();
var compactFooter = Desc<TextBlock>(historyWindow).Single(t=>t.Text.StartsWith("悬停曲线"));
compactFooter.BringIntoView(); Pump(40);
var compactRoot = (FrameworkElement)historyWindow.Content;
var footerBottom = compactFooter.TranslatePoint(new Point(0, compactFooter.ActualHeight), compactRoot).Y;
bool compactFooterReachable = footerBottom <= compactRoot.ActualHeight + 1 &&
    Desc<ScrollViewer>(historyWindow).Any(s=>s.ScrollableHeight > 0 && s.VerticalOffset > 0);
historyWindow.Close();
historyState.HardwareHistory.RecordSystem(DateTimeOffset.UtcNow.AddSeconds(2), new(1, 2, 3, ""), new AppSettings());
Check(!app.Windows.OfType<HardwareHistoryWindow>().Any(), "History window closes cleanly while sampling continues");
historyMain.Hide();
var gapHistory = new HardwareHistory();
var gapSettings = new AppSettings();
var gapStart = DateTimeOffset.UtcNow.AddSeconds(-20);
gapHistory.RecordSystem(gapStart, new(20, 30, null, ""), gapSettings);
gapSettings.RecordHardwareHistory = false;
gapHistory.RecordSystem(gapStart.AddSeconds(2), new(40, 30, null, ""), gapSettings);
gapSettings.RecordHardwareHistory = true;
gapHistory.RecordSystem(gapStart.AddSeconds(8), new(60, 30, null, ""), gapSettings);
var gapChart = new HardwareHistoryChart { Width = 600, Height = 280 };
gapChart.Update(HardwareMetric.Cpu, gapHistory.Read(HardwareMetric.Cpu, gapStart.AddSeconds(20), TimeSpan.FromSeconds(20)),
    gapStart.AddSeconds(20), TimeSpan.FromSeconds(20), ThemePalette.All[0]);
gapChart.Measure(new(600,280)); gapChart.Arrange(new(0,0,600,280)); gapChart.UpdateLayout();
var gapBitmap = Capture(gapChart);
var gapPixels = new byte[gapBitmap.PixelWidth*gapBitmap.PixelHeight*4]; gapBitmap.CopyPixels(gapPixels,gapBitmap.PixelWidth*4,0);
bool shortPauseStaysBlank = true;
for(int y=298;y<=302;y++)for(int x=310;x<=314;x++)if(gapPixels[(y*gapBitmap.PixelWidth+x)*4+3]>20)shortPauseStaysBlank=false;
var emptyHistorySettings = new AppSettings();
var emptyHistoryMain = new MainWindow(emptyHistorySettings, preview:true, persistSettings:false);
emptyHistoryMain.OpenHardwareHistory(); Pump(30);
var emptyHistoryWindow = app.Windows.OfType<HardwareHistoryWindow>().Single();
var emptyHistoryChart = Desc<HardwareHistoryChart>(emptyHistoryWindow).Single();
emptyHistorySettings.ShowCpu = false; emptyHistoryMain.ApplySettings();
bool hiddenMetricReplaced = emptyHistoryChart.Metric == HardwareMetric.Memory;
emptyHistoryWindow.Close(); emptyHistoryMain.Hide();
Check(pausedTimelineAdvances, "Paused recording advances the displayed time window");
Check(compactFooterReachable, "An undersized history viewport can scroll the footer into view");
Check(shortPauseStaysBlank, "A short recording pause has no interpolated line or area fill");
Check(hiddenMetricReplaced, "Closing an empty selected metric switches to a visible metric");
dialog.Close();main.Hide();Console.WriteLine($"PASS {checks} WPF checks: rounded alpha, hover hit targets, todo add/complete, reflow, settings and hardware history. No driver installed.");app.Shutdown();return 0;
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
