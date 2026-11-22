using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mizu.Core.Services;
using Mizu.Updating;

static class UpdateChecks
{
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Pe(ushort machine)
    {
        var data = new byte[256]; data[0] = (byte)'M'; data[1] = (byte)'Z';
        BitConverter.GetBytes(128).CopyTo(data, 0x3c); BitConverter.GetBytes(0x00004550).CopyTo(data,128); BitConverter.GetBytes(machine).CopyTo(data,132);
        return data;
    }
    static byte[] Zip(string? extra = null, ushort machine = 0x8664)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) {
            foreach (string relative in new[] {"Mizu.exe","app/Mizu.exe","app/Mizu.dll","app/Mizu.runtimeconfig.json","app/Mizu.Updater.exe","app/mizu-package.json"}) {
                using var entry = zip.CreateEntry("Mizu-win-x64/" + relative).Open();
                entry.Write(relative == "app/Mizu.exe" ? Pe(machine) : Encoding.UTF8.GetBytes("fixture"));
            }
            if (extra is not null) { using var entry = zip.CreateEntry(extra).Open(); entry.Write([1,2,3]); }
        }
        return output.ToArray();
    }
    static Dictionary<string,object?> Asset(string tag,string runtime,bool installed,byte[] payload,bool digest=true)
    {
        string name = $"Mizu-{tag[1..]}-{runtime}" + (installed ? "-setup.exe" : ".zip");
        return new() {["name"]=name,["state"]="uploaded",["size"]=payload.Length,
            ["browser_download_url"]=UpdateService.Repository+"/releases/download/"+tag+"/"+name,["digest"]=digest ? "sha256:"+Hash(payload) : null};
    }
    static object Release(string tag,bool preview,params object[] assets) => new {tag_name=tag,prerelease=preview,draft=false,assets};
    public static async Task<int> RunAsync()
    {
        int count=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
        async Task Reject(Func<Task> action,string name) { bool failed=false;try{await action();}catch(IOException){failed=true;}Check(failed,name); }
        Check(ReleaseVersion.Parse("v0.7.10")!.CompareTo(ReleaseVersion.Parse("0.7.9"))>0,"Update versions compare numerically");
        Check(ReleaseVersion.Parse("1.0.0-rc.10")!.CompareTo(ReleaseVersion.Parse("1.0.0-rc.2"))>0,"Preview numeric identifiers compare numerically");
        Check(ReleaseVersion.Parse("1.0.0")!.CompareTo(ReleaseVersion.Parse("1.0.0-rc.99"))>0,"A final version follows its release candidates");
        Check(ReleaseVersion.Parse("../../evil") is null && ReleaseVersion.Parse("v99999999999999.0.0") is null,"Malformed and oversized version components are rejected");
        Check(ReleaseVersion.Parse("1.0.0-01") is null,"Numeric prerelease identifiers reject leading zeroes");
        Check(ReleaseVersion.Parse("1.0.0-100000000000000000000")!.CompareTo(ReleaseVersion.Parse("1.0.0-99999999999999999999"))>0,"Long numeric prerelease identifiers do not overflow");
        Check(UpdateDeployment.RuntimeFor(System.Runtime.InteropServices.Architecture.X86)=="win-x86","Updater follows the app process architecture rather than the OS architecture");
        string folder=Path.Combine(Path.GetTempPath(),"mizu-update-core-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            byte[] payload=Zip(), setup=Pe(0x14c);
            var fixtures=new[] {
                Release("v0.7.12",true,Asset("v0.7.12","win-x64",false,payload),Asset("v0.7.12","win-x64",true,setup)),
                Release("v0.7.9",false,Asset("v0.7.9","win-x64",false,payload)),
                Release("v0.7.11",false,Asset("v0.7.11","win-x64",false,payload)),
                Release("v0.7.99",false,Asset("v0.7.99","win-arm64",false,payload))
            };
            string metadata=JsonSerializer.Serialize(fixtures);
            var handler=new UpdateHandler(metadata,payload);
            using var service=new UpdateService(handler,Path.Combine(folder,"jobs"));
            var found=await service.CheckAsync("0.7.10","win-x64",false,true,default);
            Check(found?.Tag=="v0.7.12" && found.Preview,"Preview channel selects the newest compatible release");
            var stable=await service.CheckAsync("0.7.10","win-x64",false,false,default);
            Check(stable?.Tag=="v0.7.11" && !stable.Preview,"Stable channel excludes preview releases");
            Check(await service.CheckAsync("0.7.12","win-x64",false,true,default) is null,"Already-current apps are not offered a downgrade");
            var installed=await service.CheckAsync("0.7.10","win-x64",true,true,default);
            Check(installed!.Asset.Name.EndsWith("-setup.exe"),"Installed deployments select the installer asset");
            Check(found!.Asset.Name.EndsWith(".zip"),"Portable deployments select the ZIP asset");
            string root=Path.Combine(folder,"portable"); Directory.CreateDirectory(Path.Combine(root,"app"));
            File.WriteAllText(Path.Combine(root,"Mizu.exe"),"old-launcher");
            File.WriteAllText(Path.Combine(root,"app","Mizu.Updater.exe"),"test-helper");
            File.WriteAllText(Path.Combine(root,"app","sentinel.txt"),"old-application");
            var deployment=new UpdateDeployment(root,"win-x64",false);
            var ready=await service.PrepareAsync(found,deployment,null,default);
            var plan=JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(Path.Combine(ready.Directory,"plan.json")))!;
            Check(plan.TargetRoot==root && plan.Mode=="portable" && plan.Runtime=="win-x64" && plan.TargetVersion=="0.7.12","Prepared update records the correct deployment and target version");
            Check(File.ReadAllText(Path.Combine(root,"app","sentinel.txt"))=="old-application","Preparing a download never replaces running application files");
            Check(File.Exists(ready.Helper) && Hash(File.ReadAllBytes(Path.Combine(ready.Directory,"package.zip")))==found.Asset.Sha256,"Only a fully verified package is marked ready");
            var bad=found with {Asset=found.Asset with {Sha256=new string('0',64)}};
            await Reject(()=>service.PrepareAsync(bad,deployment,null,default),"A checksum mismatch aborts before any application changes");
            Check(!Directory.EnumerateFiles(Path.Combine(folder,"jobs"),"*.partial",SearchOption.AllDirectories).Any(),"Failed downloads do not leave partial files");
            using(var brokenService=new UpdateService(new UpdateHandler(metadata,payload){InputFactory=()=>new BrokenUpdateStream(payload)},Path.Combine(folder,"broken-jobs")))
                await Reject(()=>brokenService.PrepareAsync(found,deployment,null,default),"A disconnected download fails without replacing the running app");
            using(var cancel=new CancellationTokenSource())
            using(var canceledService=new UpdateService(new UpdateHandler(metadata,payload){InputFactory=()=>new BrokenUpdateStream(payload,()=>cancel.Cancel())},Path.Combine(folder,"canceled-jobs"))) {
                bool canceled=false;try{await canceledService.PrepareAsync(found,deployment,null,cancel.Token);}catch(OperationCanceledException){canceled=true;}
                Check(canceled && File.ReadAllText(Path.Combine(root,"app","sentinel.txt"))=="old-application","Canceling an in-progress download leaves the old application intact");
            }
            Check(!Directory.EnumerateFiles(folder,"*.partial",SearchOption.AllDirectories).Any(),"Canceled and disconnected transfers remove partial payloads");
            var redirect=new UpdateHandler(metadata,payload) {Redirect=new Uri("https://example.com/evil.zip")};
            using(var unsafeService=new UpdateService(redirect,Path.Combine(folder,"redirect-jobs"))) {
                await Reject(()=>unsafeService.PrepareAsync(found,deployment,null,default),"Asset redirects to unrelated hosts are rejected");
                Check(redirect.Downloads==1,"An unsafe redirect is never followed");
            }
            var fallbackAsset=Asset("v0.7.13","win-x64",false,payload,false);
            string manifestName="Mizu-0.7.13-SHA256SUMS.txt";
            var manifest=new {name=manifestName,browser_download_url=UpdateService.Repository+"/releases/download/v0.7.13/"+manifestName};
            var manifestHandler=new UpdateHandler(JsonSerializer.Serialize(new[]{Release("v0.7.13",true,fallbackAsset,manifest)}),payload) {
                Manifest="\uFEFF"+Hash(payload)+"  "+fallbackAsset["name"]+"\n"
            };
            using(var fallbackService=new UpdateService(manifestHandler)) {
                var fallback=await fallbackService.CheckAsync("0.7.10","win-x64",false,true,default);
                Check(fallback?.Asset.Sha256==Hash(payload),"Missing GitHub digests fall back to the versioned checksum manifest, including UTF-8 BOM");
            }
            var hostileAsset=Asset("v0.7.13","win-x64",false,payload); hostileAsset["browser_download_url"]="https://example.com/"+hostileAsset["name"];
            using(var hostileService=new UpdateService(new UpdateHandler(JsonSerializer.Serialize(new[]{Release("v0.7.13",true,hostileAsset)}),payload)))
                await Reject(async()=>{await hostileService.CheckAsync("0.7.10","win-x64",false,true,default);},"A mismatched repository asset URL is rejected");
            foreach(string extra in new[]{"Mizu-win-x64/../outside.txt","Mizu-win-x64/app/CON.txt","Mizu-win-x64/app/MIZU.EXE","other-root/file.txt","Mizu-win-x64/app/file:stream"}) {
                string archive=Path.Combine(folder,Guid.NewGuid()+".zip"); File.WriteAllBytes(archive,Zip(extra));
                bool rejected=false;try{PackageArchive.Inspect(archive,"win-x64");}catch(IOException){rejected=true;}
                Check(rejected,"Unsafe ZIP entry is rejected: "+extra);
            }
            string wrongArch=Path.Combine(folder,"wrong.zip"); File.WriteAllBytes(wrongArch,Zip(machine:0x14c));
            bool archRejected=false;try{PackageArchive.Inspect(wrongArch,"win-x64");}catch(IOException){archRejected=true;}
            Check(archRejected,"A portable update with the wrong native architecture is rejected");
            string validArchive=Path.Combine(folder,"valid.zip"); File.WriteAllBytes(validArchive,payload);
            string extracted=PackageArchive.Extract(validArchive,"win-x64",Path.Combine(folder,"extracted"));
            Check(File.Exists(Path.Combine(extracted,"app","Mizu.exe")),"A valid package extracts inside its staging directory");
            var persisted=new AppSettings {AutoCheckUpdates=false,ReceivePreviewUpdates=false,LastUpdateCheckUtc=DateTimeOffset.UtcNow,Todos=[new(){Text="preserve todo"}],Vocabulary=[new(){Word="preserve",Meaning="保留"}]};
            var store=new SettingsStore(Path.Combine(folder,"settings.json")); store.Save(persisted);
            var loaded=store.Load();
            Check(!loaded.AutoCheckUpdates && !loaded.ReceivePreviewUpdates && loaded.Todos.Single().Text=="preserve todo" && loaded.Vocabulary.Single().Meaning=="保留","Update preferences persist without losing existing user data");
            string cleanupRoot=Path.Combine(folder,"cleanup");Directory.CreateDirectory(cleanupRoot);
            var jobs=new List<string>();
            foreach(string phase in new[]{"Completed","Completed","Applying"}) {
                string id=Guid.NewGuid().ToString("N"),path=Path.Combine(cleanupRoot,id);Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path,"plan.json"),JsonSerializer.Serialize(new UpdatePlan{Session=id,TargetRoot=root}));
                File.WriteAllText(Path.Combine(path,"journal.json"),JsonSerializer.Serialize(new{Phase=phase}));jobs.Add(path);
            }
            Directory.SetLastWriteTimeUtc(jobs[0],DateTime.UtcNow.AddDays(-3));Directory.SetLastWriteTimeUtc(jobs[1],DateTime.UtcNow.AddDays(-1));
            UpdateService.CleanupCompletedJobs(cleanupRoot,root);
            Check(!Directory.Exists(jobs[0]) && Directory.Exists(jobs[1]) && Directory.Exists(jobs[2]),"Cleanup retains the newest successful backup and every interrupted transaction");
        } finally {
            string absolute=Path.GetFullPath(folder);
            if(!absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("mizu-update-core-"))throw new Exception("Unsafe test cleanup");
            Directory.Delete(absolute,true);
        }
        return count;
    }
}

sealed class UpdateHandler(string metadata,byte[] payload) : HttpMessageHandler
{
    public Uri? Redirect;
    public string? Manifest;
    public int Downloads;
    public Func<Stream>? InputFactory;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(request.RequestUri!.Host=="api.github.com")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(metadata,Encoding.UTF8,"application/json")});
        if(request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Manifest??"")});
        Downloads++;
        if(Redirect is not null){var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=Redirect;return Task.FromResult(response);}
        HttpContent content=InputFactory is null ? new ByteArrayContent(payload) : new StreamContent(InputFactory());
        content.Headers.ContentLength=payload.Length;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
    }
}
sealed class BrokenUpdateStream(byte[] payload,Action? cancel=null) : MemoryStream(payload)
{
    private int reads;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
    {
        if(reads++==0)return base.ReadAsync(buffer[..Math.Min(buffer.Length,17)],token);
        if(cancel is not null){cancel();token.ThrowIfCancellationRequested();}
        return ValueTask.FromException<int>(new IOException("simulated connection loss"));
    }
}
