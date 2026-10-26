namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>the AI service could not be used (no/invalid key, network failure, unusable answer)</summary>
public class ArtistAiException : Exception
{
    public ArtistAiException(string message) : base(message)
    {
    }

    public ArtistAiException(string message, Exception cause) : base(message, cause)
    {
    }
}
