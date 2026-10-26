namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>told after every request how far a romanization run has got</summary>
public delegate void RomajiProgressListener(int requestsDone, int requestsTotal, int linesDone, int linesTotal);
