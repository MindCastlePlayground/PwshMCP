using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PwshMcp.Execution;

public static class FreshEnvironment
{
    public static Dictionary<string, string> Create(Dictionary<string, string?>? overrides = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!OpenProcessToken(GetCurrentProcess(), NativeNames.TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Dictionary<string, string> values;
        try { values = ReadBlock(token); }
        finally { CloseHandle(token); }

        var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in EnvNames.GeneratedNames)
            if (values.TryGetValue(name, out var value)) generated[name] = value;
        if (!string.IsNullOrEmpty(identity.Name)) generated[EnvNames.UserName] = identity.Name[(identity.Name.LastIndexOf('\\') + 1)..];

        var machine = ReadRegistry(Registry.LocalMachine, EnvNames.MachineEnvKey);
        var user = ReadRegistry(Registry.CurrentUser, EnvNames.UserEnvKey);
        foreach (var pair in machine) values[pair.Key] = pair.Value.Value;
        foreach (var pair in user) values[pair.Key] = pair.Value.Value;
        foreach (var pair in machine.Concat(user))
            if (pair.Value.Expand) values[pair.Key] = Expand(pair.Value.Value, values);

        if (machine.TryGetValue(EnvNames.Path, out var machinePath) || user.TryGetValue(EnvNames.Path, out _))
        {
            var m = machine.TryGetValue(EnvNames.Path, out machinePath) ? Resolve(machinePath, values) : "";
            var u = user.TryGetValue(EnvNames.Path, out var userPath) ? Resolve(userPath, values) : "";
            values[EnvNames.Path] = JoinPath(m, u);
        }
        foreach (var pair in generated) values[pair.Key] = pair.Value;
        EnsureDefaults(values);

        if (overrides is not null)
            foreach (var pair in overrides)
                if (pair.Value is null) values.Remove(pair.Key); else values[pair.Key] = pair.Value;
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadBlock(IntPtr token)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!CreateEnvironmentBlock(out var block, token, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (var p = block; Marshal.ReadInt16(p) != 0;)
            {
                var item = Marshal.PtrToStringUni(p);
                if (string.IsNullOrEmpty(item)) break;
                var split = item[0] == '=' ? item.IndexOf('=', 1) : item.IndexOf('=');
                if (split > 0) result[item[..split]] = item[(split + 1)..];
                p = IntPtr.Add(p, (item.Length + 1) * 2);
            }
        }
        finally { DestroyEnvironmentBlock(block); }
        return result;
    }

    private static Dictionary<string, EnvValue> ReadRegistry(RegistryKey root, string path)
    {
        var result = new Dictionary<string, EnvValue>(StringComparer.OrdinalIgnoreCase);
        using var key = root.OpenSubKey(path);
        if (key is null) return result;
        foreach (var name in key.GetValueNames())
        {
            var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null) continue;
            result[name] = new EnvValue(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? "",
                key.GetValueKind(name) == RegistryValueKind.ExpandString);
        }
        return result;
    }

    private static string Resolve(EnvValue item, Dictionary<string, string> values) => item.Expand ? Expand(item.Value, values) : item.Value;

    private static string Expand(string value, Dictionary<string, string> values)
    {
        for (var pass = 0; pass < 4; pass++)
            foreach (var pair in values)
                value = value.Replace("%" + pair.Key + "%", pair.Value, StringComparison.OrdinalIgnoreCase);
        return value;
    }

    private static string JoinPath(string machine, string user) => string.Join(";", new[] { machine, user }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static void EnsureDefaults(Dictionary<string, string> values)
    {
        if (!values.ContainsKey(EnvNames.Path))
        {
            var root = values.GetValueOrDefault(EnvNames.SystemRoot, @"C:\Windows");
            values[EnvNames.Path] = Path.Combine(root, "System32") + ";" + root + ";" + Path.Combine(root, "System32", "Wbem");
        }
        if (!values.ContainsKey(EnvNames.Temp) && values.TryGetValue(EnvNames.UserProfile, out var profile)) values[EnvNames.Temp] = Path.Combine(profile, "AppData", "Local", "Temp");
        if (!values.ContainsKey(EnvNames.Tmp) && values.TryGetValue(EnvNames.Temp, out var temp)) values[EnvNames.Tmp] = temp;
    }

    private readonly record struct EnvValue(string Value, bool Expand);

    [DllImport(NativeNames.Kernel32, SetLastError = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport(NativeNames.Advapi32, SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport(NativeNames.Kernel32, SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport(NativeNames.Userenv, SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
    [DllImport(NativeNames.Userenv, SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
