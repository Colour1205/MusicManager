using System.Collections.ObjectModel;
using System.ComponentModel;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>How a result card's status line is coloured: accent = something will change, secondary = fine, critical = a problem.</summary>
public enum ResultTone
{
    Neutral,
    Accent,
    Critical,
}

/// <summary>
/// What one card of the review shows (Java: ResultCardList.Row): the song, what happens to it, the details under that, and
/// a status line. The card is <see cref="ResultCard"/>.
/// </summary>
public sealed class ResultCardItem
{
    public ResultCardItem(string title, string headline, string details, string status, ResultTone tone)
    {
        Title = title ?? "";
        Headline = headline ?? "";
        Details = details ?? "";
        Status = status ?? "";
        Tone = tone;
    }

    public string Title { get; }

    /// <summary>What happens to the song (empty for a card that only lists tag changes).</summary>
    public string Headline { get; }

    /// <summary>The lines under it, one per line.</summary>
    public string Details { get; }

    public string Status { get; }

    public ResultTone Tone { get; }

    /// <summary>What a screen reader says for the card (a list item is named after its ToString).</summary>
    public override string ToString()
    {
        string text = Title;
        if (Headline.Length != 0)
        {
            text += ", " + Headline;
        }
        if (Details.Length != 0)
        {
            text += ", " + Details.Replace("\n", ", ");
        }
        if (Status.Length != 0)
        {
            text += ", " + Status;
        }
        return text;
    }
}

/// <summary>
/// One artist group of the review (Java: ArtistCardList.ArtistCard): the group, the names its combo box offers, the text
/// about the spellings found, and whether the name can be edited right now. An edit is reported through the callback so the
/// tag-change preview can be redone. The card is <see cref="ArtistGroupCard"/>.
/// </summary>
public sealed class ArtistGroupItem : INotifyPropertyChanged
{
    private const int MaxSpellingsShown = 8;

    private readonly Action _onEdited;
    private readonly Func<IReadOnlyList<ReviewText.ArtistSong>> _songs;
    private bool _canEdit = true;

    /// <param name="songs">the songs the card counts, asked for only when the user opens the list</param>
    public ArtistGroupItem(ArtistGroup group, Action onEdited, Func<IReadOnlyList<ReviewText.ArtistSong>> songs)
    {
        Group = group;
        _onEdited = onEdited;
        _songs = songs;
        Candidates = new ObservableCollection<string>(CandidatesOf(group));
        SpellingsText = SpellingsOf(group);
        AllSpellings = AllSpellingsOf(group);
        Warning = WarningOf(group);
        SongCountText = ReviewTextSongs(group.SongCount);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public ArtistGroup Group { get; }

    /// <summary>
    /// The current name first, then every spelling the library uses, then the names Claude proposed. A name the user typed
    /// that is not among them is added at the top, so the combo box always has an item that is its text.
    /// </summary>
    public ObservableCollection<string> Candidates { get; }

    public string SpellingsText { get; }

    public string AllSpellings { get; }

    /// <summary>The red line of a group that was suggested by Claude ("Suggested with low confidence") or is not verified ("Not verified"); null for a sure one.</summary>
    public string Warning { get; }

    public string SongCountText { get; }

    /// <summary>The songs of this artist (what "N songs" counts), for the list the song count opens.</summary>
    public IReadOnlyList<ReviewText.ArtistSong> Songs()
    {
        return _songs == null ? new List<ReviewText.ArtistSong>() : _songs();
    }

    /// <summary>Names cannot be edited while a run is going: it works from the plan these cards change.</summary>
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (_canEdit != value)
            {
                _canEdit = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
            }
        }
    }

    /// <summary>Applies what the combo box shows (picked or typed) to the group; an empty field restores the old name.</summary>
    /// <returns>the text the combo box should show afterwards</returns>
    public string Commit(string typed)
    {
        string name = JavaString.Trim(typed ?? "");
        if (name.Length == 0)
        {
            return Group.CanonicalName;
        }
        if (!Candidates.Contains(name))
        {
            Candidates.Insert(0, name);
        }
        if (name != Group.CanonicalName)
        {
            Group.CanonicalName = name;
            _onEdited();
        }
        return name;
    }

    /// <summary>What a screen reader says for the card (a list item is named after its ToString).</summary>
    public override string ToString()
    {
        return Group.CanonicalName + ", " + SongCountText + (Warning == null ? "" : ", " + Warning);
    }

    private static string ReviewTextSongs(int count)
    {
        return count + (count == 1 ? " song" : " songs");
    }

    private static string SpellingsOf(ArtistGroup group)
    {
        var text = new System.Text.StringBuilder();
        int shown = 0;
        foreach (ArtistVariant variant in group.Variants)
        {
            if (variant.Count == 0)
            {
                continue; // a name proposed for the group that no song actually uses
            }
            if (shown == MaxSpellingsShown)
            {
                text.Append("  ·  …");
                break;
            }
            if (shown > 0)
            {
                text.Append("  ·  ");
            }
            text.Append(variant.Text).Append(" ×").Append(variant.Count);
            shown++;
        }
        return text.ToString();
    }

    private static string AllSpellingsOf(ArtistGroup group)
    {
        var text = new System.Text.StringBuilder();
        foreach (ArtistVariant variant in group.Variants)
        {
            if (variant.Count > 0)
            {
                text.Append(variant.Text).Append(" ×").Append(variant.Count).Append('\n');
            }
        }
        return text.ToString().TrimEnd('\n');
    }

    private static List<string> CandidatesOf(ArtistGroup group)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>();
        if (group.CanonicalName != null && seen.Add(group.CanonicalName))
        {
            candidates.Add(group.CanonicalName);
        }
        foreach (ArtistVariant variant in group.Variants)
        {
            if (variant.Text != null && seen.Add(variant.Text))
            {
                candidates.Add(variant.Text);
            }
        }
        return candidates;
    }

    /// <summary>the one red line of a card, always one of the same two texts: a suggested name, or a name that is not verified</summary>
    private static string WarningOf(ArtistGroup group)
    {
        if (group.IsSuggestedByAi)
        {
            return "Suggested with low confidence";
        }
        return group.IsLowConfidence ? "Not verified" : null;
    }
}
