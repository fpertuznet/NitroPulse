using NitroPulse;
int count = 0;
void Check(bool result) { if (!result) throw new Exception("Test failed " + count); count++; }
Check(UpdateChecker.TryVersion("v1.0.12", out var newer));
Check(newer > new Version(1,0,11,0));
Check(UpdateChecker.TryVersion("1.0.11.0", out var same) && same == new Version(1,0,11,0));
foreach (var invalid in new[]{"v1.0.12-beta", "1.0", "99999999999999.0.0", "banana"}) Check(!UpdateChecker.TryVersion(invalid,out _));
Check(UpdateChecker.TryRepository("https://github.com/test/NitroPulse.git",out var repo) && repo == "test/NitroPulse");
Check(!UpdateChecker.TryRepository("https://evil.com/test/NitroPulse",out _));
string Json(string url, bool preview=false) => System.Text.Json.JsonSerializer.Serialize(new {draft=false,prerelease=preview,tag_name="v1.0.12",html_url=url});
Check(UpdateChecker.Parse(Json("https://github.com/test/NitroPulse/releases/tag/v1.0.12"),repo)?.Version == newer);
Check(UpdateChecker.Parse(Json("https://github.com/test/NitroPulse/releases/tag/v1.0.12",true),repo) is null);
foreach (var url in new[]{"http://github.com/test/NitroPulse/releases/tag/v1.0.12","https://evil.com/test/NitroPulse/releases/tag/v1.0.12","https://github.com/other/NitroPulse/releases/tag/v1.0.12"}) {
    bool rejected=false; try { UpdateChecker.Parse(Json(url),repo); } catch(InvalidDataException) {rejected=true;} Check(rejected);
}
Console.WriteLine($"PASS: {count} pruebas de versiones, repositorios y enlaces seguros.");
await InstallerTests.Run();
await QueryTests.Run();
