using NitroPulse;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;

internal static class InstallerTests
{
    public static async Task Run()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] bytes = File.ReadAllBytes(typeof(InstallerTests).Assembly.Location);
        var version = new Version(1,0,12,0);
        var asset = new UpdateAsset(new Uri("https://github.com/test/NitroPulse/releases/download/v1.0.11/NitroPulse.exe"), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), false);
        var release = new ReleaseInfo(version,"v1.0.11", new Uri("https://github.com/test/NitroPulse/releases/tag/v1.0.11"),asset);
        var ready = await UpdateInstaller.DownloadCoreAsync(release, CancellationToken.None, new FakeHandler(bytes), Path.Combine(root,"good"));
        if (ready.Hash != asset.Sha256) throw new Exception("Download did not verify");
        async Task Reject(ReleaseInfo bad, FakeHandler handler, string folder) {
            try { await UpdateInstaller.DownloadCoreAsync(bad, CancellationToken.None,handler,Path.Combine(root,folder)); }
            catch(InvalidDataException) { return; } throw new Exception("Unsafe download accepted: " + folder);
        }
        await Reject(release with {Asset=asset with {Sha256=new string('0',64)}},new FakeHandler(bytes),"bad-hash");
        await Reject(release,new FakeHandler(bytes[..100]),"incomplete");
        await Reject(release,new FakeHandler(bytes,new Uri("https://evil.com/payload")),"evil-redirect");
        await Reject(release with {Version=new Version(9,0,0,0)},new FakeHandler(bytes),"wrong-version");
        foreach(string url in new[]{"http://github.com/file","https://github.com.evil.com/file","https://user@github.com/file"})
            if(UpdateInstaller.AllowedDownload(new Uri(url))) throw new Exception("Unsafe host accepted");
        string zipPath = Path.Combine(root,"good.zip");
        using(var zip = ZipFile.Open(zipPath,ZipArchiveMode.Create)) { var entry=zip.CreateEntry("Aplicacion/NitroPulse.exe"); using var output=entry.Open(); output.Write(bytes); }
        UpdateInstaller.ExtractExecutable(zipPath,Path.Combine(root,"extracted.exe"));
        UpdateInstaller.ValidateExecutable(Path.Combine(root,"extracted.exe"),version);
        string badZip = Path.Combine(root,"bad.zip");
        using(var zip = ZipFile.Open(badZip,ZipArchiveMode.Create)) zip.CreateEntry("../NitroPulse.exe");
        bool rejected=false; try {UpdateInstaller.ExtractExecutable(badZip,Path.Combine(root,"outside.exe"));} catch(InvalidDataException){rejected=true;}
        if(!rejected) throw new Exception("ZIP traversal accepted");
        Console.WriteLine("PASS: descarga íntegra, hash incorrecto, descarga incompleta, redirección ajena, versión incorrecta, hosts HTTPS y extracción ZIP segura.");
    }
    private sealed class FakeHandler(byte[] bytes, Uri? redirect=null): HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(
            redirect is null ? new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)} :
                new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=redirect}});
    }
}
