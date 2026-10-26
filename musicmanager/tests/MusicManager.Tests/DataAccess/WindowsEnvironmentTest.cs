using Microsoft.Win32;
using MusicManager.DataAccess;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// The Java version parsed the output of "reg query"; the C# version reads the registry itself, so the four tests of the
/// parser read the same kinds of values (REG_SZ, REG_EXPAND_SZ, other types, names in another case, longer names) from a
/// scratch key under HKCU\Software that is deleted again.
/// </summary>
public class WindowsEnvironmentTest : IDisposable
{
    private readonly string _path = @"Software\MusicManager.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _key;

    public WindowsEnvironmentTest()
    {
        _key = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, false);
        using (RegistryKey parent = Registry.CurrentUser.OpenSubKey(@"Software\MusicManager.Tests", true))
        {
            if (parent != null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            {
                parent.Dispose();
                Registry.CurrentUser.DeleteSubKey(@"Software\MusicManager.Tests", false);
            }
        }
    }

    [Fact]
    public void ReadsTheValueOutOfRegQueryOutput()
    {
        _key.SetValue("ANTHROPIC_API_KEY", "sk-ant-api03-abc_DEF-123", RegistryValueKind.String);

        Assert.Equal("sk-ant-api03-abc_DEF-123", WindowsEnvironment.ReadText(_key, "ANTHROPIC_API_KEY"));
    }

    [Fact]
    public void ExpandableStringsAreRead()
    {
        _key.SetValue("NAME", "value", RegistryValueKind.ExpandString);

        Assert.Equal("value", WindowsEnvironment.ReadText(_key, "NAME"));
    }

    [Fact]
    public void TheNameIsMatchedWithoutCaseAndOnlyAsAWholeName()
    {
        _key.SetValue("anthropic_api_key", "one", RegistryValueKind.String);
        _key.SetValue("ANTHROPIC_API_KEY_OLD", "two", RegistryValueKind.String);

        Assert.Equal("one", WindowsEnvironment.ReadText(_key, "ANTHROPIC_API_KEY"));

        using (RegistryKey onlyLonger = Registry.CurrentUser.CreateSubKey(_path + @"\longer"))
        {
            onlyLonger.SetValue("ANTHROPIC_API_KEY_OLD", "two", RegistryValueKind.String);
            Assert.Null(WindowsEnvironment.ReadText(onlyLonger, "ANTHROPIC_API_KEY"));
        }
    }

    [Fact]
    public void NothingFoundMeansNull()
    {
        Assert.Null(WindowsEnvironment.ReadText(_key, "X"));
        Assert.Null(WindowsEnvironment.ReadText(null, "X"));
        Assert.Null(WindowsEnvironment.ReadText(Registry.CurrentUser.OpenSubKey(_path + @"\no such key"), "X"));
        _key.SetValue("X", 1, RegistryValueKind.DWord);
        Assert.Null(WindowsEnvironment.ReadText(_key, "X")); // only text values count
    }

    [Fact]
    public void AVariableThatIsSetNowhereIsNull()
    {
        Assert.Null(WindowsEnvironment.Read("MUSICMANAGER_TEST_VARIABLE_THAT_DOES_NOT_EXIST"));
    }
}
