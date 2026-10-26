using TagLib;
using TagFile = TagLib.File;
using Id3v2Tag = TagLib.Id3v2.Tag;

namespace MusicManager.DataAccess;

/// <summary>
/// The tag of one audio file with the handful of fields the application uses (title, artists, album artists, the
/// multi-valued ARTISTS field, album, cover art), the way the Java code got them from jaudiotagger. TagLibSharp
/// differs from jaudiotagger in a few places that change results, and this class irons them out so that the
/// callers (<see cref="LibraryScanner"/>, <see cref="ArtistTags"/>, <see cref="DataAccess"/>) behave like the Java ones:
/// <list type="bullet">
/// <item>a file has ONE tag, the one jaudiotagger picks: the Vorbis comment of FLAC and Ogg, the ID3v2 tag of MP3, WAV
/// and AAC files (ID3v1 only if there is no ID3v2; the RIFF INFO list of a WAV only if there is no ID3 chunk) and the
/// ilst of MP4. Other tags of the same file (APE, ...) are not looked at.</item>
/// <item>ID3v2 text frames are split at the NUL separators only. TagLibSharp also splits the artist frames of ID3v2.2
/// and 2.3 tags at '/', which turns "AC/DC" into two artists; jaudiotagger keeps "AC/DC" as one value.</item>
/// <item>the ARTISTS field of ID3v2 is the TXXX frame described "ARTISTS", matched case sensitively.</item>
/// <item>an MP3 or AAC file without any tag has no tag at all (<see cref="HasTag"/> is false); the other formats always have one.</item>
/// </list>
/// </summary>
internal sealed class AudioTags : IDisposable
{
    /// <summary>the tag fields used (jaudiotagger's FieldKey TITLE, ARTIST, ALBUM_ARTIST, ARTISTS and ALBUM)</summary>
    internal enum Field
    {
        Title,
        Artist,
        AlbumArtist,
        Artists,
        Album
    }

    private enum Kind
    {
        /// <summary>no tag in the file</summary>
        None,
        /// <summary>Vorbis comment (FLAC, Ogg)</summary>
        Xiph,
        Id3v2,
        Id3v1,
        /// <summary>the ilst atom of MP4</summary>
        Apple,
        /// <summary>the INFO list of a WAV file</summary>
        RiffInfo,
        /// <summary>any other tag: only the plain fields</summary>
        Other
    }

    private const string AppleMean = "com.apple.iTunes";

    // jaudiotagger cannot write to a WAV file that has an INFO list but no ID3 chunk
    private const string RiffInfoReadOnly = "the INFO list of a WAV file is only read";

    private static readonly ByteVector InfoINAM = ByteVector.FromString("INAM", StringType.Latin1);
    private static readonly ByteVector InfoIART = ByteVector.FromString("IART", StringType.Latin1);
    private static readonly ByteVector InfoIPRD = ByteVector.FromString("IPRD", StringType.Latin1);
    private static readonly ByteVector FrameTIT2 = ByteVector.FromString("TIT2", StringType.Latin1);
    private static readonly ByteVector FrameTPE1 = ByteVector.FromString("TPE1", StringType.Latin1);
    private static readonly ByteVector FrameTPE2 = ByteVector.FromString("TPE2", StringType.Latin1);
    private static readonly ByteVector FrameTALB = ByteVector.FromString("TALB", StringType.Latin1);
    private static readonly ByteVector FrameTXXX = ByteVector.FromString("TXXX", StringType.Latin1);

    // frame creators are process wide: only the frames of the files this class opens are RawTextFrames, so that other users
    // of TagLibSharp in the process (and their changes to a frame) are not affected
    [ThreadStatic]
    private static bool _opening;

    private readonly TagFile _file;
    private TagLib.Tag _tag;
    private Kind _kind;

    static AudioTags()
    {
        // the text frames of the files opened here become RawTextFrames
        TagLib.Id3v2.FrameFactory.AddFrameCreator(CreateFrame);
    }

    private AudioTags(TagFile file)
    {
        _file = file;
        Pick();
    }

    /// <summary>
    /// reads the tag of an audio file, picking the format from the file extension
    /// </summary>
    /// <exception cref="Exception">if the file is not a readable audio file of a supported format</exception>
    internal static AudioTags Open(string path)
    {
        _opening = true;
        try
        {
            return new AudioTags(TagFile.Create(path));
        }
        finally
        {
            _opening = false;
        }
    }

    /// <summary>false if the file has no tag at all (an MP3 or AAC file without ID3 data)</summary>
    internal bool HasTag => _kind != Kind.None || !(_file is TagLib.Mpeg.AudioFile || _file is TagLib.Aac.File);

    public void Dispose()
    {
        _file.Dispose();
    }

    private void Pick()
    {
        TagLib.Tag tag;
        if ((tag = Existing(TagTypes.Xiph)) != null)
        {
            Set(tag, Kind.Xiph);
        }
        else if ((tag = Existing(TagTypes.Id3v2)) != null)
        {
            Set(tag, Kind.Id3v2);
        }
        else if ((tag = Existing(TagTypes.Apple)) != null)
        {
            Set(tag, Kind.Apple);
        }
        else if ((tag = Existing(TagTypes.Id3v1)) != null)
        {
            Set(tag, Kind.Id3v1);
        }
        else if ((tag = Existing(TagTypes.RiffInfo)) != null)
        {
            Set(tag, Kind.RiffInfo);
        }
        else
        {
            _tag = null;
            _kind = Kind.None;
        }
    }

    /// <summary>
    /// the tag of that type if the file really has one on disk: TagLibSharp makes up an empty ID3v1 and ID3v2 tag for every
    /// MP3 file (and ID3v2 and INFO for every WAV file) that has none, and fills the made-up ID3v2 tag of an MP3 file
    /// that only has ID3v1 from it
    /// </summary>
    private TagLib.Tag Existing(TagTypes type)
    {
        return (_file.TagTypesOnDisk & type) != 0 ? _file.GetTag(type, false) : null;
    }

    private void Set(TagLib.Tag tag, Kind kind)
    {
        _tag = tag;
        _kind = kind;
    }

    // ---- reading ----

    /// <summary>the first value of the field, "" if there is none</summary>
    internal string GetFirst(Field field)
    {
        List<string> values = GetAll(field);
        return values.Count == 0 ? "" : values[0];
    }

    /// <summary>all values of the field, in the file's order; empty if there is none or the tag format cannot hold the field</summary>
    internal List<string> GetAll(Field field)
    {
        var values = new List<string>();
        switch (_kind)
        {
            case Kind.Xiph:
                values.AddRange(((TagLib.Ogg.XiphComment)_tag).GetField(XiphName(field)));
                break;
            case Kind.Id3v2:
                values.AddRange(Id3Values((Id3v2Tag)_tag, field));
                break;
            case Kind.Apple:
                values.AddRange(AppleValues((TagLib.Mpeg4.AppleTag)_tag, field));
                break;
            case Kind.RiffInfo:
                values.AddRange(InfoValues((TagLib.Riff.ListTag)_tag, field));
                break;
            case Kind.Id3v1:
            case Kind.Other:
                values.AddRange(PlainValues(_tag, field));
                break;
        }
        return values;
    }

    /// <summary>the picture embedded in the file (the first one if there are several), or null if there is none</summary>
    internal byte[] GetFirstArtwork()
    {
        if (_file is TagLib.Flac.File)
        {
            // FLAC keeps its pictures in blocks of their own; a picture hidden in the Vorbis comment (as Ogg does it) is
            // not seen by jaudiotagger, so it is not seen here either
            TagLib.Tag blocks = _file.GetTag(TagTypes.FlacMetadata, false);
            return blocks != null && blocks.Pictures.Length > 0 ? blocks.Pictures[0].Data.Data : null;
        }
        return _tag != null && _tag.Pictures.Length > 0 ? _tag.Pictures[0].Data.Data : null;
    }

    private static string XiphName(Field field)
    {
        switch (field)
        {
            case Field.Title:
                return "TITLE";
            case Field.Artist:
                return "ARTIST";
            case Field.AlbumArtist:
                return "ALBUMARTIST";
            case Field.Artists:
                return "ARTISTS";
            default:
                return "ALBUM";
        }
    }

    private static IEnumerable<string> Id3Values(Id3v2Tag tag, Field field)
    {
        switch (field)
        {
            case Field.Title:
                return TextValues(tag, FrameTIT2);
            case Field.Artist:
                return TextValues(tag, FrameTPE1);
            case Field.AlbumArtist:
                return TextValues(tag, FrameTPE2);
            case Field.Album:
                return TextValues(tag, FrameTALB);
            default:
                TagLib.Id3v2.UserTextInformationFrame artists = ArtistsFrame(tag, false);
                return artists == null ? new string[0] : artists.Text;
        }
    }

    /// <summary>the values of the first frame with this id, split at NUL only</summary>
    private static string[] TextValues(Id3v2Tag tag, ByteVector id)
    {
        TagLib.Id3v2.TextInformationFrame frame = TagLib.Id3v2.TextInformationFrame.Get(tag, id, false);
        if (frame == null)
        {
            return new string[0];
        }
        if (frame is RawTextFrame raw && raw.Payload != null && raw.Payload.Count > 0)
        {
            return raw.Payload.ToStrings((StringType)raw.Payload[0], 1);
        }
        return frame.Text;
    }

    /// <summary>every text frame but the user defined ones (TXXX, which TagLibSharp reads right) becomes a RawTextFrame</summary>
    private static TagLib.Id3v2.Frame CreateFrame(ByteVector data, int offset, TagLib.Id3v2.FrameHeader header, byte version)
    {
        ByteVector id = header.FrameId;
        if (_opening && id.Count == 4 && id[0] == (byte)'T' && id != FrameTXXX)
        {
            return new RawTextFrame(data, offset, header, version);
        }
        return null;
    }

    /// <summary>
    /// a text frame that keeps its undecoded text: TagLibSharp splits the artist frames of ID3v2.2 and 2.3 at '/' and
    /// loses everything after a NUL there, which is not what jaudiotagger (and so the Java program) reads. The frame is
    /// also written back as it was read (when the tag is saved in the version it was read in), so that saving a tag does
    /// not cost the values of the frames that were not touched.
    /// </summary>
    private sealed class RawTextFrame : TagLib.Id3v2.TextInformationFrame
    {
        private byte _version;

        /// <summary>the frame's data after its header: the encoding byte, then the text</summary>
        internal ByteVector Payload { get; private set; }

        internal RawTextFrame(ByteVector data, int offset, TagLib.Id3v2.FrameHeader header, byte version)
            : base(data, offset, header, version)
        {
        }

        protected override void ParseFields(ByteVector data, byte version)
        {
            Payload = data;
            _version = version;
            base.ParseFields(data, version);
        }

        protected override ByteVector RenderFields(byte version)
        {
            return Payload != null && version == _version ? new ByteVector(Payload) : base.RenderFields(version);
        }
    }

    private static TagLib.Id3v2.UserTextInformationFrame ArtistsFrame(Id3v2Tag tag, bool create)
    {
        return TagLib.Id3v2.UserTextInformationFrame.Get(tag, "ARTISTS", StringType.UTF8, create, true);
    }

    private static IEnumerable<string> AppleValues(TagLib.Mpeg4.AppleTag tag, Field field)
    {
        switch (field)
        {
            case Field.Title:
                return Present(tag.Title);
            case Field.Artist:
                return tag.Performers;
            case Field.AlbumArtist:
                return tag.AlbumArtists;
            case Field.Album:
                return Present(tag.Album);
            default:
                return tag.GetDashBoxes(AppleMean, "ARTISTS");
        }
    }

    private static IEnumerable<string> PlainValues(TagLib.Tag tag, Field field)
    {
        switch (field)
        {
            case Field.Title:
                return Present(tag.Title);
            case Field.Artist:
                return tag.Performers;
            case Field.AlbumArtist:
                return tag.AlbumArtists;
            case Field.Album:
                return Present(tag.Album);
            default:
                return new string[0]; // the format has no ARTISTS field
        }
    }

    private static IEnumerable<string> InfoValues(TagLib.Riff.ListTag tag, Field field)
    {
        ByteVector id = InfoId(field);
        return id == null ? new string[0] : tag.GetValuesAsStrings(id);
    }

    /// <summary>the INFO chunk that holds the field; null for the fields that format cannot hold</summary>
    private static ByteVector InfoId(Field field)
    {
        switch (field)
        {
            case Field.Title:
                return InfoINAM;
            case Field.Artist:
                return InfoIART;
            case Field.Album:
                return InfoIPRD;
            default:
                return null;
        }
    }

    private static string[] Present(string value)
    {
        return value == null ? new string[0] : new[] { value };
    }

    // ---- writing ----

    /// <summary>makes sure the file has a tag to write to: the format's usual one (jaudiotagger's getTagOrCreateAndSetDefault)</summary>
    internal void CreateTagIfMissing()
    {
        if (_kind != Kind.None)
        {
            return;
        }
        if (_file is TagLib.Flac.File || _file is TagLib.Ogg.File)
        {
            Set(_file.GetTag(TagTypes.Xiph, true), Kind.Xiph);
        }
        else if (_file is TagLib.Mpeg4.File)
        {
            Set(_file.GetTag(TagTypes.Apple, true), Kind.Apple);
        }
        else
        {
            Set(_file.GetTag(TagTypes.Id3v2, true), Kind.Id3v2);
        }
    }

    /// <summary>removes every value of the field; nothing happens if there is none</summary>
    /// <exception cref="NotSupportedException">if the tag format cannot hold the field</exception>
    internal void DeleteField(Field field)
    {
        switch (_kind)
        {
            case Kind.Xiph:
                ((TagLib.Ogg.XiphComment)_tag).RemoveField(XiphName(field));
                break;
            case Kind.Id3v2:
                DeleteId3((Id3v2Tag)_tag, field);
                break;
            case Kind.Apple:
                SetApple((TagLib.Mpeg4.AppleTag)_tag, field, new string[0]);
                break;
            case Kind.RiffInfo:
                throw new NotSupportedException(RiffInfoReadOnly);
            case Kind.Id3v1:
            case Kind.Other:
                SetPlain(_tag, field, new string[0]);
                break;
            default:
                throw new InvalidOperationException("no tag");
        }
    }

    /// <summary>adds one more value to the field</summary>
    /// <exception cref="NotSupportedException">if the tag format cannot hold the field</exception>
    internal void AddField(Field field, string value)
    {
        var values = GetAll(field);
        values.Add(value);
        switch (_kind)
        {
            case Kind.Xiph:
                ((TagLib.Ogg.XiphComment)_tag).SetField(XiphName(field), values.ToArray());
                break;
            case Kind.Id3v2:
                SetId3((Id3v2Tag)_tag, field, values.ToArray());
                break;
            case Kind.Apple:
                SetApple((TagLib.Mpeg4.AppleTag)_tag, field, values.ToArray());
                break;
            case Kind.RiffInfo:
                throw new NotSupportedException(RiffInfoReadOnly);
            case Kind.Id3v1:
            case Kind.Other:
                SetPlain(_tag, field, values.ToArray());
                break;
            default:
                throw new InvalidOperationException("no tag");
        }
    }

    /// <summary>writes the changed tag to the file</summary>
    internal void Commit()
    {
        // the tags TagLibSharp made up (see Existing) must not be written as if they had been there: only the tag that was
        // edited and the ones that were on disk before are kept
        TagTypes madeUp = _file.TagTypes & ~_file.TagTypesOnDisk & ~(_tag == null ? TagTypes.None : _tag.TagTypes)
                & (TagTypes.Id3v1 | TagTypes.Id3v2 | TagTypes.RiffInfo | TagTypes.MovieId | TagTypes.DivX);
        if (madeUp != TagTypes.None)
        {
            _file.RemoveTags(madeUp);
        }
        _file.Save();
    }

    private static void DeleteId3(Id3v2Tag tag, Field field)
    {
        if (field == Field.Artists)
        {
            TagLib.Id3v2.UserTextInformationFrame frame;
            while ((frame = ArtistsFrame(tag, false)) != null)
            {
                tag.RemoveFrame(frame);
            }
            return;
        }
        tag.RemoveFrames(Id3FrameId(field));
    }

    private static ByteVector Id3FrameId(Field field)
    {
        switch (field)
        {
            case Field.Title:
                return FrameTIT2;
            case Field.Artist:
                return FrameTPE1;
            case Field.AlbumArtist:
                return FrameTPE2;
            default:
                return FrameTALB;
        }
    }

    /// <summary>all values in one frame (several values are separated by NUL in the file)</summary>
    private static void SetId3(Id3v2Tag tag, Field field, string[] values)
    {
        if (field == Field.Artists)
        {
            ArtistsFrame(tag, true).Text = values;
            return;
        }
        TagLib.Id3v2.TextInformationFrame frame = TagLib.Id3v2.TextInformationFrame.Get(tag, Id3FrameId(field), false);
        if (frame is RawTextFrame)
        {
            // such a frame is written as it was read, whatever is set on it: a new one takes its place
            tag.RemoveFrame(frame);
        }
        TagLib.Id3v2.TextInformationFrame.Get(tag, Id3FrameId(field), true).SetText(values);
    }

    private static void SetApple(TagLib.Mpeg4.AppleTag tag, Field field, string[] values)
    {
        switch (field)
        {
            case Field.Title:
                tag.Title = values.Length == 0 ? null : values[0];
                break;
            case Field.Artist:
                tag.Performers = values;
                break;
            case Field.AlbumArtist:
                tag.AlbumArtists = values;
                break;
            case Field.Album:
                tag.Album = values.Length == 0 ? null : values[0];
                break;
            default:
                tag.SetDashBoxes(AppleMean, "ARTISTS", values);
                break;
        }
    }

    private static void SetPlain(TagLib.Tag tag, Field field, string[] values)
    {
        switch (field)
        {
            case Field.Title:
                tag.Title = values.Length == 0 ? null : values[0];
                break;
            case Field.Artist:
                tag.Performers = values;
                break;
            case Field.AlbumArtist:
                if (tag is TagLib.Id3v1.Tag)
                {
                    throw new NotSupportedException("ID3v1 has no album artist"); // TagLibSharp would just ignore it
                }
                tag.AlbumArtists = values;
                break;
            case Field.Album:
                tag.Album = values.Length == 0 ? null : values[0];
                break;
            default:
                throw new NotSupportedException("the tag format has no ARTISTS field");
        }
    }
}
