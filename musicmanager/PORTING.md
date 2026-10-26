# Porting guide: Music Manager, Java/Swing to C#/WinUI 3

> **Status: the port is finished.** The Java/Swing app this was ported from is gone from the tree (a copy of its last state is kept outside the repository); this file is the record of the rules the port followed. Paths below that say `musicmanager-winui` are now `musicmanager`, and the Java file names are kept for every C# file that came from one.


The Java app in `..\musicmanager` is the **source of truth** (behavior, names, tests). This folder is the C# port:
.NET 10, WinUI 3 on the newest stable Windows App SDK (2.5.1), the same clean architecture and the same file layout.
Many agents port the layers **at the same time**, so everything below is a rule that makes the names predictable.

## 1. Layout (Java package 鈫?C# project / folder / namespace)

| Java (`musicmanager\src\main\java\com\musicmanager\`) | C# project (`src\`) | namespace |
|---|---|---|
| `entity\*.java` | `MusicManager.Entity\*.cs` | `MusicManager.Entity` |
| `use_case\<Pkg>\*.java` | `MusicManager.UseCase\<Pkg>\*.cs` | `MusicManager.UseCase.<Pkg>` |
| `interface_adapter\*.java`, `interface_adapter\<Pkg>\*.java` | `MusicManager.InterfaceAdapter\[<Pkg>\]*.cs` | `MusicManager.InterfaceAdapter[.<Pkg>]` |
| `data_access\*.java` | `MusicManager.DataAccess\*.cs` | `MusicManager.DataAccess` |
| `view\Library\*.java` that is **not** tied to Swing (models, filters, flows) | `MusicManager.View\Library\*.cs` | `MusicManager.View.Library` |
| `view\*.java`, `view\Library\*.java`, `view\ConvertLRC\*.java` that **is** Swing (components, dialogs) | `MusicManager.App\View\...\*.xaml(.cs)` (WinUI 3) | `MusicManager.App.View[.Library]` |
| `app\AppFeatures.java` | `MusicManager.View\AppFeatures.cs` | `MusicManager.App` |
| `app\Main.java` (`buildContext`) | `MusicManager.App\CompositionRoot.cs` | `MusicManager.App` |
| (Java-vs-.NET helpers, new) | `MusicManager.Common` | `MusicManager.Common` |
| tests `src\test\java\com\musicmanager\<pkg>\XTest.java` | `tests\MusicManager.Tests\<Pkg>\XTest.cs` (PascalCase folders) | `MusicManager.Tests.<Pkg>` |

Pascal-case the package folders: `data_access` 鈫?`DataAccess`, `use_case` 鈫?`UseCase`, `interface_adapter` 鈫?`InterfaceAdapter`.
Test folders: `tests\MusicManager.Tests\DataAccess\`, `...\UseCase\StandardizeLyrics\`, `...\InterfaceAdapter\Library\`, `...\View\Library\`.

**Dependencies (project references; never go against the arrows):**
`Common` 鈫?`Entity` 鈫?`UseCase` 鈫?{ `InterfaceAdapter`, `DataAccess` } 鈫?`View` 鈫?`App` (App also references all others;
`DataAccess` is only used by the composition root). A use case never knows an adapter, a presenter or a view.
The files and folders already in the solution (csproj, `Directory.Build.props`, `Common`, `InterfaceAdapter\ViewModel.cs`,
`tools\slice.ps1`) are done: do not change them (ask in your report if you need something there).

## 2. Naming rules (so that code written in parallel fits together)

* **Types**: the Java class name unchanged (`StandardizeLyricsInteractor`). Nested Java types stay nested (`Lyric.SyncType`).
* **Interfaces**: `I` + Java name; a Java name ending in `Interface` loses that suffix:
  `StandardizeLyricsDataAccessInterface` 鈫?`IStandardizeLyricsDataAccess`, `OpenLibraryInputBoundary` 鈫?`IOpenLibraryInputBoundary`,
  `OpenLibraryOutputBoundary` 鈫?`IOpenLibraryOutputBoundary`, `AppSettingsDataAccessInterface` 鈫?`IAppSettingsDataAccess`.
  Nested interfaces keep nesting (`ProcessFlow.Host` 鈫?`ProcessFlow.IHost`).
* **Functional interfaces become delegates** (lambdas must keep working): `RomajiProgressListener` 鈫?  `public delegate void RomajiProgressListener(int requestsDone, int requestsTotal, int linesDone, int linesTotal)` (in
  `MusicManager.UseCase.StandardizeLyrics`); `ClaudeArtistParser.ProgressListener` and `ClaudeRomanizer.Pause` 鈫?nested delegates;
  `SongDialog.PreviewSource` 鈫?a delegate. `java.util.function`: `Consumer<T>`鈫抈Action<T>`, `BiConsumer`鈫抈Action<,>`,
  `Function<A,B>`鈫抈Func<A,B>`, `Supplier<T>`鈫抈Func<T>`, `Predicate<T>`鈫抈Func<T,bool>`, `Runnable`鈫抈Action`.
* **Members**: `getFoo()` 鈫?property `Foo`; `isFoo()` 鈫?property `IsFoo`; `setFoo(v)` 鈫?the property's setter; every other method
  PascalCase (`execute` 鈫?`Execute`, `presentProgress` 鈫?`PresentProgress`). Constructor parameter order is unchanged.
  A Java getter that returns a *mutable list the callers add to* returns `List<T>` (not `IReadOnlyList<T>`).
  Public constants `static final` 鈫?`public const` / `public static readonly` with the SAME name in PascalCase
  (`DEFAULT_MODEL` 鈫?`DefaultModel`). Private fields: `_camelCase`.
* **Enums**: PascalCase members (`CONVERTED_FROM_TTML` 鈫?`ConvertedFromTtml`, `Category.NEEDS_WORK` 鈫?`NeedsWork`).
  `values()` 鈫?`Enum.GetValues<T>()`, `valueOf` 鈫?`Enum.Parse`. Where Java prints/compares `name()`, use a helper that gives the
  Java constant name back (UPPER_SNAKE) so the text stays identical (e.g. persisted values).
* **Generic view model**: `ViewModel<T>` is already ported (`MusicManager.InterfaceAdapter.ViewModel<T>`: `ViewName`, `State`,
  `FirePropertyChange()`, `FirePropertyChange(name)`, `AddPropertyChangeListener(PropertyChangeListener)`;
  `PropertyChangeListener` is a delegate taking a `PropertyChangeEvent` with `PropertyName`, `NewValue`).
* **Data holders**: keep them classes with read-only properties set in the constructor (mirror the Java; do not turn them into
  records unless the Java class had `equals`/`hashCode`). No `required`/`init` tricks that change how tests construct them.

## 3. Behavior differences Java 鈫?.NET that change results (read before you port string/regex/collection code)

* Strings are UTF-16 in both, so `char`, `length()`, `charAt`, surrogate handling carry over. But:
  `String.trim()` 鈮?`string.Trim()` 鈫?use `JavaString.Trim`; `strip()` 鈮?`Trim()`; `isBlank` 鈫?`JavaString.IsBlank`;
  `substring(begin, end)` takes an END index 鈫?`JavaString.Substring`; `split(regex)` drops trailing empties 鈫?`JavaString.Split`;
  `toLowerCase(Locale.ROOT)` 鈫?`ToLowerInvariant()`; `equalsIgnoreCase` 鈫?`string.Equals(a, b, StringComparison.OrdinalIgnoreCase)`;
  `compareTo` is ordinal 鈫?`string.CompareOrdinal`; `String.format("%.1f")` 鈫?use `CultureInfo.InvariantCulture` always;
  `Integer.parseInt` 鈫?`int.Parse(s, CultureInfo.InvariantCulture)` (and mind that Java throws `NumberFormatException` where
  `int.TryParse` returns false). Never let the current culture leak into parsing or formatting: pass `CultureInfo.InvariantCulture`.
* Regex: .NET `Regex` is close to `java.util.regex` but not equal. Check: `Pattern.CASE_INSENSITIVE | UNICODE_CASE` 鈫?  `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`; `Matcher.find()` loops 鈫?`Regex.Match/NextMatch`; `matches()` (whole
  string) 鈫?anchor with `^(?:...)$` or compare `Match.Length`; `lookingAt()` 鈫?`\G`; possessive quantifiers/`\p{IsHangul}`-style
  Java script classes do not exist: write the explicit code-point range (`臧€-頌, kana `銇€-銉縛, ...); `\p{L}`,
  `\p{M}`, `\p{Lu}` work. `Matcher.replaceAll("$1")` 鈫?`Regex.Replace(.., "$1")`; a literal `$` in a replacement needs `$$`.
  `Pattern.quote` 鈫?`Regex.Escape`. `\R`, `\h`, `\X` do not exist in .NET.
* `java.text.Normalizer` 鈫?`string.Normalize(NormalizationForm.FormD/FormC)`.
* `Character.isLetter/isDigit/isWhitespace/isUpperCase` 鈫?`char.IsLetter` ... (same for BMP; use the `(string, index)` overloads for
  surrogate pairs). `Character.UnicodeScript` has no equivalent: use explicit ranges as the Java code does or as above.
* Collections: `HashMap` 鈫?`Dictionary` (iteration order is NOT part of the contract in either); **`LinkedHashMap` 鈫?  `OrderedDictionary<K,V>`** (System.Collections.Generic, .NET 9+) so insertion order stays; `LinkedHashSet` 鈫?an
  `OrderedDictionary<T,bool>` or a `List<T>` + `HashSet<T>`; `TreeMap` 鈫?`SortedDictionary` (comparer: ordinal unless the Java passed one);
  `Set.copyOf/List.copyOf` 鈫?`new HashSet<T>(x)` / `new List<T>(x)`; `Collections.unmodifiableX` 鈫?`AsReadOnly()` / a copy.
  `list.remove(int)` vs `list.remove(Object)` 鈫?`RemoveAt` vs `Remove`. `List.sort(null)` of strings is ordinal.
  `Map.merge`, `computeIfAbsent`, `putIfAbsent` 鈫?`TryAdd` / `TryGetValue` patterns. Java streams 鈫?LINQ (`Collectors.toList()` 鈫?`ToList()`).
* Integers: Java `int` overflow wraps silently like C# `unchecked` (default); `/` and `%` on negatives behave the same; `Math.round(x)`
  rounds half UP (`Math.Floor(x + 0.5)`), C# `Math.Round` is banker's 鈫?write `(long)Math.Floor(x + 0.5)`. `(int)` casts of doubles saturate in
  C# only since .NET 9 for out-of-range; keep ranges in mind. `long` ms math is identical.
* Exceptions: `RuntimeException` 鈫?a class deriving from `Exception`; `IOException` 鈫?`IOException`; checked exceptions do not exist,
  so keep the same exception TYPES the callers catch (`LyricParseException`, `ArtistAiException`, ...).
* Files: `java.io.File`/`Path` 鈫?strings + `System.IO.Path/File/Directory`. `Files.walk`/`listFiles` order is unspecified in Java:
  where the Java sorted the result, sort the same way (ordinal); where it did not and a test depends on it, sort ordinally.
  Text is UTF-8 **without BOM** when writing (`new UTF8Encoding(false)`); when reading, strip a BOM like the Java did.
  Line endings: copy exactly what the Java wrote (`\r\n` vs `\n`).
* Time: `System.currentTimeMillis()` 鈫?`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`; `Thread.sleep` 鈫?`Thread.Sleep`.
* Threads: `synchronized` 鈫?`lock`; `ExecutorService/CompletableFuture/SwingWorker` 鈫?`Task`/dedicated threads; UI-thread hops
  (`SwingUtilities.invokeLater`) go through an injected `Action<Action>` dispatcher (never touch WinUI types in the non-App projects).
* `user.home`: use `MusicManager.Common.UserHome.Path` instead of `System.getProperty("user.home")` (tests override it).
* Equality: Java `==` on strings/objects is reference equality, `.equals` value equality; in C# `==` on strings is value equality. Where the
  Java relied on `==` identity (rare, e.g. `assertSame`) use `ReferenceEquals` / `Assert.Same`.
* `Object.hashCode/equals/toString` that the Java classes define: port them as `GetHashCode/Equals/ToString` with the same text.

## 4. Libraries

| Java | C# |
|---|---|
| jaudiotagger (tags, cover art, FLAC/MP3) | `TagLibSharp` (`TagLib.File.Create`); multi-valued fields: Vorbis comments via `TagLib.Ogg.XiphComment`, ID3v2 frames via `TagLib.Id3v2.Tag`. Keep reading/writing the same fields (ARTIST, ALBUMARTIST, ARTISTS, TITLE, ALBUM, cover art) |
| Gson | `System.Text.Json` (`JsonNode`/`JsonDocument` for tree access). **The JSON files must stay byte-compatible with what the Java wrote/reads** (`~\.musicmanager\settings.json`, `romaji-cache.json`, `artist-cache.json`): same property names (the Java field names), nulls omitted (Gson default), same value shapes; read the Java class + its tests for the exact format |
| `java.net.http.HttpClient`, `com.sun.net.httpserver` (in tests) | `System.Net.Http.HttpClient` (inject a transport as the Java does); tests use `HttpListener` or a fake `HttpMessageHandler` |
| `javax.xml` / DOM / SAX | `System.Xml.Linq` / `XmlReader` (the TTML parser repairs malformed XML itself: port that logic, do not rely on the XML library's recovery) |
| `ImageIO`, `BufferedImage`, AWT | `System.Drawing.Common` (decode, scale, PNG encode) inside DataAccess; WinUI `BitmapImage` in App |
| registry via `reg query` process | `Microsoft.Win32.Registry` (current user, `Environment` key) |
| Recycle Bin | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)` |
| JUnit 5 | xUnit 2.9 (see 搂5) |
| Swing | WinUI 3 (see 搂6) |

## 5. Tests = the oracle (port every Java test)

* Every test method of the Java test classes you own is ported 1:1 (same inputs, same assertions, same expected strings). Keep the
  class name and the method names (method names PascalCase: `swingsOwnWheelHandlerIsSwitchedOff` 鈫?`SwingsOwnWheelHandlerIsSwitchedOff`).
* Mapping: `@Test` 鈫?`[Fact]`; `@ParameterizedTest` with `@ValueSource/@CsvSource/@MethodSource` 鈫?`[Theory]` with
  `[InlineData]`/`[MemberData]`; `@BeforeEach` 鈫?constructor; `@AfterEach` 鈫?`IDisposable.Dispose`; `@TempDir Path` 鈫?create
  `Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid())` in the constructor and delete it in `Dispose`;
  `assertEquals(expected, actual)` 鈫?`Assert.Equal(expected, actual)` (argument ORDER is the same: expected first);
  `assertEquals(double, double, delta)` 鈫?`Assert.Equal(expected, actual, precision)`; `assertTrue/False(c, msg)` 鈫?`Assert.True(c, msg)`;
  `assertNull/NotNull/Same` 鈫?`Assert.Null/NotNull/Same`; `assertThrows(X.class, () -> ...)` 鈫?`Assert.Throws<X>(() => ...)`
  (xUnit's `Throws` needs the exact type; use `ThrowsAny<X>` where the Java accepted subclasses); `assertAll` 鈫?sequential asserts;
  `assertEquals(List, List)` 鈫?`Assert.Equal(expectedList, actualList)` (element-wise); `fail(msg)` 鈫?`Assert.Fail(msg)`; `assumeTrue`
  鈫?`Assert.SkipUnless`-style early return is NOT allowed: skip only with a documented reason.
* Anonymous inner classes / lambdas that fake a Java interface become small private classes or lambdas over the delegate.
* A ported test that fails is a signal about **your port**: fix the C# code. Only change a test when it encodes a Java-specific detail
  (default sort order, regex flavor, hash order); say so in your report with the reason.
* Tests that need a real audio file build it the same way the Java test did (byte arrays written to a temp file).
* Tests that set `UserHome.Path` must restore it (`finally` / `Dispose`). xUnit runs the tests of this solution **sequentially**.
* Report counts: `Java tests (methods incl. parameter rows) 鈫?C# tests (Facts + Theory rows)`, and how many pass.

## 6. WinUI 3 (UI workers; the UI is smoke-tested only, the logic is what must be right)

* Project `MusicManager.App` (WinUI 3, Windows App SDK 2.5.1, unpackaged, self-contained, `win-x64`). Build with
  `dotnet build src\MusicManager.App\MusicManager.App.csproj -p:Platform=x64`. XAML pitfalls seen: never give an element an
  `x:Name` that equals a member of its base class (`Title` on a `Window`); `ContentDialog.XamlRoot` must be set; a `FolderPicker`
  needs `WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window))`.
* Scrolling is native (smooth, touchpad aware): **do not port `SmoothScrolling`**. No `SongGridCanvas`/`TextFit`/`WrappedText`/
  `GridGeometry`/`SongCardPainter` custom painting: use `ItemsRepeater`/`GridView` with a `DataTemplate`, wrapping `TextBlock`s.
* **Look (UPDATED, overrides anything older): the UI must follow the WinUI 3 / Fluent design style, NOT the old Swing look.** Read
  `src\MusicManager.App\STYLE.md` (the Fluent style spec for this app) before writing any XAML. In short: Mica backdrop, system
  light/dark theme through `ThemeResource` brushes only (no hard-coded hex palette, no `view\Theme.java` colors), the stock WinUI
  controls and their default styles (`Button` / `AccentButtonStyle`, `ToggleButton`, `SelectorBar`, `AutoSuggestBox`, `InfoBar`,
  `ProgressBar`, `CheckBox`/`ToggleSwitch`, `ComboBox`, `ContentDialog`, `ItemsView`/`GridView`, `CommandBar`), the type ramp
  (`TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyStrongTextBlockStyle`, `CaptionTextBlockStyle`), Segoe Fluent Icons
  glyphs, theme corner radii, 4-px spacing grid, `Frame` navigation with a back button in the title bar. No green. Only the
  feature set and the one-window flow Home 鈫?Library 鈫?Review stay as in the Java app.
* Anything on the UI thread goes through `DispatcherQueue`; the non-App projects only see an `Action<Action>` dispatcher.

## 7. How a worker works

1. Read the Java files you own (and the tests) completely before writing. Port file by file, keeping the structure.
2. Build/test only your slice, in isolation, with `tools\slice.ps1` (it compiles just the folders you name, straight from the repo,
   into your own scratch folder; see the header of the script). Typical:
   `powershell -NoProfile -File tools\slice.ps1 -Name artists -Sources "src/MusicManager.Common,src/MusicManager.Entity,src/MusicManager.UseCase/StandardizeArtists" -Tests "tests/MusicManager.Tests/UseCase/StandardizeArtists"`
3. Other layers are being ported at the same moment. If your slice needs a type from another layer that does not exist yet (or is
   named differently from the rule-derived name), wait a little and re-read that file (poll every ~60-90 s, up to ~40 min), then adapt
   YOUR call sites. **Never edit files you do not own.** Do not run `dotnet build` on the whole solution.
4. Do not run the Java build or touch the Java tree. Never write into the user's music folders; the sample libraries for experiments
   are copies under the scratch folder.
5. Your report: files ported, test counts (Java 鈫?C#, passing), every deliberate deviation from the Java behavior and why, and
   anything another layer must know. Keep it short.
