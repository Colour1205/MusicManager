using Microsoft.Win32;
using MusicManager.Common;

namespace MusicManager.DataAccess;

/// <summary>
/// reads an environment variable the way Windows stores it (the user's, then the machine's), for programs that
/// were started before the variable was added: a process only inherits the variables that existed when its parent
/// (Explorer, a terminal) started, so a variable set later is invisible to Environment.GetEnvironmentVariable until
/// the next sign-in.
/// </summary>
public static class WindowsEnvironment
{
    // the Java version ran "reg query" on these two keys; here the registry is read directly
    private static readonly (RegistryHive Hive, string SubKey)[] RegistryKeys =
    {
        (RegistryHive.CurrentUser, "Environment"),
        (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")
    };

    /// <summary>the variable from the process environment, else from the Windows environment store; null if it is set nowhere</summary>
    public static string Read(string name)
    {
        string fromProcess = Clean(Environment.GetEnvironmentVariable(name));
        if (fromProcess != null || !OperatingSystem.IsWindows())
        {
            return fromProcess;
        }
        foreach ((RegistryHive hive, string subKey) in RegistryKeys)
        {
            string value = Clean(Query(hive, subKey, name));
            if (value != null)
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>the text value in one registry key, or null if the key or the value is missing or cannot be read</summary>
    private static string Query(RegistryHive hive, string subKey, string name)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey key = root.OpenSubKey(subKey, false);
            return key == null ? null : ReadText(key, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// the value called name (without regard to case) if it holds text, i.e. is of type REG_SZ or REG_EXPAND_SZ, the way
    /// "reg query" prints it: a REG_EXPAND_SZ value is not expanded. Null if there is none or it is of another type.
    /// </summary>
    public static string ReadText(RegistryKey key, string name)
    {
        if (key == null || name == null)
        {
            return null;
        }
        object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null)
        {
            return null;
        }
        RegistryValueKind kind = key.GetValueKind(name);
        return kind == RegistryValueKind.String || kind == RegistryValueKind.ExpandString ? (string)value : null;
    }

    private static string Clean(string value)
    {
        return value == null || JavaString.Trim(value).Length == 0 ? null : JavaString.Trim(value);
    }
}
