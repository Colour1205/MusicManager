# UI style: WinUI 3 / Fluent

The user's instruction: **"make sure the UI follows WinUI 3 style"**. The Java/Swing look (custom palette, hand-painted
cards, flat dark/indigo buttons, a fixed 560 px column) is NOT carried over. The app looks and behaves like a native
Windows 11 WinUI 3 app (compare the WinUI 3 Gallery and Settings): the same features and the same one-window flow
Home → Library → Review, in the Fluent look.

## Window and theme
* `MusicManagerFrame` (the window): Mica backdrop, title bar extended into the content with the app name and a **back button**
  (Library → Home, Review → Library); the pages live in a `Frame`.
* Follow the system light/dark theme (do not force `RequestedTheme`). **Only `ThemeResource` brushes and styles**, never a hex
  colour for UI chrome: page background `ApplicationPageBackgroundThemeBrush`, cards `CardBackgroundFillColorDefaultBrush` with
  `CardStrokeColorDefaultBrush`, text `TextFillColorPrimaryBrush` / `TextFillColorSecondaryBrush` / `TextFillColorTertiaryBrush`,
  accent `AccentFillColorDefaultBrush` / `AccentTextFillColorPrimaryBrush`, removal/error `SystemFillColorCriticalBrush`.
  **No green anywhere** (the user's rule): "good" states use the accent or secondary text.
* Corner radii from the theme: `ControlCornerRadius` (4) for controls, `OverlayCornerRadius` (8) for cards and dialogs.
* Typography: the type-ramp styles (`TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyStrongTextBlockStyle`,
  `BodyTextBlockStyle`, `CaptionTextBlockStyle`), never raw sizes. Spacing on a 4-px grid.
* Icons: `FontIcon` with Segoe Fluent Icons glyphs.

## Controls (the stock ones with their default styles; nothing painted by hand)
Accent `Button` for the primary action (Open, Process, Standardize), default `Button` for the others, `SelectorBar` for the
filter chips and the Review sections, `AutoSuggestBox` for search, `CheckBox`, `InfoBar` for results and errors,
`ProgressBar` / `ProgressRing` for progress, `ContentDialog` for the song window, Options and confirmations (`ToggleSwitch`,
`ComboBox`, `PasswordBox`, `Expander` inside), `ItemsView` with a `UniformGridLayout` for the song cards, `CommandBar` or a
`Grid` for the bottom bar. Scrolling is native (smooth, touchpad aware).

## Waiting (the user's rules for the animated "thinking" look)
* `AiStatusText` (a small spinning star and a word cycling Identifying / Classifying / Analyzing / Thinking, in a flowing
  blue-violet-pink gradient) is ONLY for a spot where something is really being analyzed or asked of Claude: the song window's
  preview and the Preview page while it analyzes. **Do not spam it**: at most one on screen at a time.
* `SkeletonLines` (slightly rounded "redacted" bars with the same flowing gradient, no blur) stand for content that is coming,
  next to an `AiStatusText` or alone (the one short bar under a value that is still loading).
* A spot that just loads or works (reading a folder, checking the library, writing the changes) gets a plain `ProgressBar`
  and **no text**. An `AiStatusText` never has other text beside it (no headline, no counts, no caption) and is never put
  inside an `InfoBar`. Results and errors keep their texts.
