# Context — conventions

Every factory agent reads this file before working on Context. Keep it short and accurate.

- **What it is:** a personal console app for keeping information in one place. The user stores
  notes as Markdown files in a `Context` folder inside their app data folder
  (`Environment.SpecialFolder.ApplicationData`). Later it will pull in information from email,
  the day's appointments from the user's calendar, and other sources.
- **Tags:** the user places tags in the Markdown files, and the tags link related files together.
  The links between files are stored in a database that lives in the same `Context` app data
  folder. The Markdown files stay the source of the content.
- **UI:**
  - An input box at the bottom of the screen is used for navigation.
  - A todo list is always shown in a sidebar, sorted by due date. The user sets each todo's due date.
  - The `/current` view shows all relevant information for now, such as today's appointments, due
    todos and linked notes.
- **How it runs:** an interactive session that stays open until `/quit` (exit code 0). A Spectre
  `Layout` fills the terminal: the main view on the left, the `Todos` sidebar on the right (fixed
  32 columns), one hint row, then a 3-line bordered input panel at the bottom. After each command
  the screen is cleared and the whole layout redrawn. Input is read by our own `LineEditor`
  (`console.Input.ReadKey(intercept: true)` + `Cursor.SetPosition`); no `Live` display.
  - Commands start with `/`. Each feature registers its commands (name, description, handler) in the
    single `CommandRegistry` (lookup ignores case), and `/help` lists whatever is registered.
  - **Command popup:** typing `/` into an empty input opens a popup that lists
    `CommandRegistry.Match(line)` (Levenshtein-ranked, so typos still match): name in `steelblue1`,
    description in `grey`, one row each, escaped. It sits just above the input panel, ends on the hint
    row and grows upwards, full terminal width, at most 8 rows (`… N more` on the last one) and never
    over row 1. `LineEditor` redraws it in place after every key (`Cursor.SetPosition`, no clear and no
    layout redraw); rows it stops covering are restored from the last frame, which `App.Draw` keeps
    as `Segment.SplitLines` (`App.RestoreRow`). Enter, Esc, or a line that no longer starts with `/`
    closes it. Esc means two things: with the popup open it closes it and keeps the text (it stays
    closed until the line stops starting with `/`); with no popup it clears the line.
  - Unknown commands and other errors show a short `[red]` hint on the row just above the input
    panel, never a stack trace, and never end the loop. The next command clears the hint.
  - **Notes:** `NoteStore` keeps each note as a top-level `<Title>.md` file in its root folder
    (`NoteStore.DefaultFolder()` = `<ApplicationData>/Context`; the folder is created on the first
    `/new`). A new note's content is `# <Title>\n` (UTF-8, no BOM), written with `FileMode.CreateNew`.
    Titles are validated (`NoteStore.ValidateTitle`: no `/ \ :`, invalid file-name or control chars,
    no leading `.`, at most 100 chars) after `NoteStore.NormalizeTitle` trims them and strips any trailing
    `.md` (ignoring case), so `/new x.md` creates `x.md`; names in `/open` are only matched against `List()` (as typed first, then with
    one `.md` stripped), never turned into a path. Note text is shown as plain `Text`, never parsed as markup.
  - Tests drive the loop by queuing input on `TestConsole.Input` (`PushTextWithEnter`, `PushKey`)
    and always end with `/quit`, so they can't hang.
- **Stack:** .NET 10 (`net10.0`), C# with nullable reference types and implicit usings on.
  `Spectre.Console` 0.57.2 handles all terminal output. Tests: xUnit + `Spectre.Console.Testing` 0.57.2.
- **Build:** `dotnet build` (must stay at 0 warnings)
- **Test command:** `dotnet test`
- **Run:** `dotnet run --project src/Context`
- **Layout:**
  - `Context.slnx`: the solution. `Directory.Build.props` turns on `EnforceCodeStyleInBuild`.
  - `src/Context/`: the app. `Program.cs` only wires things up (builds the registry with
    `CreateDefault()` + `NoteCommands.Register`, passes it and `AnsiConsole.Console` to `App`, returns
    `App.Run()`); the loop, layout and dispatch live in `App.cs`, commands in `CommandRegistry.cs`,
    input in `LineEditor.cs`, the command popup's rows and `PopupArea` in `CommandPopup.cs`, the note
    files in `NoteStore.cs`, and `/new`, `/notes`, `/open` in `NoteCommands.cs`.
  - `tests/Context.Tests/`: one test class per production class (`AppTests.cs` tests `App.cs`), plus
    shared helpers such as `AnsiWrites.cs` (splits ANSI output on cursor moves). Note tests use their
    own temp folder (deleted in `Dispose`), never the real app data folder.
- **Style:**
  - Write all output through an injected `IAnsiConsole`, never `Console.*` or the static
    `AnsiConsole.*` methods (except in `Program.cs`). Tests use `TestConsole` and assert on `.Output`.
  - Wrap any user or external text in `Markup.Escape` before it goes into markup.
  - Use Spectre widgets (`Table`, `Panel`, `Rule`, `Layout`) instead of hand-drawn ASCII.
    Colours: `SteelBlue1` accents, `grey` secondary text, `red` errors.
  - File-scoped namespaces, `internal sealed` unless a type must be public.
  - No primary constructors anywhere (classes, structs or positional records). Write a
    conventional constructor that assigns to fields or properties.
  - Private fields are camelCase **without** a leading underscore (`console`, not `_console`).
    `this.` only where a field and a constructor parameter share a name.
  - No `var`: always write the explicit type. Braces on every `if`/`else`/`for`/`foreach`/`while`
    body, even single lines.
  - `.editorconfig` holds these rules. The build enforces it (`EnforceCodeStyleInBuild`), so a
    style break is a build warning, and builds must stay at 0 warnings.
  - `Run` returns a process exit code: 0 for success, non-zero for errors.
  - No new NuGet packages without a reason in the spec. The approver escalates any new dependency.
- **Git:** the main branch is `main`. Factory work happens on `factory/<job-id>` branches and is merged with `--no-ff`.
  `.gitignore` is the standard `dotnet new gitignore`.
