using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// Standardizes the albums of a library in steps the user can review in between: Analyze() reads every song's album tag,
/// has Claude (or the conservative local fallback) say which album each spelling is, and returns a plan; Apply() writes the
/// plan, including any names the user edited, into the audio files' ALBUM tags; MoveFolders() puts the songs in folders
/// named after their albums.
/// </summary>
public class StandardizeAlbumsInteractor : IStandardizeAlbumsInputBoundary
{
    private readonly IStandardizeAlbumsDataAccess _dataAccess;
    private readonly IStandardizeAlbumsOutputBoundary _outputBoundary;

    public StandardizeAlbumsInteractor(IStandardizeAlbumsDataAccess dataAccess, IStandardizeAlbumsOutputBoundary outputBoundary)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
    }

    public void Analyze(StandardizeAlbumsAnalyzeInputData inputData)
    {
        _outputBoundary.PresentProgress("Reading album tags...", 0, 0);
        List<SongAlbumTags> songs = _dataAccess.ReadAlbumTags(inputData.Path);
        if (songs.Count == 0)
        {
            _outputBoundary.PresentError("No audio files with readable tags were found in that folder.");
            return;
        }

        OrderedDictionary<string, AlbumRaw> raws = AlbumStandardizer.CollectRaw(songs, inputData.Path);
        IDictionary<string, ParsedAlbum> parsed;
        bool usedAi = false;
        List<string> notes = new List<string>();

        if (raws.Count == 0)
        {
            parsed = new Dictionary<string, ParsedAlbum>(); // no song has an album or a folder to name one after
        }
        else if (inputData.IsUseAi)
        {
            string apiKey = inputData.ApiKey;
            if (apiKey == null || JavaString.Trim(apiKey).Length == 0)
            {
                apiKey = _dataAccess.ReadApiKeyFromEnvironment();
            }
            // a key is only needed for names Claude has not seen yet: answers it gave earlier are saved
            bool haveKey = apiKey != null && JavaString.Trim(apiKey).Length != 0;

            _outputBoundary.PresentProgress("Asking Claude to group " + raws.Count + " album names...", 0, 0);
            try
            {
                parsed = _dataAccess.ParseAlbumsWithAi(raws, haveKey ? JavaString.Trim(apiKey) : null, inputData.Model, notes,
                        message => _outputBoundary.PresentProgress(message, 0, 0));
                usedAi = true;
            }
            catch (ArtistAiException e)
            {
                _outputBoundary.PresentError(haveKey ? "Claude could not be used: " + e.Message
                        : "No Anthropic API key found. Set the ANTHROPIC_API_KEY environment variable, type a key "
                                + "into the window, or untick \"Use Claude\" for albums.");
                return;
            }
        }
        else
        {
            parsed = AlbumStandardizer.ParseLocally(raws.Keys);
        }

        // the names come from every song of the folder, only the songs in scope get their tags and folders changed
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs, parsed, usedAi, inputData.Path, inputData.AudioPaths);
        plan.Warnings.AddRange(notes);
        Present(plan);
    }

    public void Review(StandardizeAlbumsApplyInputData inputData)
    {
        Present(inputData.Plan);
    }

    private void Present(AlbumPlan plan)
    {
        _outputBoundary.PresentAlbumsAnalysis(new StandardizeAlbumsAnalysisOutputData(plan,
                AlbumStandardizer.ComputeUpdates(plan), AlbumStandardizer.ComputeFolderMoves(plan)));
    }

    public void Apply(StandardizeAlbumsApplyInputData inputData)
    {
        AlbumPlan plan = inputData.Plan;
        List<SongAlbumUpdate> updates = AlbumStandardizer.ComputeUpdates(plan);

        int updated = 0;
        List<string> failed = new List<string>();
        foreach (SongAlbumUpdate update in updates)
        {
            if (_dataAccess.WriteAlbumTag(update))
            {
                updated++;
            }
            else
            {
                failed.Add(update.AudioPath);
            }
            _outputBoundary.PresentProgress("Writing album tags...", updated + failed.Count, updates.Count);
        }

        _outputBoundary.PresentAlbumsApplied(
                new StandardizeAlbumsApplyOutputData(updated, plan.ScopedSongCount - updates.Count, failed));
    }

    public void MoveFolders(StandardizeAlbumsApplyInputData inputData)
    {
        AlbumPlan plan = inputData.Plan;
        List<AlbumFolderMove> moves = AlbumStandardizer.ComputeFolderMoves(plan);

        int foldersRenamed = 0;
        int songsMoved = 0;
        List<AlbumFolderMoveResult.Skipped> skipped = new List<AlbumFolderMoveResult.Skipped>();
        Dictionary<string, string> movedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        foreach (AlbumFolderMove move in moves)
        {
            AlbumFolderMoveResult result = _dataAccess.MoveToAlbumFolder(move);
            if (result.IsFolderRenamed)
            {
                foldersRenamed++;
            }
            songsMoved += result.Moved.Count;
            foreach (KeyValuePair<string, string> moved in result.Moved)
            {
                movedPaths[moved.Key] = moved.Value;
            }
            skipped.AddRange(result.NotMoved);
            _outputBoundary.PresentProgress("Moving songs into album folders...", ++done, moves.Count);
        }

        _outputBoundary.PresentAlbumsMoved(new StandardizeAlbumsMoveOutputData(foldersRenamed, songsMoved, skipped, movedPaths));
    }
}
