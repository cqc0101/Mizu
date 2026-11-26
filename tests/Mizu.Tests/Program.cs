using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mizu.Core;
using Mizu.Core.Devices;
using Mizu.Core.Services;

try {
var count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
var now = DateTimeOffset.Parse("2026-11-13T04:00:00Z");
var utcAlarm = DesktopClock.NextAlarm(new TimeOnly(3, 45), now, TimeZoneInfo.Utc);
Check(utcAlarm == DateTimeOffset.Parse("2026-11-14T03:45:00Z"), "A passed alarm time rolls to tomorrow");
Check(DesktopClock.NextAlarm(new TimeOnly(4, 30), now, TimeZoneInfo.Utc) == now.AddMinutes(30), "A future alarm keeps today's date");
var desktopClock = new DesktopClock
{
    Alarms = [new(Guid.NewGuid(), new(7, 20), "会议", false, now.AddSeconds(4)),
        new(Guid.NewGuid(), new(8, 30), "", true, now.AddSeconds(4))],
    Countdown = new DesktopTimer(10).Start(now),
    Focus = new DesktopTimer(60).Start(now),
    BreakMinutes = 2
};
desktopClock.Countdown = desktopClock.Countdown.Pause(now.AddSeconds(3));
Check(desktopClock.Countdown.Remaining(now.AddMinutes(1)) == 7, "A paused countdown keeps its remaining time");
desktopClock.Countdown = desktopClock.Countdown.Start(now.AddSeconds(5));
Check(desktopClock.Countdown.RemainingFraction(now.AddSeconds(5)) == .7 &&
    Math.Abs(desktopClock.Countdown.RemainingFraction(now.AddSeconds(7).AddMilliseconds(500)) - .45) < .000001,
    "The circle follows the remaining portion of the original duration after resuming");
Check(desktopClock.CollectReminders(now.AddSeconds(4)).Count == 2 && desktopClock.Alarms[0].DueAt is null &&
    desktopClock.Alarms[1].DueAt > now.AddSeconds(4), "Each due alarm fires once and daily alarms get a new deadline");
desktopClock.ShowCountdownDial = false;
var desktopTools = new AppSettings { StickyNote = "第一行\n第二行", StickyNoteTopmost = true, DesktopClock = desktopClock };
var restoredTools = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(desktopTools))!;
restoredTools.Validate();
Check(restoredTools.StickyNote == desktopTools.StickyNote && restoredTools.DesktopClock.Countdown.DueAt == now.AddSeconds(12),
    "Local settings preserve a multiline note, alarms and the running countdown");
Check(restoredTools.StickyNoteTopmost && !restoredTools.DesktopClock.ShowCountdownDial && restoredTools.DesktopClock.ShowFocusDial,
    "Sticky note pin and independent clock dial preferences are preserved");
Check(restoredTools.DesktopClock.CollectReminders(now.AddSeconds(12)).SequenceEqual(["倒计时结束"]) &&
    restoredTools.DesktopClock.Countdown.Remaining(now.AddSeconds(12)) == 0 &&
    restoredTools.DesktopClock.Countdown.RemainingFraction(now.AddSeconds(12)) == 0 &&
    restoredTools.DesktopClock.CollectReminders(now.AddSeconds(13)).Count == 0, "Countdown reaches zero exactly once across restarts");
Check(restoredTools.DesktopClock.CollectReminders(now.AddMinutes(1)).SequenceEqual(["专注结束"]) &&
    restoredTools.DesktopClock.OnBreak && restoredTools.DesktopClock.Focus.DurationSeconds == 120 &&
    restoredTools.DesktopClock.Focus.DueAt is null, "Finishing a focus session prepares the configured break");
var monitorSettings = new AppSettings { TemperatureEnabled = true, ShowGpuMemory = true };
const long gib = 1024L * 1024 * 1024;
Check(MemoryDisplay.Percentage(gib, 4 * gib) == 25 && MemoryDisplay.Percentage(0, 4 * gib) == 0, "Capacity percentage distinguishes zero usage from missing readings");
Check(MemoryDisplay.Percentage(gib, null) is null && MemoryDisplay.Percentage(gib, 0) is null && MemoryDisplay.Percentage(2 * gib, gib) is null,
    "Missing, zero or inconsistent memory capacities never produce a misleading percentage");
var ramReading = new TelemetryService().Sample(false);
Check(ramReading.MemoryTotalBytes is > 0 && ramReading.MemoryUsedBytes >= 0 &&
      ramReading.MemoryPercent == MemoryDisplay.Percentage(ramReading.MemoryUsedBytes, ramReading.MemoryTotalBytes),
    "Physical RAM capacity and percentage come from the same Windows sample");
var tempSettings = new AppSettings { ShowVramTemperature = true, ShowGpuMemory = false, ShowCpu = false, ShowMemory = false, ShowTemperature = false };
var tempHistory = new HardwareHistory();
tempHistory.RecordGpu(now, new(null, null, null, "") { VramCelsius = 62.5 }, tempSettings);
tempHistory.RecordGpu(now.AddSeconds(5), null, tempSettings);
Check(tempHistory.Read(HardwareMetric.VramTemperature, now.AddSeconds(5), HardwareHistory.Retention).Select(s => s.Value).SequenceEqual(new double?[] {62.5, null}) &&
    tempHistory.Read(HardwareMetric.GpuDedicated, now.AddSeconds(5), HardwareHistory.Retention).Count == 0,
    "VRAM temperature records independently and unsupported samples leave gaps");
var hardware = new HardwareHistory();
hardware.RecordSystem(now, new(24, 48, 53, ""), monitorSettings);
hardware.RecordSystem(now.AddSeconds(2), new(null, 49, null, ""), monitorSettings);
Check(hardware.Read(HardwareMetric.Cpu, now.AddSeconds(2), TimeSpan.FromMinutes(5)).Select(s => s.Value).SequenceEqual(new double?[] {24, null}), "Missing CPU readings remain gaps rather than zero or a repeated reading");
Check(hardware.Read(HardwareMetric.CpuTemperature, now.AddSeconds(2), TimeSpan.FromMinutes(5)).Last().Value is null, "Missing temperature remains a gap");
hardware.RecordGpu(now, new(400 * 1048576L, null, null, "NVIDIA"), monitorSettings);
hardware.RecordGpu(now.AddSeconds(5), new(400 * 1048576L, null, null, "NVIDIA"), monitorSettings);
Check(hardware.Read(HardwareMetric.GpuDedicated, now.AddSeconds(5), TimeSpan.FromMinutes(5)).Count == 2, "Stable GPU memory still records every actual sample");
Check(hardware.Read(HardwareMetric.GpuShared, now.AddSeconds(5), TimeSpan.FromMinutes(5)).All(s => s.Value is null), "Unsupported shared GPU memory is never invented");
monitorSettings.RecordHardwareHistory = false;
hardware.RecordSystem(now.AddSeconds(4), new(90, 90, 90, ""), monitorSettings);
hardware.RecordGpu(now.AddSeconds(10), null, monitorSettings);
Check(hardware.Read(HardwareMetric.Cpu, now.AddSeconds(10), HardwareHistory.Retention).Count == 2 && hardware.Read(HardwareMetric.GpuDedicated, now.AddSeconds(10), HardwareHistory.Retention).Count == 2, "Pause stops both system and GPU history without discarding prior readings");
monitorSettings.RecordHardwareHistory = true; monitorSettings.ShowCpu = false; monitorSettings.TemperatureEnabled = false;
hardware.RecordSystem(now.AddSeconds(6), new(25, 50, 54, ""), monitorSettings);
Check(hardware.Read(HardwareMetric.Cpu, now.AddSeconds(6), HardwareHistory.Retention).Last().Value is null && hardware.Read(HardwareMetric.CpuTemperature, now.AddSeconds(6), HardwareHistory.Retention).Last().Value is null, "Disabled metrics and temperature acquisition do not record hidden readings");
var bounded = new HardwareHistory();
for (int i = 0; i <= 7200; i++) bounded.RecordSystem(now.AddSeconds(i * 2), new(i % 100, 48, null, ""), new AppSettings());
var lastTime = now.AddHours(4);
var retained = bounded.Read(HardwareMetric.Cpu, lastTime, HardwareHistory.Retention);
Check(retained.Count == 1801 && retained[0].Time == lastTime.AddHours(-1), "Four hours of monitoring retains only one hour of CPU samples");
Check(bounded.Read(HardwareMetric.Cpu, lastTime, TimeSpan.FromMinutes(5)).Count == 151, "Five-minute view excludes samples outside its range");
Check(bounded.Read(HardwareMetric.Cpu, lastTime.AddHours(2), HardwareHistory.Retention).Count == 0, "Expired samples do not reappear after a long sleep");
bounded.RecordSystem(lastTime.AddMinutes(-30), new(10, 20, null, ""), new AppSettings());
Check(bounded.Read(HardwareMetric.Cpu, lastTime.AddMinutes(-30), HardwareHistory.Retention).Count == 1, "Clock rollback starts an ordered curve without future points");
var fastHistory = new HardwareHistory();
for (int i = 0; i < 4000; i++) fastHistory.RecordSystem(now.AddMilliseconds(i), new(double.NaN, double.PositiveInfinity, double.NaN, ""), new AppSettings { TemperatureEnabled = true });
Check(fastHistory.Read(HardwareMetric.Cpu, now.AddSeconds(4), HardwareHistory.Retention).Count == HardwareHistory.MaximumSamplesPerMetric, "Burst sampling cannot exceed the independent memory cap");
Check(fastHistory.Read(HardwareMetric.CpuTemperature, now.AddSeconds(4), HardwareHistory.Retention).All(s => s.Value is null), "Invalid floating-point values cannot reach chart geometry");
var resumed = new HardwareHistory();
var resumeSettings = new AppSettings { ShowGpuMemory = true };
resumed.RecordSystem(now, new(20, 40, null, ""), resumeSettings);
resumed.RecordGpu(now, new(100, 50, null, "test"), resumeSettings);
resumeSettings.RecordHardwareHistory = false; resumed.ApplySettings(resumeSettings);
resumeSettings.RecordHardwareHistory = true;
resumed.RecordSystem(now.AddSeconds(4), new(60, 50, null, ""), resumeSettings);
resumed.RecordGpu(now.AddSeconds(5), new(200, 80, null, "test"), resumeSettings);
Check(resumed.Read(HardwareMetric.Cpu, now.AddSeconds(5), HardwareHistory.Retention).Last().StartsSegment, "CPU resume breaks a short pause even when no sampling tick occurred while paused");
Check(resumed.Read(HardwareMetric.GpuDedicated, now.AddSeconds(5), HardwareHistory.Retention).Last().StartsSegment, "GPU resume also breaks a short recording pause");
resumeSettings.ShowGpuMemory = false; resumed.ApplySettings(resumeSettings);
resumeSettings.ShowGpuMemory = true; resumed.RecordGpu(now.AddSeconds(8), new(250, 90, null, "test"), resumeSettings);
Check(resumed.Read(HardwareMetric.GpuDedicated, now.AddSeconds(8), HardwareHistory.Retention).Last().StartsSegment, "Disabling and reenabling the GPU module leaves a discontinuity");
resumed.RecordGpu(now.AddSeconds(10), new(250, 90, null, "test"), resumeSettings);
Check(!resumed.Read(HardwareMetric.GpuDedicated, now.AddSeconds(10), HardwareHistory.Retention).Last().StartsSegment, "Uninterrupted equal GPU readings connect normally");
resumeSettings.RecordHardwareHistory = false;
resumed.RecordSystem(now.AddHours(2), new(0, 0, null, ""), resumeSettings);
Check(resumed.Read(HardwareMetric.Cpu, now.AddSeconds(10), HardwareHistory.Retention).Count == 0 &&
      resumed.Read(HardwareMetric.GpuDedicated, now.AddSeconds(10), HardwareHistory.Retention).Count == 0,
      "Paused monitoring expires both CPU and GPU storage rather than only filtering the visible range");
Check(KnownWiredKeyboardProvider.RecognizedModel("HHKB Professional", "PFU Limited")=="HHKB Professional","HHKB product descriptor is a readable model");
foreach(var generic in new[]{"USB Keyboard","HID Keyboard Device","Gaming Keyboard","Wireless mouse 8k NANO dongle-L","HID VHF Driver","VID_0853&PID_0100"})
    Check(KnownWiredKeyboardProvider.RecognizedModel(generic,"Generic") is null,"Generic and non-keyboard descriptions are excluded: "+generic);
var wiredEndpoint=new Mizu.Core.Native.HidEndpoint("path",0x0853,0x0100,1,6,8,0,0,"HHKB Professional","wired-container","HID\\WIRED","PFU Limited");
var wiredNode=new Mizu.Core.Native.PnpNode("HID\\WIRED","HID Keyboard Device","Keyboard","wired-container","USB\\VID_0853&PID_0100",null,true);
var wiredFound=await new KnownWiredKeyboardProvider([wiredEndpoint],[wiredNode]).ScanAsync(CancellationToken.None);
Check(wiredFound.Single().IdentifiedWiredKeyboard && wiredFound[0].Link==LinkKind.Wired && wiredFound[0].Percent is null,"Known USB keyboard is identified without inventing battery readings");
Check((await new KnownWiredKeyboardProvider([wiredEndpoint],[wiredNode with {ParentId="ROOT\\VHF"}]).ScanAsync(CancellationToken.None)).Count==0,"Virtual parents cannot qualify as USB keyboards");
Check((await new KnownWiredKeyboardProvider([wiredEndpoint with {Product="USB Keyboard"}],[wiredNode]).ScanAsync(CancellationToken.None)).Count==0,"Unknown USB keyboard is not added");
var wiredMerged=BatteryService.Merge([wiredFound[0],wiredFound[0] with {IdentifiedWiredKeyboard=false,Name="HID keyboard",Percent=83,Source="Standard HID BatteryStrength"}]);
Check(wiredMerged.Count==1 && wiredMerged[0].IdentifiedWiredKeyboard && wiredMerged[0].Percent==83 && wiredMerged[0].Name=="HHKB Professional","Standard battery readings merge with known wired identity");
var oldBattery = wiredFound[0] with { IdentifiedWiredKeyboard = false, Percent = 15, ConnectionObservedAt = null, ObservedAt = now.AddHours(-2) };
var freshWired = wiredFound[0] with { ConnectionObservedAt = now };
var mergedWired = BatteryService.Merge([freshWired, oldBattery]).Single();
Check(mergedWired.LastSignalAt == now && new PeripheralPresence().Update([mergedWired], now).Single().DisplayDimmed == false,
    "A current USB connection stays visible when merged with a two-hour-old battery reading");
Check(mergedWired.ObservedAt == oldBattery.ObservedAt && !mergedWired.IsFresh(now), "USB connection does not refresh a stale battery reading");
var radioOnly = new WindowsBatteryProvider([], [], [new BluetoothLink("key", "Bluetooth Keyboard", null, null, true, PeripheralKind.Keyboard)]);
var connectedKeyboard = (await radioOnly.ScanAsync(default)).Single();
Check(connectedKeyboard.ConnectionObservedAt is not null && connectedKeyboard.ObservedAt is null, "Bluetooth connection without battery carries connection evidence only");
var connectionTracker = new PeripheralPresence();
for (int minute = 0; minute <= 61; minute++)
{
    var time = now.AddMinutes(minute);
    var current = connectionTracker.Update([connectedKeyboard with { ConnectionObservedAt = time }], time);
    Check(current.Single().DisplayDimmed == false, "Repeated confirmed Bluetooth connections stay visible and bright");
}
var lastConnection = now.AddMinutes(61);
Check(connectionTracker.Update([], lastConnection.AddMinutes(15)).Single().DisplayDimmed == true, "Bluetooth gray timer starts at last confirmed connection");
Check(connectionTracker.Update([], lastConnection.AddHours(1)).Count == 0, "Bluetooth disappears an hour after confirmations stop");
var graceDevice=new Peripheral("sleep-mouse","Sleep Mouse A9",PeripheralKind.Mouse,LinkKind.Receiver,LinkState.Online,80,false,"test",now);
var presence=new PeripheralPresence();Check(presence.Update([graceDevice],now).Single().DisplayDimmed==false,"New device is visible at normal brightness");
Check(presence.Update([graceDevice with {State=LinkState.Offline}],now.AddMinutes(14)).Single().DisplayDimmed==false,"Sleeping device retains normal display before fifteen minutes");
Check(presence.Update([],now.AddMinutes(15)).Single().DisplayDimmed==true,"Absent device becomes gray at fifteen minutes");
Check(presence.Update([],now.AddMinutes(59)).Count==1 && presence.Update([],now.AddHours(1)).Count==0,"Device remains until one hour then disappears");
Check(presence.Update([graceDevice],now.AddMinutes(61)).Count==0,"Repeated stale online data cannot renew the grace period");
Check(presence.Update([graceDevice with {ObservedAt=now.AddMinutes(62)}],now.AddMinutes(62)).Single().DisplayDimmed==false,"New signal restores a hidden device");
var signalOnly=new PeripheralPresence();signalOnly.Update([graceDevice with {ObservedAt=null}],now);Check(signalOnly.Update([graceDevice with {ObservedAt=null}],now.AddHours(1)).Count==0,"Timestamp-free repeated records do not renew indefinitely");
signalOnly.Update([graceDevice with {State=LinkState.Offline,ObservedAt=null}],now.AddMinutes(61));Check(signalOnly.Update([graceDevice with {ObservedAt=null}],now.AddMinutes(62)).Count==1,"A new connected transition restores timestamp-free devices");
var deviceLayoutSettings=new AppSettings();Check(WidgetLayout.Calculate(deviceLayoutSettings,1).Sections.Single(s=>s.Kind==WidgetSection.Devices).Height==78,"One device uses exactly one row");
var twelveWords=new AppSettings {ShowVocabulary=true,VocabularyVisibleRows=12,Vocabulary=Enumerable.Range(0,12).Select(i=>new VocabularyEntry {Word="word"+i}).ToList()};twelveWords.Validate();var twelveRotation=new VocabularyRotation();twelveRotation.Update(twelveWords,now);
Check(twelveRotation.VisibleRowCount==12 && WidgetLayout.Calculate(twelveWords).Sections.Single(s=>s.Kind==WidgetSection.Vocabulary).Height==12*66,"All twelve requested word groups are selected and given space");
Check(!WidgetLayout.Calculate(deviceLayoutSettings,0).Sections.Any(s=>s.Kind==WidgetSection.Devices),"Zero devices reserve no section");
var vocabularyGridSettings = new AppSettings
{
    ShowVocabulary = true, VocabularyColumns = 6, VocabularyVisibleRows = 24, Scale = .8,
    ShowWeather = false, ShowCpu = false, ShowMemory = false, ShowTemperature = false, ShowDevices = true,
    Vocabulary = Enumerable.Range(1, 180).Select(i => new VocabularyEntry { Word = i.ToString() }).ToList()
};
var roomyVocabulary = WidgetLayout.Calculate(vocabularyGridSettings, 0, 1040, 1920);
var busierVocabulary = WidgetLayout.Calculate(vocabularyGridSettings, 2, 1040, 1920);
Check(roomyVocabulary.VocabularyRows > busierVocabulary.VocabularyRows && roomyVocabulary.VocabularyRows > 12 &&
    (roomyVocabulary.Height + WidgetLayout.ExpandedQuickEntryHeight) * .8 <= 1040 - WidgetLayout.DesktopClearance,
    "The word grid uses space freed by other modules and leaves room for the quick editor");
var narrowVocabulary = WidgetLayout.Calculate(vocabularyGridSettings, 0, 1040, 720);
Check(roomyVocabulary.VocabularyColumns == 6 && narrowVocabulary.VocabularyColumns < 6 && narrowVocabulary.VocabularyColumns > 1 &&
    narrowVocabulary.Width * .8 <= 720 - WidgetLayout.DesktopClearance && vocabularyGridSettings.VocabularyColumns == 6 &&
    vocabularyGridSettings.VocabularyVisibleRows == 24, "A narrower work area limits columns without changing the saved preference");
Check(WidgetLayout.Calculate(vocabularyGridSettings, 0, 4000, 1920).VocabularyRows == 24, "A tall work area caps the grid at twenty-four rows");
vocabularyGridSettings.Vocabulary.Clear();
Check(WidgetLayout.Calculate(vocabularyGridSettings).Width == WidgetLayout.BaseWidth, "An empty word grid does not widen the panel");
var themeSettings=new AppSettings {RandomThemeEnabled=true,ThemeIntervalMinutes=1,Theme="light"};var themeRotation=new ThemeRotation();
Check(!themeRotation.Update(themeSettings,now,new[]{"light","dark"}) && !themeRotation.Update(themeSettings,now.AddSeconds(59),new[]{"light","dark"}),"Theme stays stable before its minute deadline");
Check(themeRotation.Update(themeSettings,now.AddMinutes(1),new[]{"light","dark"}) && themeSettings.Theme=="dark","Random theme changes at deadline and excludes current color");
themeSettings.RandomThemeEnabled=false;Check(!themeRotation.Update(themeSettings,now.AddHours(12),new[]{"light","dark"}),"Disabled random theme does not switch");
var legacyMinutes=new AppSettings {VocabularyIntervalSeconds=61,ThemeIntervalMinutes=9999};legacyMinutes.Validate();
Check(legacyMinutes.VocabularyMinutes==2 && legacyMinutes.ThemeIntervalMinutes==720,"Legacy seconds round upward to minutes and theme supports twelve hours");
legacyMinutes.VocabularyIntervalMinutes=720;legacyMinutes.Validate();Check(legacyMinutes.VocabularyIntervalSeconds==43200,"Vocabulary supports a twelve-hour deadline");
var activeReceiver=new Peripheral("receiver","ATK A9 Ultra Max 2.0 Tri-Mode",PeripheralKind.Mouse,LinkKind.Receiver,LinkState.Online,90,false,"ATK",now);
var activeBluetooth=activeReceiver with {Id="bluetooth",Name="ATK A9 ULTRA MAX 2.0",Link=LinkKind.Bluetooth,ContainerId="bt-container"};
Check(ActivePeripheralFilter.Select(new[]{activeReceiver,activeBluetooth}).Single().Link==LinkKind.Bluetooth,"Active ATK channel variants collapse to Bluetooth");
Check(ActivePeripheralFilter.Select(new[]{activeReceiver,activeBluetooth with {State=LinkState.Offline}}).Single().Link==LinkKind.Receiver,"Receiver is shown after Bluetooth disconnects");
Check(ActivePeripheralFilter.Select(new[]{activeReceiver,activeReceiver with {Id="other",Name="Logitech MX Master 3"}}).Count==2,"Separate active mice remain separate devices");
var parsedWords=VocabularyImport.Parse("word,meaning\nserene,\"宁静,平和\"\n\"say \"\"hello\"\"\",问好", ".csv");
Check(parsedWords.Count==2 && parsedWords[0].Meaning=="宁静,平和" && parsedWords[1].Word=="say \"hello\"", "CSV handles headers, quoted commas and escaped quotes");
Check(VocabularyImport.Parse("serene\t宁静\ncalm\t平静", ".tsv").Count==2 && VocabularyImport.Parse("serene\ncalm", ".txt").Count==2,"TSV and word-only text import");
Check(VocabularyImport.Parse("[{\"word\":\"serene\",\"meaning\":\"宁静\"}]", ".json")[0].Meaning=="宁静", "JSON imports case-insensitive word and meaning fields");
VocabularyEntry[] exportWords =
[
    new() { Word = "word", Meaning = "meaning" },
    new() { Word = "say \"hello\"", Meaning = "问好,打招呼\r\n第二行" },
    new() { Word = "café", Meaning = "" }
];
foreach (string extension in new[] { ".csv", ".json" })
{
    var roundtrip = VocabularyImport.Parse(VocabularyExport.Serialize(exportWords, extension), extension);
    Check(roundtrip.Select(w => (w.Word, w.Meaning)).SequenceEqual(exportWords.Select(w => (w.Word, w.Meaning))),
        $"Vocabulary export reimports all entries, preserving order, punctuation, Unicode and multiline definitions in {extension}");
}
bool badImport=false;try {VocabularyImport.Parse("[{\"Meaning\":\"missing word\"}]", ".json");}catch(FormatException){badImport=true;}
Check(badImport,"Malformed JSON book rejects missing words before any import");
var rotationSettings=new AppSettings {ShowVocabulary=true,VocabularyVisibleRows=2,VocabularyIntervalMinutes=1,Vocabulary=[new(){Word="one",Meaning="一"},new(){Word="two"},new(){Word="three"},new(){Word="four"}]};
var rotation=new VocabularyRotation();rotation.Update(rotationSettings,now);
string Window(VocabularyRotation model) => string.Join(",", model.Order.Skip((int)model.Offset).Take(model.VisibleRowCount).Select(entry => entry.Word));
Check(Window(rotation)=="one,two","Sequential rotation starts with configured group size");
rotation.Update(rotationSettings,now.AddSeconds(59));Check(Window(rotation)=="one,two","Words stay stable until deadline");
rotation.Update(rotationSettings,now.AddSeconds(60));Check(Window(rotation)=="three,four","Words rotate at configured deadline");
rotation.Update(rotationSettings,now.AddSeconds(120));Check(Window(rotation)=="one,two","Sequential rotation wraps after the final group");
rotationSettings.ShowVocabulary=false;rotation.Update(rotationSettings,now.AddSeconds(180));Check(Window(rotation)=="one,two","Hidden vocabulary pauses automatic rotation");
var pagingSettings = new AppSettings { ShowVocabulary = true, VocabularyVisibleRows = 3, VocabularyIntervalMinutes = 1,
    Vocabulary = Enumerable.Range(1, 8).Select(index => new VocabularyEntry { Word = index.ToString() }).ToList() };
var paging = new VocabularyRotation();
paging.Update(pagingSettings, now);
paging.Scroll(pagingSettings, 120, 0, now);
Check(paging.Offset == 0 && Window(paging) == "1,2,3", "Scrolling up at the top keeps the first word in place");
paging.Scroll(pagingSettings, -30, 0, now);
Check(paging.Offset == .25, "High-resolution wheel deltas scroll a fraction of a row");
paging.Scroll(pagingSettings, -90, .125, now);
Check(paging.Offset == 1 && Window(paging) == "2,3,4", "Same-direction inputs accumulate without losing travel");
paging.Scroll(pagingSettings, 30, .5, now);
Check(paging.Offset == .25, "Reversing starts from the displayed offset and discards queued forward travel");
paging.Scroll(pagingSettings, -2400, .25, now);
paging.Scroll(pagingSettings, -120, 5, now);
Check(paging.Offset == 5 && Window(paging) == "6,7,8", "Scrolling down past the end stops flush with the last word");
paging.Scroll(pagingSettings, 120, 5, now);
Check(paging.Offset == 4 && Window(paging) == "5,6,7", "Scrolling back moves one word toward the beginning");
paging.Scroll(pagingSettings, 2400, 4, now);
paging.Scroll(pagingSettings, 120, 0, now.AddMinutes(1));
Check(paging.Offset == 0 && Window(paging) == "1,2,3", "Top boundary takes precedence over a coincident automatic deadline");
paging.Update(pagingSettings, now.AddSeconds(119));
Check(Window(paging) == "1,2,3", "Manual interaction resets the automatic deadline even at the first page");
paging.Update(pagingSettings, now.AddMinutes(2));
Check(Window(paging) == "4,5,6", "Automatic rotation resumes after the new deadline");
pagingSettings.VocabularyRandom = true;
paging.Update(pagingSettings, now.AddMinutes(3));
string randomFirst = Window(paging);
paging.Scroll(pagingSettings, -120, 0, now.AddMinutes(3));
Check(Window(paging) != randomFirst && paging.Order.Distinct().Count() == 8, "Manual scroll preserves a complete shuffled order");
paging.Scroll(pagingSettings, 120, 1, now.AddMinutes(3));
Check(Window(paging) == randomFirst, "Random mode can return to the exact same words");
pagingSettings.VocabularyVisibleRows = 12;
paging.Update(pagingSettings, now.AddMinutes(4));
paging.Scroll(pagingSettings, -120, 0, now.AddMinutes(4));
Check(paging.VisibleRowCount == 8 && paging.Offset == 0, "A list that fits in the viewport stays put");
var multiColumnPaging = new VocabularyRotation();
Check(EnglishLexicon.Contains(" serene ") && EnglishLexicon.Contains("METICULOUS") &&
    EnglishLexicon.Contains("don’t"), "The bundled English dictionary recognizes known spellings and apostrophe variants");
Check(!EnglishLexicon.Contains("sernee") && !EnglishLexicon.Contains("你好") &&
    !EnglishLexicon.Contains("route reflector") && !EnglishLexicon.Contains("bgp"), "Unknown words and unsupported phrases do not offer pronunciation");
VocabularyEntry[] lookupEntries =
[
    new() { Word = "serenity", Meaning = "宁静" },
    new() { Word = "serene", Meaning = "平静的" },
    new() { Word = "don’t", Meaning = "不要" },
    new() { Word = "route reflector", Meaning = "路由反射器" }
];
var savedLookup = LocalWordSearch.Find(lookupEntries, " SERENE ");
Check(savedLookup[0] == new LocalWordMatch("serene", "平静的", true) &&
    savedLookup.Count(match => match.Word == "serene") == 1, "Exact saved words rank first and do not repeat in built-in results");
Check(LocalWordSearch.Find(lookupEntries, "路由").Single().Word == "route reflector" &&
    LocalWordSearch.Find(lookupEntries, "don't")[0].Saved, "Local lookup supports Chinese meanings and apostrophe variants");
var bundledLookup = LocalWordSearch.Find([], "meticul");
Check(bundledLookup.Any(match => match.Word == "meticulous" && !match.Saved && match.Meaning is null) &&
    bundledLookup.Count <= 8, "Built-in prefix results are bounded and do not include definitions");
Check(LocalWordSearch.Find(lookupEntries, "   ").Count == 0 &&
    LocalWordSearch.Find(lookupEntries, "zzz__no_match").Count == 0, "Blank and unmatched queries return no results");
var editing = new AppSettings { Vocabulary = lookupEntries.ToList() };
var edited = editing.Vocabulary[1];
Check(!VocabularyImport.TryEdit(editing, edited, " SERENITY ", "覆盖", out var editMessage) &&
    editMessage == "单词已存在" && edited.Word == "serene" && edited.Meaning == "平静的",
    "A duplicate rename leaves both original fields intact");
Check(VocabularyImport.TryEdit(editing, edited, " Serene ", "  宁静的  ", out _) &&
    ReferenceEquals(editing.Vocabulary[1], edited) && edited.Word == "Serene" && edited.Meaning == "宁静的",
    "Editing trims input and keeps the existing word in its position");
editing.Vocabulary.Remove(edited);
Check(!VocabularyImport.TryEdit(editing, edited, "calm", "平静", out _) &&
    !editing.Vocabulary.Contains(edited), "A stale edit cannot restore a deleted word");
pagingSettings.VocabularyRandom = false;
pagingSettings.VocabularyVisibleRows = 3;
multiColumnPaging.Update(pagingSettings, now, 2, 3);
Check(multiColumnPaging.VisibleRowCount == 2 && multiColumnPaging.TotalRowCount == 3, "A last partial row occupies one row of the multi-column viewport");
multiColumnPaging.Scroll(pagingSettings, -960, 0, now, 2, 3);
Check(multiColumnPaging.Offset == 1 && multiColumnPaging.Order.Skip((int)multiColumnPaging.Offset * 3).First().Word == "4",
    "Multi-column scrolling stops with the last partial row visible and keeps row-major order");
multiColumnPaging.Update(pagingSettings, now, 2, 2);
Check(multiColumnPaging.Offset == 1 && multiColumnPaging.Order[2].Word == "3", "Changing columns keeps the previous leading word in view");
multiColumnPaging.Scroll(pagingSettings, 960, 1, now, 2, 2);
Check(multiColumnPaging.Offset == 0, "Multi-column wheel scrolling stops at the first row");
foreach (bool random in new[] { false, true })
{
    var adding = new AppSettings
    {
        ShowVocabulary = true, VocabularyRandom = random, VocabularyVisibleRows = 2, VocabularyIntervalMinutes = 1,
        Vocabulary = Enumerable.Range(1, 8).Select(i => new VocabularyEntry { Word = "word" + i }).ToList()
    };
    var viewport = new VocabularyRotation();
    viewport.Update(adding, now, 2, 2);
    viewport.Scroll(adding, -120, 0, now, 2, 2);
    var previousOrder = viewport.Order.ToArray();
    Check(VocabularyImport.TryAdd(adding, "new word", "note", out _), "A distinct word is accepted");
    viewport.RevealLatest(adding, now.AddSeconds(15), 2, 2);
    Check(viewport.Offset == 3 && viewport.Order.Take(previousOrder.Length).SequenceEqual(previousOrder) &&
        viewport.Order[^1].Word == "new word" && adding.Vocabulary[^1].Word == "new word",
        "Adding a word preserves the current cycle and reveals the final partial row");
    viewport.Update(adding, now.AddSeconds(70), 2, 2);
    Check(viewport.Offset == 3 && viewport.Order[^1].Word == "new word", "Adding gives the last page a fresh rotation interval");
    Check(!VocabularyImport.TryAdd(adding, " NEW WORD ", "overwritten", out var message) && message == "单词已存在" &&
        adding.Vocabulary[^1].Meaning == "note" && adding.Vocabulary.Count == 9, "Duplicate words ignore surrounding spaces and case");
}
rotationSettings.ShowVocabulary=true;
rotationSettings.VocabularyHideMeaning=true;var wordArea=WidgetLayout.Calculate(rotationSettings).Sections.Single(s=>s.Kind==WidgetSection.Vocabulary);
Check(wordArea.Height==84,"Hidden meanings release unused row height");
rotationSettings.ShowClock=true;Check(WidgetLayout.Calculate(rotationSettings).Sections[0].Kind==WidgetSection.Clock,"Clock appears before weather");
rotationSettings.VocabularyVisibleRows=99;rotationSettings.VocabularyColumns=99;rotationSettings.VocabularyIntervalSeconds=0;rotationSettings.Validate();
Check(rotationSettings.VocabularyVisibleRows==WidgetLayout.MaximumVocabularyRows && rotationSettings.VocabularyColumns==WidgetLayout.MaximumVocabularyColumns &&
    rotationSettings.VocabularyIntervalSeconds==60,"Invalid rotation settings are bounded");
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
var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (sourceRoot is not null && !File.Exists(Path.Combine(sourceRoot.FullName, "Mizu.slnx"))) sourceRoot = sourceRoot.Parent;
var installer = Path.Combine(sourceRoot?.FullName ?? Environment.CurrentDirectory, "vendor/PawnIO/PawnIO_setup.exe");
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
    var cacheFile = Path.Combine(temp, "weather-cache.json");
    var cache = new WeatherCache(cacheFile);
    var cacheCity = new Place("Example", "", null, 30, 120);
    Check(cache.Load(cacheCity, 12, now) is null, "Missing cache is empty");
    cache.Save(cacheCity, weather);
    Check(cache.Load(cacheCity, 12, now.AddHours(1))?.Celsius == weather.Celsius, "Weather persists across restarts");
    Check(cache.Load(cacheCity with { Longitude = 121 }, 12, now) is null, "Never show a different city's cache");
    Check(cache.Load(cacheCity, 1, now.AddHours(2)) is null, "Expired cache is rejected");
    Check(cache.Load(cacheCity, 12, now.AddMinutes(-1)) is null, "Future dated cache is rejected");
    File.WriteAllText(cacheFile, "{broken");
    Check(cache.Load(cacheCity, 12, now) is null, "Corrupt cache cannot crash startup");
    cache.Save(cacheCity, weather); cache.Clear();
    Check(!File.Exists(cacheFile), "Cache clear removes only the weather file");
    using var poetryJson = JsonDocument.Parse("""{"content":"海内存知己，天涯若比邻。","author":"王勃","origin":"送杜少府之任蜀州"}""");
    var poem = PoetryService.Parse(poetryJson.RootElement, now);
    Check(poem.Lines(1).SequenceEqual(new[] { "海内存知己，" }), "One verse retains its original punctuation");
    Check(poem.Lines(2).SequenceEqual(new[] { "海内存知己，", "天涯若比邻。" }), "Two verses belong to the same quote");
    var poemPath = Path.Combine(temp, "poetry-cache.json");
    using var poems = new PoetryService(cachePath: poemPath); poems.Save(poem);
    Check(poems.Load()?.Origin == poem.Origin, "Poetry persists with its attribution");
    File.WriteAllText(poemPath, "broken"); Check(poems.Load() is null, "Invalid poetry cache cannot break startup");
    var poemSettings = new AppSettings { ShowPoetry = true, PoetryLines = 2 }; poemSettings.Validate();
    Check(poemSettings.PoetryLines == 1 && WidgetLayout.Calculate(poemSettings).Sections.Single(s => s.Kind == WidgetSection.Poetry).Height == 64, "Legacy verse settings migrate to one physical line");
    using var richPoemJson = JsonDocument.Parse("""{"status":"success","data":{"content":"海内存知己，天涯若比邻。","origin":{"title":"送杜少府之任蜀州","dynasty":"唐代","author":"王勃"}}}""");
    var richPoem = PoetryService.Parse(richPoemJson.RootElement, now);
    Check(richPoem.Dynasty == "唐代" && richPoem.Author == "王勃" && richPoem.Origin == "送杜少府之任蜀州", "V2 metadata is read from the matching source record");
    using var badPoem = JsonDocument.Parse("""{"data":{"content":"诗句","origin":{"title":"出处未全"}}}""");
    bool provenanceRejected = false; try { PoetryService.Parse(badPoem.RootElement, now); } catch(JsonException) { provenanceRejected = true; }
    Check(provenanceRejected, "Incomplete online metadata is rejected so the independent fallback can be used");
    poems.Save(richPoem); Check(poems.Load()?.Dynasty == "唐代", "Dynasty survives cache persistence");
    var onlineBeforeFallback = File.ReadAllText(poemPath);
    poems.Save(OfflinePoetry.Pick(now.AddSeconds(1)));
    Check(File.ReadAllText(poemPath) == onlineBeforeFallback, "Offline selection never overwrites the successful online cache");
    Check(poems.Load()?.Source == "内置公版诗库", "Latest offline selection survives restart independently");
    var todoLayout = new AppSettings { ShowTodo = true };
    var emptyTodoHeight = WidgetLayout.Calculate(todoLayout).Height;
    todoLayout.ShowTodo = false;
    Check(WidgetLayout.Calculate(todoLayout).Height == emptyTodoHeight, "Empty todo adds absolutely no reserved height");
    todoLayout.ShowTodo = true; todoLayout.Todos.Add(new() { Text = "Task" });
    Check(WidgetLayout.Calculate(todoLayout).Sections.Any(s => s.Kind == WidgetSection.Todo), "Todo section appears after first task");
    todoLayout.Todos[0].Completed = true;
    Check(!WidgetLayout.Calculate(todoLayout).Sections.Any(s => s.Kind == WidgetSection.Todo), "Completing the last visible task removes its section");
    poemSettings.ShowPoetry = false;
    Check(!WidgetLayout.Calculate(poemSettings).Sections.Any(s => s.Kind == WidgetSection.Poetry), "Poetry can be removed from layout");
    var store = new SettingsStore(Path.Combine(temp, "settings.json"));
    var customOrder=new AppSettings {Scale=.4,ShowClock=true,HideInactiveDevices=true,VocabularySpacing=0,VocabularyMeaningFontSize=21,SectionOrder=[WidgetSection.Vocabulary,WidgetSection.Clock,WidgetSection.Vocabulary,(WidgetSection)999]};
    store.Save(customOrder);var ordered=store.Load();
    Check(ordered.SectionOrder.Count==Enum.GetValues<WidgetSection>().Length && ordered.SectionOrder[0]==WidgetSection.Vocabulary && ordered.SectionOrder.Distinct().Count()==ordered.SectionOrder.Count,"Custom order reloads, drops invalid duplicates and appends missing modules");
    Check(ordered.Scale==.4 && ordered.HideInactiveDevices && ordered.VocabularySpacing==0,"Small scale, activity filter and tight spacing survive disk reload");
    Check(ordered.VocabularyMeaningFontSize==21,"Vocabulary meaning font size survives disk reload");
    ordered.Vocabulary=[new(){Word="first"},new(){Word="second"}];ordered.ShowVocabulary=true;ordered.ShowClock=true;
    Check(WidgetLayout.Calculate(ordered).Sections[0].Kind==WidgetSection.Vocabulary,"Custom module order affects actual widget geometry");
    ordered.VocabularyVisibleRows=2;ordered.VocabularyHideMeaning=true;
    Check(WidgetLayout.Calculate(ordered).Sections.First(s=>s.Kind==WidgetSection.Vocabulary).Height==68,"Zero spacing gives compact word rows");
    var vocabSettings = new AppSettings { ShowVocabulary=true, Vocabulary=[new(){Word=" serene ",Meaning=" 宁静的 "},new(){Word=" "}] };
    store.Save(vocabSettings);var words=store.Load();
    Check(words.ShowVocabulary && words.Vocabulary.Count==1 && words.Vocabulary[0].Word=="serene" && words.Vocabulary[0].Meaning=="宁静的","Vocabulary survives disk reload and drops blank entries");
    words.ShowVocabulary=false;store.Save(words);
    Check(store.Load().Vocabulary.Count==1 && !WidgetLayout.Calculate(words).Sections.Any(s=>s.Kind==WidgetSection.Vocabulary),"Hiding vocabulary preserves saved entries without reserving space");
    var wordPager=new DevicePager();
    Check(wordPager.Advance(0,-120,3,0,pageSize:1)==1 && wordPager.Advance(1,-120,3,200,pageSize:1)==2 && wordPager.Advance(2,-120,3,400,pageSize:1)==2,"Single-word paging reaches every entry and stops at the end");
    settings.Scale = 99; settings.ProtectedPushToken = SettingsStore.Protect("test-private-token"); store.Save(settings);
    var restored = store.Load();
    Check(restored.Scale == 1.2 && SettingsStore.Unprotect(restored.ProtectedPushToken) == "test-private-token", "Bounded settings and DPAPI roundtrip");
    settings.TextScale = 1.2; settings.TextWeight = 600; settings.ShowWeather = false; settings.ShowTodo = true; settings.CornerRadius = 47; settings.ForecastHours = 9; settings.ForecastDays = 6; settings.PanelLocked = true;
    settings.Todos = [new() { Text = "待办一" }, new() { Text = "待办二", Completed = true }];
    store.Save(settings); var custom = store.Load();
    Check(custom.PanelLocked, "Panel lock survives a settings reload");
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
var losses = new LossWindow();
Check(losses.Percent is null, "No probe is not zero loss");
losses.Add(true); losses.Add(false);
Check(losses.Percent == 50, "Loss counts unsuccessful probes");
for (int i = 0; i < 60; i++) losses.Add(true);
Check(losses.Count == 60 && losses.Percent == 0, "Rolling loss expires old failures");
losses.Clear(); Check(losses.Count == 0 && losses.Percent is null, "Target changes reset loss history");
Check(NetworkService.Rate(100, 300, 2) == 100, "Network bytes normalized by elapsed time");
Check(NetworkService.Rate(300, 100, 2) is null && NetworkService.Rate(0, 1, 0) is null, "Reset counters do not create negative rates");
for (int mask = 0; mask < 4; mask++) {
    var modules = new AppSettings { ShowNetwork = (mask & 1) != 0, ShowMedia = (mask & 2) != 0 };
    var sections = WidgetLayout.Calculate(modules).Sections;
    Check(sections.Any(s => s.Kind == WidgetSection.Network) == modules.ShowNetwork && sections.Any(s => s.Kind == WidgetSection.Media) == modules.ShowMedia, "Independent network and media visibility");
    Check(sections.Zip(sections.Skip(1)).All(pair => pair.First.Top + pair.First.Height < pair.Second.Top), "New modules do not overlap");
}
var probeSettings = new AppSettings { NetworkProbeTarget = "bad://target" }; probeSettings.Validate();
Check(probeSettings.NetworkProbeTarget == "1.1.1.1", "Invalid probe restored safely");
var migratedProbes = new AppSettings { NetworkProbeTarget = "8.8.4.4" }; migratedProbes.Validate();
Check(migratedProbes.NetworkProbeTargets!.SequenceEqual(new[] { "8.8.4.4" }), "Legacy single IP migrates without changing its address");
var multiProbes = new AppSettings { NetworkProbeTargets = ["1.1.1.1", "1.1.1.1", "2001:db8::1", "2001:0db8:0:0:0:0:0:1", "bad", "0.0.0.0"] }; multiProbes.Validate();
Check(multiProbes.NetworkProbeTargets!.SequenceEqual(new[] { "1.1.1.1", "2001:db8::1" }), "Probe list normalizes IPv6 and removes duplicate or invalid addresses");
multiProbes.NetworkProbeTargets = []; multiProbes.Validate(); Check(multiProbes.NetworkProbeTargets.Count == 0, "All probes can be deleted while retaining network rates");
Check(NetworkService.NormalizeTarget("224.0.0.1") is null && NetworkService.NormalizeTarget("ff02::1") is null && NetworkService.NormalizeTarget("127.0.0.1") == "127.0.0.1", "Only literal unicast probe targets are accepted");
using (var sampleStop = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
    var sampleService = new NetworkService();
    var firstProbes = await sampleService.SampleAsync(new[] { "127.0.0.1", "::1" }, sampleStop.Token);
    var nextProbes = await sampleService.SampleAsync(new[] { "127.0.0.1", "127.0.0.2" }, sampleStop.Token);
    Check(firstProbes.Targets.Length == 2 && nextProbes.Targets.Length == 2, "Each configured IP receives its own result");
    Check(!nextProbes.Connected || (nextProbes.Targets[0].Probes == 2 && nextProbes.Targets[1].Probes == 1), "Existing probe history survives edits while a new target starts fresh");
}
var bluetoothContainer = Guid.NewGuid().ToString();
var btRoot = new Mizu.Core.Native.PnpNode("BTHLE\\DEV_AABBCCDDEEFF\\1", "ATK", "Bluetooth", bluetoothContainer, "", 75, true);
var btMouse = new Mizu.Core.Native.PnpNode("HID\\MOUSE", "HID Mouse", "Mouse", bluetoothContainer, btRoot.InstanceId, null, true);
var liveLink = new BluetoothLink("aep-mouse", "ATK A9", Guid.NewGuid().ToString(), "aa:bb:cc:dd:ee:ff", false, PeripheralKind.Mouse);
var btOffline = await new WindowsBatteryProvider(new[] { btRoot, btMouse }, [], new[] { liveLink }).ScanAsync(CancellationToken.None);
Check(btOffline.Count == 1 && btOffline[0].State == LinkState.Offline, "Real Bluetooth disconnect overrides stale PnP connected state");
var btOnline = await new WindowsBatteryProvider(new[] { btRoot, btMouse }, [], new[] { liveLink with { Connected = true } }).ScanAsync(CancellationToken.None);
Check(btOnline.Count == 1 && btOnline[0].State == LinkState.Online && btOnline[0].Link == LinkKind.Bluetooth && btOnline[0].Kind == PeripheralKind.Mouse, "A paired Bluetooth mouse is classified and published on connection");
var btWithoutBattery = await new WindowsBatteryProvider(new[] { btRoot with { Battery = null }, btMouse }, [], new[] { liveLink with { Connected = true } }).ScanAsync(CancellationToken.None);
Check(btWithoutBattery.Count == 1 && btWithoutBattery[0].State == LinkState.Online && btWithoutBattery[0].Percent is null, "Connected Bluetooth devices remain visible without fabricating battery levels");
Check(!JsonSerializer.Serialize(btOnline).Contains("AABBCCDDEEFF", StringComparison.OrdinalIgnoreCase), "Bluetooth addresses stay out of serialized diagnostics");
Check(OfflinePoetry.Entries.Count == 30 && OfflinePoetry.Entries.Select(p => p.Content).Distinct().Count() == 30, "Thirty distinct public-domain quotes are included");
Check(OfflinePoetry.Entries.All(p => p.Dynasty.Length > 0 && p.Author.Length > 0 && p.Origin.Length > 0 && p.Source == "内置公版诗库"), "Offline entries carry complete, distinct provenance");
var offlineOne = OfflinePoetry.Pick(now); var offlineTwo = OfflinePoetry.Pick(now.AddDays(1));
Check(offlineOne.Content != offlineTwo.Content, "Offline daily rotation advances");
Check(OfflinePoetry.Pick(now, offlineOne.Content, true).Content != offlineOne.Content, "Manual offline refresh avoids the previous quote");
Check((offlineOne with { Dynasty = "唐代" }).DynastyMark == "唐", "Dynasty credit uses one character");
var fallbackDay = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;
var fallbackJson = JsonSerializer.Serialize(new {
    current_condition = new[] { new { temp_C = "18", weatherCode = "116", windspeedKmph = "8", humidity = "60" } },
    weather = Enumerable.Range(0, 3).Select(i => new {
        date = fallbackDay.AddDays(i).ToString("yyyy-MM-dd"), maxtempC = "21", mintempC = "12",
        astronomy = new[] { new { sunrise = "06:00 AM", sunset = "06:00 PM" } },
        hourly = Enumerable.Range(0, 8).Select(h => new { time = (h * 300).ToString(), tempC = "18", weatherCode = "116" }).ToArray()
    }).ToArray()
});
var backupHandler = new WeatherSequenceHandler(fallbackJson);
using(var backupService = new WeatherService(backupHandler)) {
    var backup = await backupService.FetchAsync(new Place("绍兴", "中国", null, 30, 120), CancellationToken.None);
    Check(backup.Source.StartsWith("wttr.in") && backup.HourResolution == 3 && backup.Hours.Length > 0 && backup.Days.Length == 2, "Primary failure returns real backup fields without inventing seven days");
    Check(backupHandler.Hosts.SequenceEqual(new[] { "api.open-meteo.com", "wttr.in" }), "Weather failover crosses independent hosts");
}
var disabledHandler = new WeatherSequenceHandler(fallbackJson);
using(var disabledBackup = new WeatherService(disabledHandler)) {
    bool backupRejected = false;
    try { await disabledBackup.FetchAsync(new Place("绍兴", "中国", null, 30, 120), CancellationToken.None, false); } catch(HttpRequestException) { backupRejected = true; }
    Check(backupRejected && disabledHandler.Hosts.Count == 1, "Disabling backup prevents its network request");
}
Check(WttrWeather.ToWmo(113) == 0 && WttrWeather.ToWmo(143) == 45 && WttrWeather.ToWmo(149) == 45 && WttrWeather.ToWmo(389) == 95, "Backup weather codes normalize to existing icons");
Check(WttrWeather.ToWmo(9999) == -1, "Unknown backup conditions stay unknown instead of turning into sunshine");
count += await UpdateChecks.RunAsync();
count += await FirebaseSyncChecks.RunAsync();
Console.WriteLine($"PASS {count} checks. No external notification was sent.");
return 0;
} catch(Exception error) { Console.Error.WriteLine(error); return 1; }

sealed class FakeHandler(string response) : HttpMessageHandler
{
    public string? Uri; public string? Body;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Uri = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(token); return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") }; }
}

sealed class WeatherSequenceHandler(string fallback) : HttpMessageHandler
{
    public List<string> Hosts { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        token.ThrowIfCancellationRequested(); Hosts.Add(request.RequestUri!.Host);
        return Task.FromResult(request.RequestUri.Host == "wttr.in" ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fallback) } : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
