using MusicManager.Common;
using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>
/// Standardizes artist tags in two steps the user can review in between:
/// Analyze() reads every song's tags, has Claude (or the conservative local fallback)
/// split and group the artist names, and returns a plan; Apply() writes the plan,
/// including any canonical names the user edited, back into the audio files.
/// </summary>
public class StandardizeArtistsInteractor : IStandardizeArtistsInputBoundary
{
    private readonly IStandardizeArtistsDataAccess _dataAccess;
    private readonly IStandardizeArtistsOutputBoundary _outputBoundary;

    public StandardizeArtistsInteractor(IStandardizeArtistsDataAccess dataAccess,
            IStandardizeArtistsOutputBoundary outputBoundary)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
    }

    public void Analyze(StandardizeArtistsAnalyzeInputData inputData)
    {
        _outputBoundary.PresentProgress("Reading artist tags...", 0, 0);
        List<SongArtistTags> songs = _dataAccess.ReadArtistTags(inputData.Path);
        if (songs.Count == 0)
        {
            _outputBoundary.PresentError("No audio files with readable tags were found in that folder.");
            return;
        }

        IDictionary<string, int> rawCounts = ArtistStandardizer.CountRawValues(songs);
        IDictionary<string, List<ParsedArtist>> parsed;
        bool usedAi = false;
        List<string> notes = new List<string>();

        if (inputData.IsUseAi)
        {
            string apiKey = inputData.ApiKey;
            if (apiKey == null || JavaString.Trim(apiKey).Length == 0)
            {
                apiKey = _dataAccess.ReadApiKeyFromEnvironment();
            }
            // a key is only needed for names Claude has not seen yet: answers it gave earlier are saved, so a library
            // whose artists are all known works without one. The data access says so if it does need the key.
            bool haveKey = apiKey != null && JavaString.Trim(apiKey).Length != 0;

            _outputBoundary.PresentProgress("Asking Claude to group " + rawCounts.Count + " artist names...", 0, 0);
            try
            {
                parsed = _dataAccess.ParseArtistsWithAi(rawCounts, haveKey ? JavaString.Trim(apiKey) : null,
                        inputData.Model, notes, message => _outputBoundary.PresentProgress(message, 0, 0));
                usedAi = true;
            }
            catch (ArtistAiException e)
            {
                _outputBoundary.PresentError(haveKey ? "Claude could not be used: " + e.Message
                        : "No Anthropic API key found. Set the ANTHROPIC_API_KEY environment variable, type a key "
                                + "into the window, or untick \"Use Claude\".");
                return;
            }
        }
        else
        {
            parsed = ArtistStandardizer.ParseLocally(rawCounts.Keys);
        }

        // the names come from every song of the folder, only the songs in scope get their tags changed
        ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, parsed, inputData.Separator, usedAi,
                inputData.AudioPaths);
        plan.Warnings.AddRange(notes);
        _outputBoundary.PresentAnalysis(new StandardizeArtistsAnalysisOutputData(plan,
                ArtistStandardizer.ComputeUpdates(plan), ArtistStandardizer.ComputeRenames(plan)));
    }

    public void Review(StandardizeArtistsApplyInputData inputData)
    {
        ArtistPlan plan = inputData.Plan;
        _outputBoundary.PresentAnalysis(new StandardizeArtistsAnalysisOutputData(plan,
                ArtistStandardizer.ComputeUpdates(plan), ArtistStandardizer.ComputeRenames(plan)));
    }

    public void Apply(StandardizeArtistsApplyInputData inputData)
    {
        ArtistPlan plan = inputData.Plan;
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);

        int updated = 0;
        List<string> failed = new List<string>();
        foreach (SongArtistUpdate update in updates)
        {
            if (_dataAccess.WriteArtistTags(update))
            {
                updated++;
            }
            else
            {
                failed.Add(update.AudioPath);
            }
            _outputBoundary.PresentProgress("Writing tags...", updated + failed.Count, updates.Count);
        }

        _outputBoundary.PresentApplied(
                new StandardizeArtistsApplyOutputData(updated, plan.ScopedSongCount - updates.Count, failed));
    }

    public void RenameFiles(StandardizeArtistsApplyInputData inputData)
    {
        ArtistPlan plan = inputData.Plan;
        // the songs whose files are not named after their tags (as the plan sees them: the tags are read again for each
        // one when it is renamed, so a tag that could not be written does not make a wrong name)
        List<SongRename> renames = ArtistStandardizer.ComputeRenames(plan);

        int renamed = 0;
        int alreadyNamed = 0;
        List<SongRenameResult> skipped = new List<SongRenameResult>();
        int done = 0;
        foreach (SongRename rename in renames)
        {
            SongRenameResult result = _dataAccess.RenameSongFiles(inputData.CurrentPath(rename.AudioPath), plan.Separator);
            switch (result.Status)
            {
                case SongRenameResult.Kind.Renamed:
                    renamed++;
                    break;
                case SongRenameResult.Kind.AlreadyNamed:
                    alreadyNamed++;
                    break;
                default:
                    skipped.Add(result);
                    break;
            }
            _outputBoundary.PresentProgress("Renaming files...", ++done, renames.Count);
        }

        _outputBoundary.PresentRenamed(new StandardizeArtistsRenameOutputData(renamed, alreadyNamed, skipped));
    }

    public bool IsApiKeyAvailable => _dataAccess.ReadApiKeyFromEnvironment() != null;
}
