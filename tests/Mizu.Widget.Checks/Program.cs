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
var dialog=new SettingsWindow(main);dialog.Show();dialog.UpdateLayout();var tabs=Desc<TabControl>(dialog).Single();Check(tabs.Items.Count==6,"Six settings pages");
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
for(int i=0;i<6;i++){tabs.SelectedIndex=i;dialog.UpdateLayout();Check(Desc<ScrollViewer>(dialog).Any(),$"Settings page {i} renders");}

foreach(var id in new[]{"navy","pink","lavender"}) {
settingsLive.Theme=id;main.ApplySettings();main.UpdateLayout();
Check(HoverHints.GetPalette(main.Surface).Id==id,"New theme reaches widget and popup targets: "+id);
}
var beforeIds=main.Surface.VisibleDevices.Select(d=>d.Id).ToArray();
main.State.Devices=main.State.Devices.Select((d,i)=>d with {State=i==0?LinkState.Offline:LinkState.Online}).ToArray();
Check(beforeIds.SequenceEqual(main.Surface.VisibleDevices.Select(d=>d.Id)),"Connection state refresh preserves visible device order");
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
dialog.Close();main.Hide();Console.WriteLine($"PASS {checks} WPF checks: rounded alpha, hover hit targets, todo add/complete, reflow and all settings pages. No driver installed.");app.Shutdown();return 0;
}
}
