namespace MusicManager.UseCase.AddSongs;

/// <summary>what became of the songs that were waiting to be added when the user confirmed</summary>
public class AddCommitOutputData
{
    private readonly int _songs;
    private readonly string _problem;

    /// <param name="songs">how many songs went into the library</param>
    /// <param name="problem">null when they did; otherwise why none did (the library is as it was)</param>
    public AddCommitOutputData(int songs, string problem)
    {
        _songs = songs;
        _problem = problem;
    }

    public int Songs => _songs;

    public string Problem => _problem;

    public bool IsDone => _problem == null;
}
