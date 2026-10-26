namespace MusicManager.Common;

/// <summary>
/// Java's System.getProperty("user.home"). The caches and the settings file live under it, so a test points it at a
/// temporary folder (set Path; null again restores the real one). Tests that set it must not run in parallel with
/// each other: the test project switches xUnit's parallelism off.
/// </summary>
public static class UserHome
{
    private static string _override;

    public static string Path
    {
        get => _override
               ?? Environment.GetEnvironmentVariable("MUSICMANAGER_HOME")
               ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        set => _override = value;
    }
}
