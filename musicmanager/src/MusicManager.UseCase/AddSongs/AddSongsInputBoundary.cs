namespace MusicManager.UseCase.AddSongs;

public interface IAddSongsInputBoundary
{
    /// <summary>
    /// stages the picked songs for the library (blocks until done, so call it off the UI thread); the result is presented. The
    /// library itself is not changed: the songs go into it with <see cref="Commit"/>, or are thrown away with <see cref="Discard"/>.
    /// </summary>
    void Add(AddSongsInputData inputData);

    /// <summary>the user confirmed: puts the staged songs into the library, all or none; the result is presented (blocks until done)</summary>
    void Commit();

    /// <summary>the user did not confirm: throws the staged songs away; the library was never touched</summary>
    void Discard();
}
