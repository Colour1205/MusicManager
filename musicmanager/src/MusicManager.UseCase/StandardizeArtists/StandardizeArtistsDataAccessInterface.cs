using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeArtists;

public interface IStandardizeArtistsDataAccess
{
    /// <summary>reads the artist-related tags of every audio file under path (recursively)</summary>
    List<SongArtistTags> ReadArtistTags(string path);

    /// <summary>
    /// asks the AI to split each raw tag value into its individual artists and give each one's official name
    /// (Latin-script, except Chinese-language artists, who keep their Chinese name), then has the uncertain names
    /// checked on the web. Every key of rawCounts must appear in the result.
    /// </summary>
    /// <param name="rawCounts">raw tag value -> number of tag values using it (a hint for which spelling is the usual one)</param>
    /// <param name="apiKey">the Anthropic API key</param>
    /// <param name="model">the model id to use</param>
    /// <param name="notes">receives a sentence for anything the user should know, e.g. that the web check was skipped</param>
    /// <param name="status">receives a short sentence before each request, to show progress; may be null</param>
    /// <exception cref="ArtistAiException">if the service cannot be used</exception>
    IDictionary<string, List<ParsedArtist>> ParseArtistsWithAi(IDictionary<string, int> rawCounts, string apiKey,
            string model, List<string> notes, Action<string> status);

    /// <summary>the key from the ANTHROPIC_API_KEY environment variable (including one set after this program started), or null if it is not set</summary>
    string ReadApiKeyFromEnvironment();

    /// <summary>writes the update to the audio file and verifies it by reading it back; returns true on success</summary>
    bool WriteArtistTags(SongArtistUpdate update);
}
