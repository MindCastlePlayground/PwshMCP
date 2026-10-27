using Microsoft.Win32;
using PwshMcp.Execution;
using System.Security.Principal;

const string probe = "PWMC_ENV_TEST_VALUE";
const string pathProbe = "Path";
using var key = Registry.CurrentUser.CreateSubKey("Environment", true)!;
var oldProbe = key.GetValue(probe, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
var hadProbe = key.GetValueNames().Contains(probe, StringComparer.OrdinalIgnoreCase);
var oldKind = hadProbe ? key.GetValueKind(probe) : RegistryValueKind.String;
var oldPath = key.GetValue(pathProbe, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
var hadPath = key.GetValueNames().Contains(pathProbe, StringComparer.OrdinalIgnoreCase);
var oldPathKind = hadPath ? key.GetValueKind(pathProbe) : RegistryValueKind.String;
var userDir = Path.Combine(Path.GetTempPath(), "pwmc-env-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(userDir);
var exe = Path.Combine(userDir, "pwmc-env-probe.cmd");
File.WriteAllText(exe, "@echo off\r\n");

try
{
    key.SetValue(pathProbe, userDir, RegistryValueKind.String);
    key.SetValue(probe, "first", RegistryValueKind.String);
    var first = FreshEnvironment.Create();
    Check(first.TryGetValue("USERPROFILE", out var profile) && profile == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "current USERPROFILE");
    Check(first.TryGetValue("USERNAME", out var user) && user == Environment.UserName, "current USERNAME");
    Check(first["Path"].Contains(userDir, StringComparison.OrdinalIgnoreCase), "user PATH entry");
    var machinePath = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")?.GetValue("Path") as string ?? "";
    Check(first["Path"].Contains(machinePath.Split(';')[0], StringComparison.OrdinalIgnoreCase), "machine PATH entry");
    Check(Find("pwmc-env-probe.cmd", first["Path"]) == exe, "command discovery uses fresh PATH");
    Check(FreshEnvironment.Create(new() { [probe] = "%USERNAME%" })[probe] == "%USERNAME%", "literal override remains literal");
    Check(!FreshEnvironment.Create(new() { ["Path"] = null }).ContainsKey("Path"), "PATH deletion honored");

    key.SetValue(probe, "second", RegistryValueKind.String);
    Check(FreshEnvironment.Create()[probe] == "second", "fresh user value reflected");
    key.DeleteValue(probe, false);
    Check(!FreshEnvironment.Create().ContainsKey(probe), "user value deletion reflected");
    Console.WriteLine("PASS: fresh environment, current user, combined PATH, command discovery, literal override, deletion and restoration");
}
finally
{
    Restore(key, probe, hadProbe, oldProbe, oldKind);
    Restore(key, pathProbe, hadPath, oldPath, oldPathKind);
    File.Delete(exe);
    Directory.Delete(userDir);
}

static string? Find(string name, string path) => path.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);
static void Restore(RegistryKey key, string name, bool existed, object? value, RegistryValueKind kind)
{
    if (existed) key.SetValue(name, value!, kind); else key.DeleteValue(name, false);
}
static void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}
