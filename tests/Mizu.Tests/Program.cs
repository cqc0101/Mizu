using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mizu.Core;
using Mizu.Core.Devices;
using Mizu.Core.Services;

var count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
var now = DateTimeOffset.Parse("2026-11-13T04:00:00Z");
var q = HidppProtocol.Request(1, 7, 1, 10);
byte[] reply = new byte[20];
new byte[] { 0x11, 1, 7, 0x1a, 82, 8, 0 }.CopyTo(reply, 0);
Check(q.Length == 20 && q[3] == 0x1a, "HID++ packet size and software ID");
Check(HidppProtocol.Matches(reply, q), "Matching HID++ response");
foreach (var position in new[] { 0, 1, 2, 3 })
{ var wrong = reply.ToArray(); wrong[position] = position == 0 ? (byte)0x12 : (byte)(wrong[position] ^ 1); Check(!HidppProtocol.Matches(wrong, q), "Reject unrelated HID++ packet " + position); }
Check(!HidppProtocol.Matches([0x11, 1, 7], q), "Reject truncated HID++ response");
Check(HidppProtocol.Battery(reply, 0x1004) == (82, false), "Unified battery percentage");
var legacy = reply.ToArray(); legacy[6] = 1;
Check(HidppProtocol.Battery(legacy, 0x1000) == (82, true), "Legacy 0x1000 charging");
legacy[6] = 4;
Check(HidppProtocol.Battery(legacy, 0x1000).Charging == true, "Slow recharge also suppresses low-battery notifications");
var coarse = reply.ToArray(); coarse[4] = 0;
Check(HidppProtocol.Battery(coarse, 0x1004).Percent is null, "Coarse battery is not a fabricated percentage");
coarse[5] = 0;
Check(HidppProtocol.Battery(coarse, 0x1004).Percent == 0, "A genuine empty battery is preserved");
var impossible = reply.ToArray(); impossible[4] = 255;
Check(HidppProtocol.Battery(impossible, 0x1004).Percent is null, "Reject out-of-range battery");
byte[] error = [0x10, 1, 0xff, 7, 0x1a, 2, 0];
Check(HidppProtocol.Matches(error, q) && HidppProtocol.IsError(error), "Correlate an explicit HID++ error");

var compx = AtkProtocol.CompxRequest(4);
Check(compx.Length == 17 && compx.Sum(b => b) % 256 == 0x55, "Compx request checksum");
var compxReply = compx.ToArray(); compxReply[5] = 2; compxReply[6] = 64; compxReply[7] = 1;
compxReply[16] = unchecked((byte)(85 - compxReply.Take(16).Sum(x => x)));
Check(AtkProtocol.MatchCompx(compxReply, 4), "Accept Compx battery reply");
Check(!AtkProtocol.MatchCompx(compxReply, 3), "Reject stale Compx command");
compxReply[6]++;
Check(!AtkProtocol.MatchCompx(compxReply, 4), "Reject corrupt Compx checksum");
var recordedBattery = Convert.FromHexString("0804000000024B00101A000000000000D2");
Check(AtkProtocol.MatchCompx(recordedBattery, 4) && recordedBattery[6] == 75 && recordedBattery[7] == 0,
    "Recorded 373B:11FE A9 Ultra Max battery frame");
var recordedPing = Convert.FromHexString("1101001104055A00000000000000000000000000");
Check(HidppProtocol.Matches(recordedPing, HidppProtocol.Request(1, 0, 1, 1, 0, 0, 0x5a)) && recordedPing[4] == 4,
    "Recorded K98M / Bolt HID++ 4.5 long-report ping");
var bit = AtkProtocol.BitMouseRequest(7, 1);
Check(bit.Length == 64 && bit[0] == 8 && bit[2] == 0x72 && bit[5] == 1 && bit[6] == 7, "BitMouse wire offsets include report ID");
var bitReply = bit.ToArray(); bitReply[8] = 57; bitReply[1] = unchecked((byte)bitReply.Skip(2).Sum(x => x));
Check(AtkProtocol.MatchBitMouse(bitReply, bit), "Accept checked BitMouse battery response");
bitReply[2] = 0x73;
Check(!AtkProtocol.MatchBitMouse(bitReply, bit), "Reject unsolicited BitMouse event");
bool guarded = false; try { AtkProtocol.CompxRequest(7); } catch (ArgumentException) { guarded = true; }
Check(guarded, "Prohibit EEPROM writes");
Check(DeviceClassifier.FromLogitech(0) == PeripheralKind.Keyboard && DeviceClassifier.FromLogitech(3) == PeripheralKind.Mouse, "HID++ device classification");
Check(DeviceClassifier.FromMetadata("ATK Headphones") == PeripheralKind.Unknown, "Brand names do not imply mouse");
Check(DeviceClassifier.FromHid(1, 6) == PeripheralKind.Keyboard, "HID usage classification");
var device = new Peripheral("slot1", "Same name", PeripheralKind.Keyboard, LinkKind.Receiver, LinkState.Online, 15, false, "HID++", now);
Check(BatteryService.Merge([device, device with { Id = "slot2" }]).Count == 2, "Identical names on separate receiver slots are not merged");
Check(BatteryService.Merge([device with { ContainerId = "physical1" }, device with { Id = "os", ContainerId = "physical1", Source = "Windows" }]).Count == 1, "Proven physical identity is merged");

var settings = new AppSettings(); var rules = new AlertRules(); var load = new SystemSnapshot(95, 50, 45, "");
Check(rules.Evaluate(settings, load, [device], now).Count == 1, "Fresh low battery notifies immediately");
rules.Succeeded(device.Id, now);
for (var second = 2; second <= 58; second += 2)
    Check(rules.Evaluate(settings, load, [device], now.AddSeconds(second)).Count == 0, "Debounce CPU and battery cooldown " + second);
Check(rules.Evaluate(settings, load, [device], now.AddSeconds(61)).Single().Key == "cpu", "Sustained high CPU triggers");
Check(rules.Evaluate(settings, load, [device], now.AddMinutes(2)).Count == 0, "Suspend gap resets sustained-load timer");
Check(new AlertRules().Evaluate(settings, load, [device with { State = LinkState.Offline }], now).Count == 0, "Offline battery cannot trigger");
Check(new AlertRules().Evaluate(settings, load, [device with { Charging = true }], now).Count == 0, "Charging battery cannot trigger");
Check(new AlertRules().Evaluate(settings, load, [device with { ObservedAt = now.AddMinutes(-4) }], now).Count == 0, "Stale battery cannot trigger");
var retry = new AlertRules(); retry.Failed(device.Id, now);
Check(retry.Evaluate(settings, new(null, null, null, ""), [device], now.AddSeconds(10)).Count == 0, "Failed push backs off");

using var weatherData = JsonDocument.Parse("""
{"current":{"temperature_2m":23,"weather_code":2,"is_day":1,"time":"2026-11-13T13:15","wind_speed_10m":8,"relative_humidity_2m":60},"daily":{"temperature_2m_max":[25],"temperature_2m_min":[18]},"hourly":{"time":["2026-11-13T12:00","2026-11-13T13:00","2026-11-13T14:00","2026-11-13T15:00"],"temperature_2m":[22,23,24,25],"weather_code":[1,2,2,3],"is_day":[1,1,1,1]}}
""");
var weather = WeatherService.Parse(weatherData.RootElement, now);
Check(weather.Hours[0].Time.EndsWith("14:00") && weather.High == 25 && weather.CurrentHour?.Time.EndsWith("13:00") == true, "Forecast uses destination-local time, current period and upcoming hours");

var forecastJson = JsonNode.Parse(weatherData.RootElement.GetRawText())!;
forecastJson["daily"] = JsonNode.Parse("""{"time":["2026-11-13","2026-11-14","2026-11-15"],"temperature_2m_max":[25,24,23],"temperature_2m_min":[18,17,16],"weather_code":[1,2,3]}""");
using var forecastDoc = JsonDocument.Parse(forecastJson.ToJsonString());
var forecast = WeatherService.Parse(forecastDoc.RootElement, now);
Check(forecast.Days.Length == 2 && forecast.Days[0].Date == "11/14" && forecast.Days[0].Code == 2, "Multi-day forecasts start tomorrow in the city's time zone");
for (int mask = 0; mask < 64; mask++) foreach (var compact in new[] { false, true })
{
    var layoutSettings = new AppSettings { ShowWeather = (mask & 1) != 0, ShowDevices = (mask & 2) != 0, ShowCpu = (mask & 4) != 0, ShowMemory = (mask & 8) != 0, ShowTemperature = (mask & 16) != 0, ShowTodo = (mask & 32) != 0, CompactMode = compact, WeatherDaily = true, ForecastDays = 7, ForecastHours = 12 };
    var layout = WidgetLayout.Calculate(layoutSettings);
    Check(layout.Sections.All(s => s.Top >= 32 && s.Height > 0 && s.Top + s.Height <= layout.Height - 20) && layout.Height >= 104, $"Module combination {mask}, compact {compact} fits its bounds");
    Check(layout.Sections.Zip(layout.Sections.Skip(1)).All(pair => pair.Second.Top >= pair.First.Top + pair.First.Height + 16), "Visible module sections never overlap");
}
var emptyWeather = WidgetLayout.Calculate(new AppSettings { ShowDevices = false, ShowCpu = false, ShowMemory = false, ShowTemperature = false, WeatherCurrent = false, WeatherTodayRange = false, WeatherHourly = false, WeatherDaily = false });
Check(emptyWeather.Sections.Length == 0 && emptyWeather.Height == 104, "Empty weather settings leave a recoverable minimum window");
Check(TemperatureDriver.GetUnsupportedReason(System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 17763)) is null, "Bundled temperature driver supports x64 Windows 1809+");
Check(TemperatureDriver.GetUnsupportedReason(System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 14393)) is not null, "Older Windows builds cannot install the bundled driver");
Check(TemperatureDriver.GetUnsupportedReason(System.Runtime.InteropServices.Architecture.X86, new Version(10, 0, 26100)) is not null && TemperatureDriver.GetUnsupportedReason(System.Runtime.InteropServices.Architecture.Arm64, new Version(10, 0, 26100)) is not null, "Driver support uses OS architecture, including emulated apps");
Check(TemperatureDriver.InterpretExitCode(0).State == DriverInstallState.Installed && TemperatureDriver.InterpretExitCode(3010).State == DriverInstallState.RebootRequired && TemperatureDriver.InterpretExitCode(5).State == DriverInstallState.Failed, "Installer success, reboot and failure are distinct");
var installer = Path.GetFullPath("vendor/PawnIO/PawnIO_setup.exe");
Check(File.Exists(installer) && TemperatureDriver.MatchesInstallerHash(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(installer)))), "Bundled installer exactly matches the signed upstream release");
Check(!TemperatureDriver.MatchesInstallerHash(new string('0', 64)), "Tampered installer is rejected before elevation");
var handler = new FakeHandler("{\"code\":200,\"data\":\"accepted\"}");
using var http = new HttpClient(handler);
await new PushPlusChannel("unit-test-only", http).SendAsync(new("test", "标题", "内容"), CancellationToken.None);
Check(handler.Uri == "https://www.pushplus.plus/send" && handler.Body!.Contains("unit-test-only"), "PushPlus uses HTTPS POST with correct payload");
var rejected = new PushPlusChannel("unit-test-only", new HttpClient(new FakeHandler("{\"code\":600}")));
bool failed = false; try { await rejected.SendAsync(new("t", "t", "t"), CancellationToken.None); } catch (HttpRequestException) { failed = true; }
Check(failed, "HTTP 200 alone does not mean PushPlus success");

string temp = Path.Combine(Path.GetTempPath(), "mizu-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var store = new SettingsStore(Path.Combine(temp, "settings.json"));
    settings.Scale = 99; settings.ProtectedPushToken = SettingsStore.Protect("test-private-token"); store.Save(settings);
    var restored = store.Load();
    Check(restored.Scale == 1.5 && SettingsStore.Unprotect(restored.ProtectedPushToken) == "test-private-token", "Bounded settings and DPAPI roundtrip");
    settings.TextScale = 1.2; settings.TextWeight = 600; settings.ShowWeather = false; settings.ShowTodo = true; settings.CornerRadius = 47; settings.ForecastHours = 9; settings.ForecastDays = 6;
    settings.Todos = [new() { Text = "待办一" }, new() { Text = "待办二", Completed = true }];
    store.Save(settings); var custom = store.Load();
    Check(custom.TextScale == 1.2 && custom.TextWeight == 600 && !custom.ShowWeather && custom.ShowTodo && custom.CornerRadius == 47 && custom.ForecastDays == 6 && custom.Todos[1].Completed, "Custom content and todo completion survive settings reload");
    File.WriteAllText(store.FilePath, "{}"); var migrated = store.Load();
    Check(migrated.ShowWeather && migrated.ShowDevices && migrated.ShowCpu && migrated.ShowMemory && !migrated.ShowTodo && migrated.CornerRadius == 30, "Legacy settings keep sensible visible defaults");
    Check(!File.ReadAllText(store.FilePath).Contains("test-private-token"), "Token is not stored in plaintext");
    File.WriteAllText(store.FilePath, "{malformed"); Check(store.Load().Scale == 1, "Corrupt config falls back safely");
    var invalid = new AppSettings { Scale = double.NaN, GlassOpacity = double.PositiveInfinity, Location = new("x", "", null, 999, 0) };
    invalid.Validate(); Check(double.IsFinite(invalid.Scale) && invalid.Location is null, "Reject invalid settings numerics");
}
finally { Directory.Delete(temp, true); }
var typeBounds = new AppSettings { TextScale = double.NaN, TextWeight = 999 }; typeBounds.Validate();
Check(typeBounds.TextScale == 1 && typeBounds.TextWeight == 700, "Invalid typography falls back and clamps safely");
var largeType = new AppSettings { TextScale = 9 }; largeType.Validate();
Check(largeType.TextScale == 1.25, "Font size respects layout limits");
var pager = new DevicePager();
Check(pager.Advance(0, -120, 4, 1000) == 1, "Wheel advances to next device page");
Check(pager.Advance(1, -120, 4, 1030) == 1 && pager.Advance(1, -120, 4, 1300) == 1, "Momentum and last page do not wrap or flicker");
Check(pager.Advance(0, 120, 4, 1600) == 0, "First page does not wrap backwards");
var fineWheel = new DevicePager();
Check(fineWheel.Advance(0, -30, 6, 1000) == 0 && fineWheel.Advance(0, -30, 6, 1010) == 0 && fineWheel.Advance(0, -30, 6, 1020) == 0 && fineWheel.Advance(0, -30, 6, 1030) == 1, "High-resolution wheel deltas accumulate to one page turn");
foreach (var color in new[] { "navy", "pink", "lavender" }) { var themeSetting = new AppSettings { Theme = color }; themeSetting.Validate(); Check(themeSetting.Theme == color, "Preserve color theme " + color); }
Console.WriteLine($"PASS {count} checks. No external notification was sent.");

sealed class FakeHandler(string response) : HttpMessageHandler
{
    public string? Uri; public string? Body;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Uri = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(token); return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") }; }
}
