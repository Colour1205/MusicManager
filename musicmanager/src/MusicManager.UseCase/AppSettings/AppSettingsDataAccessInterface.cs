namespace MusicManager.UseCase.AppSettings;

// the entity is fully qualified on purpose: inside MusicManager.UseCase the simple name "AppSettings" is this namespace

public interface IAppSettingsDataAccess
{
    /// <summary>
    /// loads persisted settings, or an AppSettings with an empty path and no formats if none have been saved yet
    /// </summary>
    MusicManager.Entity.AppSettings Load();

    void Save(MusicManager.Entity.AppSettings settings);
}
