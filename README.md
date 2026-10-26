# MusicManager
App for managing local music folder, great to use to convert lyric file into different format and batch editing.

- change translation style
- **Standardize** a whole library: one word-synced lyric file per song, romaji, and consistent artist names

## One window

Pick a music folder on the home screen and press **Open**. The same window then shows your **songs** as cards (cover,
title, artist and what Process would do to that song's lyrics). From there you can:

- press **Process all** to do the whole folder, or tick some songs (or **Select all**) and press **Process N selected**
  (with a filter or search on, Process all means the songs that are shown);
- click a song to see a **preview of what Process would change for it** in a small window (the lyric files written and
  deleted, the actual lines that get romaji with any old Chinese-character romaji struck out above the new Latin one,
  and the tag changes), and press **Process this song** for just that one. Nothing is written by the preview. When the
  romaji is not saved yet it is asked from Claude for that song's lines when the window opens (and saved, so Process
  does not ask again); the artist names are worked out from the whole folder, which the first time can take a while;
- filter by what needs work (*Needs work*, *Standard*, *Line-synced*, *No lyrics*) or search by title, artist, album or
  file name;
- open **Options** to choose what Process does.

Process shows a **Preview** page first (what would change, song by song). Nothing is written until you press
**Apply** on the Preview page. The songs are checked again afterwards, so the cards show the result.

The old "Convert LRC" screen (convert `.ttml`/`.hmrc`/`.lrc` into each other) is no longer in the window, but its code is
kept: set `LegacyConvertLrc` to `true` in `AppFeatures.cs` to bring the "Convert formats" mode back into Options.

## Standardize

### Lyrics
- Every song ends up with exactly **one word-synced `.lrc`** next to its audio file (Enhanced LRC as written by LDDC:
  `[mm:ss.SSS]<mm:ss.SSS>word <mm:ss.SSS>word<end>`, translations on the lines below).
- It is built from the song's `.ttml` (or, if there is none, its `.hmrc`), or an existing word-synced `.lrc` is rewritten
  in the standard syntax. A file that is already standard is left alone.
- Each new file is read back and compared with its source (words and timing) **before** anything is replaced. A song
  that fails the comparison is left untouched.
- The deprecated `.ttml` and `.hmrc` files are then deleted (or sent to the Recycle Bin, if you tick that).
- Songs that only have a line-synced `.lrc` (including LDDC's line-level output) keep it as it is. Lyrics embedded in
  the audio files are not touched.

### Romaji (Korean and Japanese)
- Every Korean or Japanese line gets a **Latin-letter romaji line above it** (the style LDDC uses), made by Claude:
  word-synced in a word-synced `.lrc`, line-synced in a line-synced one.
- **Romaji is only for Korean and Japanese.** Chinese characters inside a Korean line stay Chinese characters in its
  romaji line (`Know My Name (以我之名) ... i-dle (아이들)` becomes `Know My Name (以我之名) ... i-dle (aideul)`), never
  pinyin. A line with any kana is Japanese, and its kanji are romanized as Japanese words.
- It works on **any `.lrc`**, with or without word timing, so newer songs that never had a `.ttml` or `.hmrc` still get
  romaji and still get their artists standardized.
- **Older, non-standard romaji is corrected.** A transliteration written in Chinese characters (what LDDC's "romaji"
  option makes) is replaced by Latin letters, and so is romaji that turned a Korean line's Chinese characters into
  pinyin; romaji that is already right is kept. The song window, Options and the Preview all say how many lines are
  *added* and how many old Chinese-character lines are *replaced*, so the two numbers never overlap.
- Lines are sent in batches across the whole library, a repeated chorus is asked once, and every answer is saved in
  `~/.musicmanager/romaji-cache.json`. The preview shows how many lines and requests it will take before anything is
  sent. If a run stops (rate limit, no network) what was done is kept: run it again and it continues.
- If Claude cannot be used at all (bad key, unknown model) the run stops **before changing anything**.

### Artists
- All artist tag values (ARTIST, ALBUMARTIST, ARTISTS) are read and grouped by Claude: *aespa*, *Aespa*,
  *에스파 (aespa)* become one artist with one official name.
- Korean, Japanese and Western artists get their official Latin-script name. **Chinese-language artists keep their
  Chinese name** (for example `G.E.M. 邓紫棋`, `周杰伦`).
- Names that are uncertain or involve Chinese, Korean or Japanese text are checked on the web by Claude (web search).
- Several artists are joined with one separator (default `; `), the same spelling for the same artist on every song;
  the individual names are also written to the multi-valued ARTISTS tag.
- In the review, each artist is a drop-down of every spelling found plus what Claude proposed: pick one or type your own.
  Names Claude is unsure about are marked in red.
- When only some songs are processed, the artist names are still worked out from the whole folder (so one artist gets
  one spelling everywhere), but only the tags of the selected songs are changed.
- What Claude decided is saved in `~/.musicmanager/artist-cache.json`, so a library whose artists were already grouped
  needs no API call (and no key) the next time.

### Claude and the API key
Artist grouping and romaji use the Anthropic API. The key is read from the `ANTHROPIC_API_KEY` environment variable
(including a value you set in Windows after the program or your terminal started, which Windows would otherwise not
pass on); you can also type a key into the window, which is kept for that window only and never saved. Your other
Options (what to do, the separator, the model) are remembered in `~/.musicmanager/settings.json`.

## The code: C# and WinUI 3

The app is written in C# (.NET 10) with **WinUI 3** (Windows App SDK 2.5) and follows the same clean architecture as the
Java/Swing version it was ported from. Every layer is its own project, and the files keep the names of the Java files they
came from (`StandardizeLyricsInteractor.cs`, `LrcParser.cs`, `LibraryStatusMapper.cs`, ...; a Java `FooInterface` is the C#
interface `IFoo` in `FooInterface.cs`):

| Project (`src/`) | Layer | Java package it replaces |
|---|---|---|
| `MusicManager.Entity` | entities | `entity` |
| `MusicManager.UseCase` | use cases and their boundaries (`StandardizeLyrics`, `StandardizeArtists`, `OpenLibrary`, `ConvertLRC`, `AppSettings`) | `use_case` |
| `MusicManager.InterfaceAdapter` | controllers, presenters, view models, status mapping | `interface_adapter` |
| `MusicManager.DataAccess` | the one `DataAccess` class, parsers, tags (TagLibSharp), Claude client, caches, settings | `data_access` |
| `MusicManager.View` | view logic that does not depend on WinUI (filters, options model, process flow, preview loader) | `view` (logic) |
| `MusicManager.App` | the WinUI 3 windows and pages, composition root | `view` (Swing), `app` |
| `MusicManager.Common` | the few places where .NET differs from Java (`UserHome`, `JavaString`) | |

Dependencies only point inward (`App` → `View` → `InterfaceAdapter` → `UseCase` → `Entity`; `DataAccess` implements the use
cases' interfaces and is only known to the composition root). The UI is a native WinUI 3 / Fluent app (Mica, system light and
dark theme, stock controls); the style rules are in `src/MusicManager.App/STYLE.md`, the Java-to-C# porting rules in
`PORTING.md`. Smooth touchpad scrolling is built into WinUI.

The Swing-only pieces (custom painted song grid, smooth-scroll helper, text measuring, theme) have no C# file: WinUI's
virtualized lists, styles and scrolling replace them.

## Build

Needs the .NET 10 SDK (no Visual Studio required). From the `musicmanager` folder:
```
dotnet build MusicManager.sln -p:Platform=x64
dotnet test tests/MusicManager.Tests/MusicManager.Tests.csproj
```
runs the build and the unit tests (the Java tests were ported one to one; they run sequentially because some of them
point the user's home folder at a temporary folder). To make the single-file exe:
```
dotnet publish src/MusicManager.App/MusicManager.App.csproj -c Release -p:Platform=x64 -o publish
```
which writes `publish/MusicManager.exe` (about 68 MB, self-contained: no .NET or Windows App SDK install is needed; it
can be renamed, for example to `Music Manager.exe`). Pass a music folder as the first argument to open it straight away.
The Java version this was ported from is not in the repository any more; its Java file names live on in the C# files.
