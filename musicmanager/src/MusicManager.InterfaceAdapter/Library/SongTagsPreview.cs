namespace MusicManager.InterfaceAdapter.Library;

/// <summary>
/// What Process would do to one song's tags (artist names), ready to show in the song window. Made by
/// <see cref="SongPreviewMapper"/> from the artist plan's updates for this song.
/// </summary>
public sealed class SongTagsPreview
{
    /// <summary>one tag field changing from before to after</summary>
    public sealed class TagLine
    {
        private readonly string _label; // "Artist", "Album artist", "Artists"
        private readonly string _before;
        private readonly string _after;

        public TagLine(string label, string before, string after)
        {
            _label = label;
            _before = before;
            _after = after;
        }

        public string Label => _label;

        public string Before => _before;

        public string After => _after;
    }

    private readonly List<TagLine> _changes;
    private readonly string _message;

    /// <param name="changes">the tag fields that change; empty when none do</param>
    /// <param name="message">
    /// the sentence for a song with nothing to show ("Nothing changes", "Artist names are off in Options", a failure
    /// reason); empty when changes are shown
    /// </param>
    public SongTagsPreview(List<TagLine> changes, string message)
    {
        _changes = changes == null ? new List<TagLine>() : changes;
        _message = message == null ? "" : message;
    }

    public List<TagLine> Changes => _changes;

    public string Message => _message;
}
