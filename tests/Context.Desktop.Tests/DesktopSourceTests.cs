using System.Text.RegularExpressions;

namespace Context.Tests;

// Source and file scans of Context.Desktop: raw markup, risky elements, the page's CSP and the guarded write path.
public sealed class DesktopSourceTests
{
    private const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' blob:; font-src 'self'; "
        + "connect-src 'self'; frame-src 'self'; base-uri 'none'; form-action 'none'";

    // 263 AC-7: the Mermaid renderer frame's own CSP: no network, its one script from its own folder, and the inline
    // <style> that Mermaid's SVG needs.
    private const string FrameCsp =
        "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'";

    // The Core calls that change data (AC-8). CalendarLink's real names are Set/Clear; Save/Delete are listed too.
    // 134: NoteStore.Rename and NoteStore.Delete. 136: sealing (SealedNote.Seal, AddRecipient), NoteStore.DeleteSensitive
    // and the key store's two changing calls; RecoveryFile.Create is caught by Create. 140: Inbox.Capture.
    // 152: NoteStore.CreateChild and NoteStore.Move. 164: DailyNote.Ensure. 165: NoteStore.CreateWithText.
    // 172: Briefing.Ensure (caught by Ensure). 174: NoteHistory.Keep and RemoveHistory, and DesktopActions.RestoreVersion,
    // which components reach only through the injected Actions (only Actions.RestoreVersion( itself is let through).
    // 177: NoteAttachments.Add (caught by Add), MoveWithNote and RemoveUnlinked. 178: TodoStore.MarkNotified.
    // 179: InboxTriage.Snooze; InboxTriage.Remove is checked on its own (Sources_CallInboxTriageWritesOnlyInDesktopActions),
    // since components remove from their own lists. 201: NoteTodos.Complete (matched by Complete). 217: SettingsStore.SaveOutlook.
    // 286: SettingsStore.SaveMailDigest. 287: TodoStore.Tie.
    private static readonly Regex Writes = new(
        @"(?:(?<!\bActions)|(?!\.\s*RestoreVersion\s*\())\.\s*(Create|Save|SaveIfUnchanged|Add|Complete|Set|Clear|Delete|Rename|ClearCache"
        + @"|Seal|AddRecipient|DeleteSensitive|CreateMachineKey|StoreRecoveryPublicKey|Capture|CreateChild|Move|Ensure|CreateWithText"
        + @"|CloseDay|RefreshWorked|RefreshStandup|MoveTodos|AutoClose|MoveDue|SaveDayEnd|SaveSavedSearches|Keep|RemoveHistory|RestoreVersion|MoveWithNote|RemoveUnlinked|MarkNotified|Tie|Snooze|SaveOutlook|SaveMailDigest|SaveScreenshotFolder|RemoveScreenshotFolder)\s*\(",
        RegexOptions.CultureInvariant);

    // 136: the real key stores and the real file dialog, spelled in pieces: no test file may hold their names (see
    // Tests_NeverReferenceTheRealKeyStoresOrFileDialog).
    private const string RealWindowsStore = "Windows" + "KeyStore";
    private const string RealMacStore = "Mac" + "KeyStore";
    private const string RealFileDialog = "Photino" + "FileDialog";
    private const string RealWindowsStoreFile = "src/Context.Desktop/" + RealWindowsStore + ".cs";
    private const string RealMacStoreFile = "src/Context.Desktop/" + RealMacStore + ".cs";

    // 367: the real licence stores (Keychain, Credential Manager), spelled in pieces: no test file may hold their names.
    private const string RealMacProStore = "Mac" + "LicenceStore";
    private const string RealWindowsProStore = "Windows" + "LicenceStore";
    private const string RealMacProStoreFile = "src/Context.Desktop/" + RealMacProStore + ".cs";
    private const string RealWindowsProStoreFile = "src/Context.Desktop/" + RealWindowsProStore + ".cs";

    // 260: the real area picker (runs screencapture) and the real hotkey (Carbon), spelled in pieces: no test file may
    // hold their names.
    private const string RealScreenCapture = "Mac" + "ScreenCapture";
    private const string RealHotkey = "Mac" + "GlobalHotkey";
    private const string RealScreenCaptureFile = "src/Context.Desktop/" + RealScreenCapture + ".cs";
    private const string RealHotkeyFile = "src/Context.Desktop/" + RealHotkey + ".cs";

    // 282: the real Times runner (starts tm), named only by itself, Program.cs and its own test.
    private const string RealTimesRunner = "Times" + "ProcessRunner";
    // Job 381: the runner and its test moved to Core and Context.Tests, so the command line can book too.
    private const string RealTimesRunnerFile = "src/Context.Core/" + RealTimesRunner + ".cs";
    private const string RealTimesRunnerTestFile = "tests/Context.Tests/" + RealTimesRunner + "Tests.cs";
    private const string CliProgramFile = "src/Context/Program.cs";
    private const string CommandBarFolder = "src/Context.Desktop/wwwroot/commandbar/";

    // 136: SealedNote.cs makes P-256 keys in memory with .NET's ECDiffieHellman.Create; that writes no data.
    private const string SealedNoteFile = "src/Context.Desktop/SealedNote.cs";
    private const string KeyFactory = "ECDiffieHellman.Create(";

    private static readonly string[] RawMarkup = ["MarkupString", "AddMarkupContent", "@(("];

    private static readonly string[] RazorForbidden = ["style=", "<a ", "<form", "<script"];

    // The only Desktop file allowed to call the writing methods.
    private const string ActionsFile = "src/Context.Desktop/DesktopActions.cs";

    // 094: the only Desktop file that may use JavaScript interop (IJSRuntime, [JSInvokable]).
    private const string InteropFile = "src/Context.Desktop/EditorInterop.cs";

    // 094: DotNetObjectReference.Create only wraps EditorInterop for the page's callbacks; it writes no data.
    private const string InteropReference = "DotNetObjectReference.Create(";

    // 094: DOM and code-eval APIs the editor's own sources must not use.
    private static readonly string[] EditorForbidden =
        ["innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function"];

    // The .cs files that may name DesktopActions (besides DesktopActions.cs itself).
    private const string ProgramFile = "src/Context.Desktop/Program.cs";

    // 138: where the calendar link may be named, and the page that sets it.
    private const string StateFile = "src/Context.Desktop/DesktopState.cs";
    private const string SettingsFile = "src/Context.Desktop/SettingsPanel.razor";

    // The link's type as a whole word: CalendarLinkView, SetCalendarLink and CalendarLinkSetting don't count.
    private static readonly Regex LinkType = new(@"\bCalendarLink\b", RegexOptions.CultureInvariant);

    // A read of the link's text: the link's Get(), or the members of Uri that hold the path or the query.
    private static readonly Regex LinkText = new(
        @"\.\s*(Get\s*\(\s*\)|(AbsoluteUri|OriginalString|PathAndQuery|Query)\b)", RegexOptions.CultureInvariant);

    private static readonly Regex LinkRun = new(@"\bCalendarLinkView\s*\.\s*Run\b", RegexOptions.CultureInvariant);

    // A Core type that holds or reads the link, unlike the masked view, its kind and the feed's status.
    private static readonly Regex LinkCoreType = new(
        @"\b(CalendarLink|CalendarLinkException|CalendarFeed)\b", RegexOptions.CultureInvariant);

    private static readonly Regex Storage = new(
        @"\b(localStorage|sessionStorage|indexedDB|NavigationManager|NavigateTo)\b|\bdocument\s*\.\s*cookie\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Logging = new(
        @"\bILogger\b|\b(Console|Debug|Trace)\s*\.", RegexOptions.CultureInvariant);

    // Spelled in pieces: no test file may hold the real source's name (see Tests_NeverReferenceTheRealUpdateSource).
    private const string RealUpdateSource = "Velopack" + "UpdateSource";
    private const string RealUpdateSourceFile = "src/Context.Desktop/" + RealUpdateSource + ".cs";

    // 134: spelled in pieces too: no test file may hold the real launcher's name.
    // 217: the real Outlook COM source, spelled in pieces: no test file may hold its name.
    private const string RealOutlook = "Outlook" + "ComSource";
    private const string RealOutlookFile = "src/Context.Desktop/" + RealOutlook + ".cs";

    // 334: the Windows session end watcher (user32), spelled in pieces: no test may construct it or call into it.
    private const string RealSessionWatcher = "SessionEnd" + "Watcher";
    private const string RealSessionWatcherFile = "src/Context.Desktop/" + RealSessionWatcher + ".cs";

    private const string RealLauncher = "Shell" + "NoteLauncher";
    private const string RealLauncherFile = "src/Context.Desktop/" + RealLauncher + ".cs";

    // Job 354: the real URL launcher, named the same way so no test names it.
    private const string RealUrlLauncher = "Shell" + "UrlLauncher";
    private const string RealUrlLauncherFile = "src/Context.Desktop/" + RealUrlLauncher + ".cs";

    // 178: the real notifier, spelled in pieces too: no test file may hold its name (it would run osascript or PowerShell).
    private const string RealNotifier = "System" + "Notifier";
    private const string NotifierFolder = "src/Context.Desktop/";
    private const string RealNotifierFile = NotifierFolder + RealNotifier + ".cs";

    // 237: the crash log prunes its own old logs in <root>/.logs with File.Delete; it writes through no other method.
    private const string CrashLogFile = "src/Context.Desktop/CrashLog.cs";
    private const string CrashLogDelete = "File.Delete(";

    // 139: System.IO's file system classes as whole words (RecoveryFile. is none).
    private static readonly Regex FileSystemClass = new(@"\b(File|Directory|Path)\.", RegexOptions.CultureInvariant);

    private static readonly string[] UpdateFiles =
    [
        "src/Context.Desktop/IUpdateSource.cs", "src/Context.Desktop/StartupUpdate.cs", RealUpdateSourceFile,
        "src/Context.Desktop/UpdateOutcome.cs", "src/Context.Desktop/UpdateStatus.cs",
    ];

    private static readonly string[] ActionsUsers = ["src/Context.Desktop/Program.cs", "src/Context.Desktop/StateComponent.cs"];

    private static readonly string[] Lifecycle =
        ["OnInitialized", "OnParametersSet", "OnAfterRender", "SetParametersAsync", "BuildRenderTree"];

    private static readonly Regex ActionsName = new(@"\bDesktopActions\b", RegexOptions.CultureInvariant);

    private static readonly Regex ActionsCall = new(@"\bActions\s*\.", RegexOptions.CultureInvariant);

    // A tag with its attributes; quoted values may hold '>' (as in "() => Ask(n)").
    private static readonly Regex Tag = new(
        @"<([A-Za-z][\w.-]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.CultureInvariant);

    // An event binding: @on<event>= on an element, or an On<Event>= event-callback parameter on a component.
    private static readonly Regex EventBinding = new(
        @"(?:@on[a-z]+|\bOn[A-Z]\w*)\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);

    // A plain HTML event attribute: on<event>= not preceded by '@' (checked on HTML elements only).
    private static readonly Regex PlainEvent = new(
        @"(?<![@\w.:-])on[a-z]+\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ButtonType = new(@"(?<![\w-])type\s*=\s*""button""", RegexOptions.CultureInvariant);

    // A method header in a @code block: a name, a parameter list, then a body or an arrow.
    private static readonly Regex MethodHeader = new(@"\b([A-Za-z_]\w*)\s*\(([^()]*)\)\s*(\{|=>)", RegexOptions.CultureInvariant);

    private static readonly string[] Keywords =
        ["if", "for", "foreach", "while", "switch", "using", "lock", "catch", "return", "when", "nameof", "fixed"];

    // AC-10: no raw markup anywhere in the Desktop sources.
    [Fact]
    public void Sources_UseNoRawMarkup()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        Assert.Contains("src/Context.Desktop/Main.razor", files);

        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(RawMarkup.Where(m => text.Contains(m, StringComparison.Ordinal)).Select(m => $"{file}: {m}"));
        }

        Assert.True(found.Count == 0, "Raw markup in Context.Desktop: " + string.Join(", ", found));
    }

    // AC-10: no inline styles, links, forms or scripts in the components.
    [Fact]
    public void Razor_HasNoRiskyElements()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor");
        Assert.True(files.Count >= 7, "Expected the seven components, found " + files.Count);

        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(RazorForbidden
                .Where(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
                .Select(m => $"{file}: {m}"));
        }

        Assert.True(found.Count == 0, "Forbidden markup in .razor files: " + string.Join(", ", found));
    }

    // AC-6 / 094 AC-12: exactly the CSP meta tag, local external scripts, no
    // inline script or style.
    [Fact]
    public void IndexHtml_HasCspAndNoInlineCode()
    {
        string html = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "index.html"));

        MatchCollection metas = Regex.Matches(
            html, "<meta\\s+http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]*)\"\\s*/?>",
            RegexOptions.IgnoreCase);
        Match meta = Assert.Single(metas);
        Assert.Equal(Csp, meta.Groups[1].Value);
        Assert.Single(Regex.Matches(html, "Content-Security-Policy", RegexOptions.IgnoreCase));

        MatchCollection scripts = Regex.Matches(html, "<script\\b([^>]*)>(.*?)</script>", RegexOptions.IgnoreCase
            | RegexOptions.Singleline);
        Assert.Equal(
            [" src=\"journal.js\"", " src=\"editor/editor.js\"", " src=\"_framework/blazor.webview.js\""],
            scripts.Select(m => m.Groups[1].Value).ToArray());
        Assert.All(scripts, m => Assert.Equal(string.Empty, m.Groups[2].Value));
        Assert.Equal(3, Regex.Matches(html, "<script", RegexOptions.IgnoreCase).Count);
        Assert.Equal(
            ["app.css", "editor/editor.css"],
            Regex.Matches(html, "<link\\s+href=\"([^\"]*)\"\\s+rel=\"stylesheet\"").Select(m => m.Groups[1].Value).ToArray());
        Assert.Equal(2, Regex.Matches(html, "<link", RegexOptions.IgnoreCase).Count);

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("\\son[a-z]+\\s*=", html);
        Assert.DoesNotContain("http:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https:", html, StringComparison.OrdinalIgnoreCase);
    }

    // 263 AC-7: frame.html has exactly its CSP, one external script (mermaid-frame.js) and no inline code or URLs.
    [Fact]
    public void FrameHtml_HasCspAndOneScript()
    {
        string html = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "mermaid", "frame.html"));

        MatchCollection metas = Regex.Matches(
            html, "<meta\\s+http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]*)\"\\s*/?>",
            RegexOptions.IgnoreCase);
        Match meta = Assert.Single(metas);
        Assert.Equal(FrameCsp, meta.Groups[1].Value);
        Assert.Single(Regex.Matches(html, "Content-Security-Policy", RegexOptions.IgnoreCase));

        MatchCollection scripts = Regex.Matches(html, "<script\\b([^>]*)>(.*?)</script>", RegexOptions.IgnoreCase
            | RegexOptions.Singleline);
        Match script = Assert.Single(scripts);
        Assert.Equal(" src=\"mermaid-frame.js\"", script.Groups[1].Value);
        Assert.Equal(string.Empty, script.Groups[2].Value);
        Assert.Single(Regex.Matches(html, "<script", RegexOptions.IgnoreCase));

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("\\son[a-z]+\\s*=", html);
        Assert.DoesNotContain("http:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//", html, StringComparison.Ordinal);
    }

    // 263 AC-8: nothing grants the renderer frame the page's origin, and the editor sets no sandbox but "allow-scripts".
    [Fact]
    public void Sources_NeverAllowSameOrigin_AndSandboxOnlyAllowsScripts()
    {
        string root = RepoFiles.Root();
        string desktop = Path.Combine(root, RepoFiles.DesktopFolder);
        string editor = Path.Combine(desktop, "editor", "src");
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        files.AddRange(Directory.EnumerateFiles(editor, "*", SearchOption.AllDirectories).Select(f => RepoFiles.Relative(root, f)));
        files.AddRange(Directory.EnumerateFiles(Path.Combine(desktop, "wwwroot"), "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".js" or ".html" or ".css")
            .Select(f => RepoFiles.Relative(root, f)));
        Assert.Contains(RepoFiles.DesktopFolder + "/wwwroot/mermaid/frame.html", files);
        Assert.Contains(RepoFiles.DesktopFolder + "/wwwroot/mermaid/mermaid-frame.js", files);
        Assert.Contains(RepoFiles.DesktopFolder + "/editor/src/mermaid.js", files);

        List<string> found = files
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("allow-same-origin", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(found.Count == 0, "allow-same-origin in: " + string.Join(", ", found));

        // Every sandbox value in the sources: setAttribute("sandbox", "..."), .sandbox = "..." and sandbox="..." alike.
        string[] sandboxes = Directory.EnumerateFiles(editor, "*.js")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\bsandbox[""']?\s*[,=]\s*[""']([^""']*)[""']").Select(m => m.Groups[1].Value))
            .ToArray();
        Assert.NotEmpty(sandboxes);
        Assert.All(sandboxes, value => Assert.Equal("allow-scripts", value));
        Assert.DoesNotContain(".sandbox.add", string.Concat(Directory.EnumerateFiles(editor, "*.js").Select(File.ReadAllText)), StringComparison.Ordinal);
    }

    // 263 AC-8: the page's editor bundle holds no Mermaid library code; the frame bundle holds Mermaid in strict mode.
    [Fact]
    public void Bundles_KeepMermaidInTheFrameOnly()
    {
        string wwwroot = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot");
        string editor = File.ReadAllText(Path.Combine(wwwroot, "editor", "editor.js"));
        string frame = File.ReadAllText(Path.Combine(wwwroot, "mermaid", "mermaid-frame.js"));

        Assert.DoesNotContain("securityLevel", editor, StringComparison.Ordinal);
        Assert.Contains("mermaid/frame.html", editor, StringComparison.Ordinal);
        Assert.Contains("securityLevel:\"strict\"", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", frame, StringComparison.Ordinal);
    }

    // AC-6 / 094 AC-12: the stylesheets load nothing from outside (no CDN, no external fonts).
    [Theory]
    [InlineData("app.css")]
    [InlineData("editor/editor.css")]
    public void Css_LoadsNothingExternal(string file)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", file));

        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@font-face", css, StringComparison.OrdinalIgnoreCase);
    }

    // 094 review: the editing components are styled in app.css (layout, the palette's selected row, the entry row,
    // themed text and date fields, note names as plain text); editor.css styles only the editor body (and ProseMirror's own
    // classes, from prosemirror-view's stylesheet).
    [Theory]
    [InlineData(".palette-row.selected")]
    [InlineData(".palette-row {")]
    [InlineData(".todo-entry {")]
    [InlineData("input[type=\"text\"],")]
    [InlineData("input[type=\"date\"],")]
    [InlineData(".note .name,")]
    [InlineData(".tag-notes .note-button")]
    [InlineData(".editor-bar,")]
    [InlineData(".note-entry {")]
    [InlineData(".note-toolbar {")]
    [InlineData(".dirty-dot {")]
    public void AppCss_StylesEditingComponents(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains(selector, css, StringComparison.Ordinal);
    }

    // 192 AC-5: the capture bar is a band; the note time rule is gone.
    [Fact]
    public void AppCss_TopBarIsABand_NoNoteTimeRule()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        int at = css.IndexOf(".top-bar {", StringComparison.Ordinal);
        Assert.True(at >= 0);
        string block = css[at..css.IndexOf('}', at)];
        Assert.Contains("background: var(--panel)", block, StringComparison.Ordinal);
        Assert.Contains("border-bottom: 1px solid var(--line)", block, StringComparison.Ordinal);
        Assert.DoesNotContain(".note .time", css, StringComparison.Ordinal);
    }

    // 210 AC-7, AC-8: the outline is styled for the right sidebar (its own 40vh scroll, a --line border and margin
    // above the todos, only the theme's colour variables); no 30vh rule tied to the note area, no .lossy rule, and the
    // sidebar keeps its fixed column.
    [Fact]
    public void AppCss_OutlineInSidebar_NoLossyRule()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        string panel = RuleBody(css, ".todos-region .outline-panel");
        Assert.Contains("max-height: 40vh;", panel, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", panel, StringComparison.Ordinal);
        Assert.Contains("border-bottom: 1px solid var(--line);", panel, StringComparison.Ordinal);
        Assert.Matches(@"margin-bottom: \d+px;", panel);
        Assert.DoesNotContain(".lossy", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\.outline-panel\s*\{", css);
        Assert.Contains("grid-template-columns: 240px minmax(0, 1fr) 280px;", css, StringComparison.Ordinal);

        string[] outlineRules = System.Text.RegularExpressions.Regex.Matches(css.ReplaceLineEndings("\n"), @"(?m)^([^\n{}]*outline-(?:panel|item)[^\n{]*)\{([^}]*)\}")
            .Select(m => m.Groups[2].Value)
            .ToArray();
        Assert.NotEmpty(outlineRules);
        Assert.All(outlineRules, r => Assert.DoesNotContain("30vh", r, StringComparison.Ordinal));
        string[] colours = outlineRules
            .SelectMany(r => System.Text.RegularExpressions.Regex.Matches(r, @"(?m)^\s*(?:color|background|border[a-z-]*):\s*([^;]+);").Select(m => m.Groups[1].Value))
            .ToArray();
        Assert.All(colours, c => Assert.Matches(@"^(none|1px solid var\(--line\)|var\(--(accent-strong|line|text|grey)\))$", c.Trim()));
        Assert.Contains(colours, c => c.Contains("var(--accent-strong)", StringComparison.Ordinal));
    }

    // 236 AC-8: the note's attribute box is styled with only the theme's colour variables (no hex, rgb( or named
    // colour), so light and dark mode both apply.
    [Fact]
    public void AppCss_AttributeBox_ThemeVariablesOnly()
    {
        string css = AppCss();

        (string Selector, string Body)[] rules = CssRules(css).Where(r => r.Selector.Contains(".note-attributes", StringComparison.Ordinal)).ToArray();
        Assert.Contains(rules, r => r.Selector.Trim() == ".note-attributes");
        string[] colours = rules
            .SelectMany(r => System.Text.RegularExpressions.Regex.Matches(r.Body, @"(?m)^\s*(?:color|background(?:-color)?|border(?:-(?:top|bottom|left|right))?(?:-color)?|outline):\s*([^;]+);").Select(m => m.Groups[1].Value))
            .ToArray();
        Assert.NotEmpty(colours);
        Assert.All(colours, c => Assert.Matches(@"^(none|1px solid var\(--line\)|var\(--(accent-strong|line|text|grey|panel|red|background)\))$", c.Trim()));
        Assert.All(rules, r => Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", r.Body));
    }

    // 214 AC-10 (395: and the title field's row): the sidebar appointments, the appointment page and its create link are styled with only the theme's
    // variables: no hex, rgb( or named colour, so light and dark mode both work.
    [Theory]
    [InlineData("day-appointments")]
    [InlineData("appointment-item")]
    [InlineData("appointment-page")]
    [InlineData("create-note-link")]
    [InlineData("create-note-entry")]
    public void AppCss_AppointmentRules_UseOnlyThemeVariables(string name)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"))
            .ReplaceLineEndings("\n");

        string[] rules = Regex.Matches(css, @"(?m)^([^\n{}]*\." + Regex.Escape(name) + @"\b[^{]*)\{([^}]*)\}")
            .Select(m => m.Groups[2].Value)
            .ToArray();
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", r));
        string[] colours = rules
            .SelectMany(r => Regex.Matches(r, @"(?m)^\s*(color|background(?:-color)?|border(?:-(?:top|right|bottom|left))?(?:-color)?|outline|fill|stroke):\s*([^;]+);")
                .Select(m => m.Groups[2].Value.Trim()))
            .ToArray();
        Assert.NotEmpty(colours);
        Assert.All(colours, c => Assert.Matches(@"^(none|(\d+px solid )?var\(--[a-z-]+\))$", c));
    }

    // 143 AC-9: the editor-failed line shares the margin rule of the unreadable line and has no rule of its own.
    [Fact]
    public void AppCss_EditorFailed_OnlyInTheUnreadableRule()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        int at = css.IndexOf(".note-editor .unreadable", StringComparison.Ordinal);
        Assert.True(at >= 0);
        int start = css.LastIndexOf('}', at) + 1;
        int brace = css.IndexOf('{', at);
        string[] selectors = css[start..brace].Split(',').Select(sel => sel.Trim()).ToArray();
        Assert.Contains(".note-editor .editor-failed", selectors);
        Assert.Contains(".note-editor .unreadable", selectors);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(css, "editor-failed"));
    }

    // 196, 211 AC-3: the editor area has no box and the layout stays as it was.
    [Fact]
    public void AppCss_EditorHost_HasNoBox_AndNoFocusMarker()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string host = RuleBody(css, ".editor-host");
        string bar = RuleBody(css, ".editor-bar,\n.panel-bar");

        Assert.Equal("margin-top: 12px;", host.Trim());
        Assert.DoesNotContain("border", host, StringComparison.Ordinal);
        Assert.DoesNotContain("background", host, StringComparison.Ordinal);
        Assert.DoesNotContain("padding", host, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(margin|padding)-(left|inline)", host);
        Assert.DoesNotMatch(@"margin:\s*\S+\s+\S+", host);
        Assert.DoesNotContain("padding", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-left", bar, StringComparison.Ordinal);

        Assert.Equal("display: none;", RuleBody(css, ".editor-host:empty").Trim());
        string editorCss = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.css"));
        Assert.Contains("outline: none;", RuleBody(editorCss, ".note-editor-body"), StringComparison.Ordinal);
    }

    // 211 AC-1, AC-5: no selector in app.css (inside @media blocks too) pairs the editor with a focus pseudo-class;
    // editor.css has only the chip's focus ring and (job 349) the Copy reference button's.
    [Fact]
    public void Css_HasNoEditorFocusSelector()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string editorCss = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.css"));

        Assert.DoesNotContain(".editor-host:focus", css, StringComparison.Ordinal);
        Assert.DoesNotContain(CssSelectors(css), IsEditorFocusSelector);
        Assert.Equal(
            [".note-editor-body .chip:focus-visible", ".note-editor-body button.decision-copy:focus-visible"],
            CssSelectors(editorCss).Where(IsEditorFocusSelector));

        int media = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal);
        Assert.True(media >= 0);
        int open = css.IndexOf('{', media);
        int depth = 0;
        int close = open;
        do
        {
            depth += css[close] == '{' ? 1 : css[close] == '}' ? -1 : 0;
            close++;
        }
        while (depth > 0);
        Assert.DoesNotMatch(@"editor-host|note-editor|ProseMirror", css[open..close]);
    }

    // 211 AC-2: no rule whose subject is the editor host, the editor or its body draws a box, a border, a shadow,
    // an outline or a background, in any state.
    [Fact]
    public void Css_EditorItselfHasNoBorderShadowOutlineOrBackground()
    {
        string root = RepoFiles.Root();
        string wwwroot = Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot");
        Regex subject = new(
            @"(^|[\s>+~])\.(editor-host|note-editor|note-editor-body|ProseMirror)(:[\w-]+(\([^)]*\))?)*$");
        Regex forbidden = new(
            @"(?m)(^|[\s;])(box-shadow|border|border-left|border-inline-start|outline|background)\s*:\s*([^;]*)");
        int checkedRules = 0;

        foreach (string file in new[] { Path.Combine(wwwroot, "app.css"), Path.Combine(wwwroot, "editor", "editor.css") })
        {
            foreach ((string selector, string body) in CssRules(File.ReadAllText(file)))
            {
                if (!selector.Split(',').Any(sel => subject.IsMatch(sel.Trim())))
                {
                    continue;
                }

                checkedRules++;
                foreach (System.Text.RegularExpressions.Match m in forbidden.Matches(body))
                {
                    bool outlineNone = m.Groups[2].Value == "outline" && m.Groups[3].Value.Trim() == "none";
                    Assert.True(outlineNone, $"{Path.GetFileName(file)}: {selector} sets {m.Groups[2].Value}");
                }
            }
        }

        Assert.True(checkedRules >= 4, checkedRules.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // 211 AC-4: the keyboard focus rings on other controls stay.
    [Fact]
    public void Css_KeepsTheFocusRingsOfOtherControls()
    {
        // Job 213 added one more: the calendar's focus day; job 289 two: the sidebar's heading button and its Today buttons.
        const int RingsInAppCssBefore211 = 9;
        const int CalendarDayRing = 1;
        const int SidebarDayPickerRings = 2;
        const int DecisionPickerAndCopyRings = 2; // Job 349: the picker and its rows; the Copy and clash buttons.
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string editorCss = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.css"));

        Assert.Equal(
            RingsInAppCssBefore211 + CalendarDayRing + SidebarDayPickerRings + DecisionPickerAndCopyRings,
            System.Text.RegularExpressions.Regex.Matches(css, @"outline: 2px solid var\(--accent-strong\)").Count);
        Assert.Contains(
            "outline: 2px solid var(--accent-strong);",
            RuleBody(editorCss, ".note-editor-body .chip:focus-visible"),
            StringComparison.Ordinal);
    }

    // 349 AC-13: the picker, the Copy reference buttons, the copied line and the clash line take every colour from the
    // theme variables (so light and dark both work) and the picker and the buttons have a visible focus ring.
    [Fact]
    public void Css_DecisionControls_ThemeColoursAndFocus()
    {
        string root = RepoFiles.Root();
        string wwwroot = Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot");
        Regex mine = new(@"decision-(picker|copy|clash|copied)");
        Regex colourProperty = new(@"(?m)(^|[\s;])(color|background|background-color|border|border-color|outline|box-shadow)\s*:\s*([^;]*)");
        Regex fixedColour = new(@"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(|\b(white|black|red|blue|green|gray|grey)\b");
        List<string> selectors = [];

        foreach (string file in new[] { Path.Combine(wwwroot, "app.css"), Path.Combine(wwwroot, "editor", "editor.css") })
        {
            foreach ((string selector, string body) in CssRules(File.ReadAllText(file)))
            {
                if (!mine.IsMatch(selector))
                {
                    continue;
                }

                selectors.AddRange(selector.Split(',').Select(sel => sel.Trim()));
                foreach (System.Text.RegularExpressions.Match m in colourProperty.Matches(body))
                {
                    string value = m.Groups[3].Value.Trim();
                    Assert.False(
                        fixedColour.IsMatch(Regex.Replace(value, @"var\(--[\w-]+\)", "var()")),
                        $"{selector}: {m.Groups[2].Value}: {value}");
                    Assert.True(
                        value.Contains("var(--", StringComparison.Ordinal) || value is "none" or "transparent" or "inherit",
                        $"{selector}: {m.Groups[2].Value}: {value}");
                }
            }
        }

        Assert.Contains(".decision-picker:focus-visible", selectors);
        Assert.Contains(".decision-picker li:focus-visible", selectors);
        Assert.Contains("button.decision-copy:focus-visible", selectors);
        Assert.Contains("p.decision-clash button:focus-visible", selectors);
        Assert.Contains(".note-editor-body button.decision-copy:focus-visible", selectors);
        Assert.Contains("p.decision-clash", selectors);
    }

    // 349: the names job 343's strip-on-copy design used are gone from the sources and the committed bundle.
    [Theory]
    [InlineData("StripMarkers")]
    [InlineData("pastedDecisionMarkdown")]
    [InlineData("DecisionHolder")]
    [InlineData("StripLength")]
    public void Decisions_RemovedNamesAreGone(string name)
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "src/Context.Core", "src/Context", RepoFiles.DesktopFolder })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".razor")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal)));
        }

        foreach (string file in new[] { "editor/src/decisions.js", "editor/src/view.js", "wwwroot/editor/editor.js" })
        {
            if (EditorFile(file).Contains(name, StringComparison.Ordinal))
            {
                found.Add(file);
            }
        }

        Assert.Empty(found);
    }

    private static bool IsEditorFocusSelector(string selector) =>
        System.Text.RegularExpressions.Regex.IsMatch(selector, @"editor-host|note-editor|ProseMirror")
        && System.Text.RegularExpressions.Regex.IsMatch(selector, @":focus(-within|-visible)?\b");

    private static IEnumerable<string> CssSelectors(string css) =>
        CssRules(css).SelectMany(rule => rule.Selector.Split(',')).Select(sel => sel.Trim());

    // Every style rule as (selector list, body), including rules nested in @media blocks; comments are dropped.
    private static IEnumerable<(string Selector, string Body)> CssRules(string css)
    {
        css = System.Text.RegularExpressions.Regex.Replace(css.ReplaceLineEndings("\n"), @"/\*.*?\*/", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            string selector = m.Groups[1].Value.Trim();
            if (!selector.StartsWith('@'))
            {
                yield return (selector, m.Groups[2].Value);
            }
        }
    }

    // 215 AC-2 (user change): no editor rule (inside @media blocks too) whose selector names h4 to h6 sets a size, weight or case.
    [Fact]
    public void EditorCss_LeavesH4ToH6Sizes()
    {
        foreach ((string selector, string body) in CssRules(BuiltEditorCss()))
        {
            bool namesLowHeading = selector.Split(',').Select(sel => sel.Trim()).Any(sel =>
                sel.StartsWith(".note-editor-body", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(sel, @"\bh[4-6]\b"));
            if (!namesLowHeading)
            {
                continue;
            }

            Assert.DoesNotMatch(@"(^|[;\s])font-size\s*:", body);
            Assert.DoesNotMatch(@"(^|[;\s])font\s*:", body);
            Assert.DoesNotMatch(@"(^|[;\s])font-weight\s*:", body);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body, @"text-transform\s*:\s*([^;]*)"))
            {
                Assert.Equal("none", m.Groups[1].Value.Trim());
            }
        }
    }

    // 215 AC-3: from app.css then the built editor.css, the element headingDom draws for each level resolves to
    // level 1 > level 2 > level 3 > level 4 (user change: h3 1.3em), with h4 to h6 unchanged.
    [Fact]
    public void EditorHeadings_RenderLargestFirst()
    {
        EditorBundle bundle = new();
        Dictionary<int, decimal> expected = new() { [1] = 28m, [2] = 22.4m, [3] = 18.2m, [4] = 14m, [5] = 11.62m, [6] = 9.38m };
        foreach ((int level, decimal px) in expected)
        {
            using System.Text.Json.JsonDocument dom = System.Text.Json.JsonDocument.Parse(bundle.Json($"ContextEditor.headingDom({level})"));
            System.Text.Json.JsonElement root = dom.RootElement;
            string tag = root[0].GetString()!;
            string? cls = root.GetArrayLength() == 3 ? root[1].GetProperty("class").GetString() : null;

            Assert.Equal(px, Math.Round(EditorHeadingPx(tag, cls), 2));
        }

        Assert.True(EditorHeadingPx("h2", "heading-level-1") > EditorHeadingPx("h2", null));
        Assert.True(EditorHeadingPx("h2", null) > EditorHeadingPx("h3", null));
        Assert.True(EditorHeadingPx("h3", null) > EditorHeadingPx("h4", null));
    }

    // 215 AC-3: the resolver's inputs: body is 14px, nothing around the editor sets a font-size, and the only app.css
    // heading selectors are the global h1/h2 and the panel ones (219: plus the appointment page's h3 Notes; 243: the
    // sidebar's Waiting for h3; 256: the To review h3).
    [Fact]
    public void EditorHeadings_ResolverInputsHold()
    {
        string app = AppCss();
        string editor = BuiltEditorCss();

        Assert.Contains(CssRules(app), rule => rule.Selector == "body" && rule.Body.Contains("font-size: 14px;", StringComparison.Ordinal));
        string[] around = [".note-editor-body", ".editor-host", ".ProseMirror"];
        foreach ((string selector, string body) in CssRules(app).Concat(CssRules(editor)))
        {
            if (selector.Split(',').Select(sel => sel.Trim()).Any(around.Contains))
            {
                Assert.DoesNotContain("font-size", body, StringComparison.Ordinal);
                Assert.DoesNotMatch(@"(^|[;\s])font\s*:", body);
            }
        }

        string[] headings = CssSelectors(app)
            .Where(sel => System.Text.RegularExpressions.Regex.IsMatch(sel.Split(' ', '>', '+', '~')[^1], @"^h[1-6]\b"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "h1", "h2", ".notes-region h2", ".todos-region h2", ".panel-bar h2", ".settings-panel h3", ".person-panel h2",
            ".links-panel h2", ".review-panel h3", ".review-panel h4.review-day", ".history-bar h2", ".appointment-page h3",
            ".subpages h2", ".board-column h3", ".todos .waiting-for h3", ".todos-region .to-review h3", ".item-panel h2",
            ".done-panel h3.done-week", ".all-todos-panel .all-todos-group h3",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), headings);
    }

    // 256 review: the To review note name is readable text: --accent-strong (not the light --accent) and underlined,
    // one line (not the weekly review's flex row), with a focus ring.
    [Fact]
    public void ToReviewNote_ReadableTextColour()
    {
        (string Selector, string Body)[] rules = CssRules(AppCss())
            .Where(r => r.Selector.Split(',').Select(sel => sel.Trim()).Contains(".to-review .review-note"))
            .ToArray();
        string body = Assert.Single(rules).Body;
        Assert.Contains("color: var(--accent-strong);", body, StringComparison.Ordinal);
        Assert.Contains("text-decoration: underline;", body, StringComparison.Ordinal);
        Assert.Contains("display: inline-block;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("var(--accent)", AppCss(), StringComparison.Ordinal);
        Assert.Contains(CssSelectors(AppCss()), sel => sel == ".to-review .review-note:focus-visible");
    }

    // 215 AC-4: the three size rules set no colour, and the shared editor heading rule keeps its colour and case.
    [Fact]
    public void EditorCss_HeadingSizeRules_SetNoColour()
    {
        string css = BuiltEditorCss();
        foreach (string selector in new[] { "\n.note-editor-body h2", ".note-editor-body h2.heading-level-1", "\n.note-editor-body h3" })
        {
            string body = RuleBody(css, selector);
            Assert.DoesNotContain("color", body, StringComparison.Ordinal);
            Assert.DoesNotContain("background", body, StringComparison.Ordinal);
            Assert.DoesNotContain("border", body, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(", body);
        }

        string shared = RuleBody(css, ".note-editor-body h6");
        Assert.Contains("color: var(--accent-strong);", shared, StringComparison.Ordinal);
        Assert.Contains("text-transform: none;", shared, StringComparison.Ordinal);
        Assert.Contains("letter-spacing: normal;", shared, StringComparison.Ordinal);
    }

    // 215 AC-5: the global h1 and h2 rules stay, and app.css never styles the editor body.
    [Fact]
    public void AppCss_GlobalHeadings_Unchanged()
    {
        string css = AppCss();
        string h1 = RuleBody(css, "\nh1");
        string h2 = RuleBody(css, "}\n\nh2");

        Assert.Contains("font-size: 20px;", h1, StringComparison.Ordinal);
        Assert.Contains("color: var(--accent-strong);", h1, StringComparison.Ordinal);
        Assert.Contains("font-size: 13px;", h2, StringComparison.Ordinal);
        Assert.Contains("color: var(--grey);", h2, StringComparison.Ordinal);
        Assert.Contains("text-transform: uppercase;", h2, StringComparison.Ordinal);
        Assert.DoesNotContain("note-editor-body", css, StringComparison.Ordinal);
    }

    // 215 AC-6: source and built editor.css hold the three size rules; the built editor.js draws the level-1 class.
    [Fact]
    public void EditorCss_SourceAndBuilt_HoldHeadingSizes()
    {
        string[] rules =
        [
            ".note-editor-body h2 { font-size: 1.6em; }",
            ".note-editor-body h2.heading-level-1 { font-size: 2em; }",
            ".note-editor-body h3 { font-size: 1.3em; }",
        ];
        foreach (string css in new[] { EditorFile("editor/src/editor.css"), BuiltEditorCss() })
        {
            string flat = System.Text.RegularExpressions.Regex.Replace(css, @"\s+", " ");
            foreach (string rule in rules)
            {
                Assert.Contains(rule, flat, StringComparison.Ordinal);
            }
        }

        Assert.Contains("heading-level-1", EditorFile("wwwroot/editor/editor.js"), StringComparison.Ordinal);
    }

    private static string AppCss() => EditorFile("wwwroot/app.css");

    private static string BuiltEditorCss() => EditorFile("wwwroot/editor/editor.css");

    // The font-size in px of a heading element inside .note-editor-body: the most specific of "tag",
    // ".note-editor-body tag" and ".note-editor-body tag.cls" in app.css then editor.css (later wins on a tie),
    // em times the inherited 14px, else the browser default.
    private static decimal EditorHeadingPx(string tag, string? cls)
    {
        List<(string Selector, int Specificity)> candidates = [(tag, 1), (".note-editor-body " + tag, 11)];
        if (cls is not null)
        {
            candidates.Add(($".note-editor-body {tag}.{cls}", 21));
        }

        string? best = null;
        int bestSpecificity = -1;
        foreach ((string selector, string body) in CssRules(AppCss()).Concat(CssRules(BuiltEditorCss())))
        {
            System.Text.RegularExpressions.Match size = System.Text.RegularExpressions.Regex.Match(body, @"font-size\s*:\s*([^;]+);");
            if (!size.Success)
            {
                continue;
            }

            foreach (string sel in selector.Split(',').Select(s => s.Trim()))
            {
                foreach ((string candidate, int specificity) in candidates)
                {
                    if (sel == candidate && specificity >= bestSpecificity)
                    {
                        best = size.Groups[1].Value.Trim();
                        bestSpecificity = specificity;
                    }
                }
            }
        }

        Dictionary<string, string> defaults = new() { ["h2"] = "1.5em", ["h3"] = "1.17em", ["h4"] = "1em", ["h5"] = "0.83em", ["h6"] = "0.67em" };
        string value = best ?? defaults[tag];
        if (value.EndsWith("px", StringComparison.Ordinal))
        {
            return decimal.Parse(value[..^2], System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.EndsWith("em", value, StringComparison.Ordinal);
        return decimal.Parse(value[..^2], System.Globalization.CultureInfo.InvariantCulture) * 14m;
    }

    // 209 AC-8: no rule styles a Save button in the editor bar; the shared bar rule stays and a later editor-bar rule
    // keeps the dirty dot right after the title.
    [Fact]
    public void AppCss_EditorBar_HasNoSaveRule_AndKeepsTheDotAfterTheTitle()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"))
            .ReplaceLineEndings("\n");

        Assert.DoesNotContain(".editor-bar .save", css, StringComparison.Ordinal);
        Assert.Contains("justify-content: space-between;", RuleBody(css, ".editor-bar,\n.panel-bar"), StringComparison.Ordinal);
        Assert.Contains("justify-content: flex-start;", RuleBody(css, ".editor-bar"), StringComparison.Ordinal);
        Assert.True(
            css.IndexOf(".editor-bar {", StringComparison.Ordinal) > css.IndexOf(".editor-bar,\n.panel-bar {", StringComparison.Ordinal));
        Assert.Contains("flex: none;", RuleBody(css, "\n.panel-bar .close"), StringComparison.Ordinal);
    }

    // 209 AC-9: README and CLAUDE.md mention no Save button in the note editor, and still name Ctrl/Cmd+S and /save.
    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    public void Docs_NameNoEditorSaveButton_ButKeepTheKeyboardSaves(string file)
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), file));

        Assert.DoesNotContain("Save button", text, StringComparison.Ordinal);
        Assert.Contains("`/save`", text, StringComparison.Ordinal);
        Assert.Matches(@"Ctrl/Cmd\+S|Ctrl\+S \(Cmd\+S", text);
    }

    private static string RuleBody(string css, string selector)
    {
        css = css.ReplaceLineEndings("\n");
        int at = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(at >= 0, selector);
        int open = css.IndexOf('{', at);
        return css[(open + 1)..css.IndexOf('}', open)];
    }

    [Fact]
    public void EditorCss_StylesOnlyTheEditorBody()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.css"));
        string[] selectors = System.Text.RegularExpressions.Regex.Matches(css, @"(?m)^([^\s{}/*][^{]*)\{")
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(sel => sel.Trim())
            .ToArray();

        Assert.NotEmpty(selectors);
        Assert.Contains(selectors, sel => sel.StartsWith(".note-editor-body", StringComparison.Ordinal));
        // 343: the decision flash's animation and its reduced-motion switch (whose rule is .note-editor-body's).
        Assert.All(selectors, sel => Assert.True(
            sel.StartsWith(".note-editor-body", StringComparison.Ordinal) || sel.Contains("ProseMirror", StringComparison.Ordinal)
                || sel is "@keyframes decision-flash" or "@media (prefers-reduced-motion: reduce)",
            sel));
    }

    // AC-8: no Desktop file except DesktopActions.cs calls a Core method that changes data.
    [Fact]
    public void Sources_CallWritingMethodsOnlyInDesktopActions()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        Assert.Contains(ActionsFile, files);
        Assert.Matches(Writes, File.ReadAllText(Path.Combine(root, ActionsFile)));

        List<string> found = [];
        foreach (string file in files.Where(f => f != ActionsFile))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            if (file == InteropFile)
            {
                Assert.Single(Regex.Matches(text, Regex.Escape(InteropReference)));
                text = text.Replace(InteropReference, string.Empty, StringComparison.Ordinal);
            }

            if (file == SealedNoteFile)
            {
                Assert.Contains(KeyFactory, text, StringComparison.Ordinal);
                text = text.Replace(KeyFactory, string.Empty, StringComparison.Ordinal);
            }

            // 136: the Windows key store is the OS store itself. Its Create/Add/Delete calls are .NET's own (CngKey,
            // X509Store, CertificateRequest) and change the certificate store, never the data folder; that it names
            // no Core store is checked in WindowsStore_TouchesNoDataFolder.
            if (file == RealWindowsStoreFile)
            {
                continue;
            }

            // 178: the notifier's Add calls fill a ProcessStartInfo's own argument list, never a Core store.
            if (file == RealNotifierFile)
            {
                Assert.Contains(".ArgumentList.Add(", text, StringComparison.Ordinal);
                text = text.Replace(".ArgumentList.Add(", string.Empty, StringComparison.Ordinal);
            }

            // 237: the crash log deletes its own pruned logs (only regular crash-*.log files in .logs) and nothing else;
            // any other writing call in it (.Move(, .Save( and so on) still fails.
            if (file == CrashLogFile)
            {
                Assert.Contains(CrashLogDelete, text, StringComparison.Ordinal);
                text = text.Replace(CrashLogDelete, string.Empty, StringComparison.Ordinal);
            }

            found.AddRange(Writes.Matches(text).Select(m => $"{file}: {m.Value}"));
        }

        Assert.True(found.Count == 0, "Context.Desktop calls writing methods outside DesktopActions: " + string.Join(", ", found));
    }

    // AC-8: DesktopActions is named only by .razor files, Program.cs and StateComponent.cs, never by the state,
    // the watcher or the snapshot.
    [Fact]
    public void DesktopActions_ReferencedOnlyByComponentsAndProgram()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs"))
        {
            if (file == ActionsFile || ActionsUsers.Contains(file))
            {
                continue;
            }

            if (ActionsName.IsMatch(File.ReadAllText(Path.Combine(root, file))))
            {
                found.Add(file);
            }
        }

        Assert.True(found.Count == 0, "DesktopActions named in: " + string.Join(", ", found));
        foreach (string file in new[] { "DesktopState.cs", "DataWatcher.cs", "DesktopSnapshot.cs" })
        {
            string text = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, file));
            Assert.DoesNotMatch(ActionsName, text);
            Assert.DoesNotMatch(ActionsCall, text);
        }
    }

    // AC-9: in .razor files every @code method that calls Actions. is bound by an event attribute in the same file,
    // is no lifecycle method and is called by no other method; no .cs file calls Actions.
    [Fact]
    public void Razor_CallsActionsOnlyFromEventHandlers()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        int callers = 0;
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            callers += ActionCallers(text).Count;
            found.AddRange(ActionCallProblems(text).Select(p => $"{file}: {p}"));
        }

        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs"))
        {
            if (file != ActionsFile && ActionsCall.IsMatch(File.ReadAllText(Path.Combine(root, file))))
            {
                found.Add($"{file}: calls Actions.");
            }
        }

        Assert.True(callers >= 1, "Expected at least one component calling Actions.");
        Assert.True(found.Count == 0, "Actions called outside event handlers: " + string.Join(", ", found));
    }

    // AC-9 (control): a handler bound by @onclick, a lambda or a component event callback passes.
    [Theory]
    [InlineData("<button type=\"button\" @onclick=\"Go\">x</button>\n@code {\n private async Task Go() { await Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<button type=\"button\" @onclick=\"() => Go(1)\">x</button>\n@code {\n private Task Go(int n) => Actions.CompleteTodo(n, t, c);\n}")]
    [InlineData("<ConfirmDialog OnConfirm=\"Go\" />\n@code {\n private async Task Go(Confirmation c)\n {\n if (x) { await Actions.CompleteTodo(1, t, c); }\n }\n}")]
    [InlineData("<Bar OnRun=\"Run\" OnGo=\"Go\" />\n@code {\n private Task Run() => Go();\n private Task Go() => Actions.CompleteTodo(1, t, c);\n}")]
    [InlineData("<button type=\"button\" @onclick=\"() => SaveAsync(false)\">x</button>\n@code {\n public Task SaveFirstAsync() => SaveAsync(true);\n private Task AutoSaveAsync() => SaveAsync(true);\n private Task SaveAsync(bool a) => Actions.CompleteTodo(1, t, c);\n}")]
    [InlineData("<section @onkeydown=\"e => Save(e)\"></section>\n@code {\n public Task SaveFirstAsync() => SaveAsync(true);\n private Task Save(KeyboardEventArgs k) => SaveAsync(false);\n private Task SaveAsync(bool a) => Actions.CompleteTodo(1, t, c);\n}")]
    public void ActionCall_Pattern_AllowsEventHandlers(string razor)
    {
        Assert.Single(ActionCallers(razor));
        Assert.Empty(ActionCallProblems(razor));
    }

    // AC-9 (control): unbound methods, lifecycle methods and handlers called from other methods are flagged.
    [Theory]
    [InlineData("<p title=\"Go\">x</p>\n@code {\n private void Go() { Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<button type=\"button\" @onclick=\"Other\">x</button>\n@code {\n private void Go() { Actions.CompleteTodo(1, t, c); }\n private void Other() { }\n}")]
    [InlineData("@code {\n protected override void OnInitialized() { _ = Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<p @onclick=\"OnAfterRenderAsync\">x</p>\n@code {\n protected override async Task OnAfterRenderAsync(bool first) { await Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("@code {\n protected override Task OnParametersSetAsync() => Actions.CompleteTodo(1, t, c);\n}")]
    [InlineData("<button type=\"button\" @onclick=\"Go\">x</button>\n@code {\n protected override void OnInitialized() { Go(); }\n private void Go() { Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<button type=\"button\" @onclick=\"Go\">x</button>\n@code {\n private void Helper() { Go(); }\n private void Go() { Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<button type=\"button\" @onclick=\"Go\">x</button>\n@code {\n private void SaveFirstAsync() { Go(); }\n private void Go() { Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<button type=\"button\" @onclick=\"SaveAsync\">x</button>\n@code {\n private void Helper() { SaveAsync(); }\n private void SaveAsync() { Actions.CompleteTodo(1, t, c); }\n}")]
    [InlineData("<section></section>\n@code {\n private Task Save(KeyboardEventArgs k) => SaveAsync(false);\n private Task SaveAsync(bool a) => Actions.CompleteTodo(1, t, c);\n}")]
    [InlineData("<section @onkeydown=\"e => Keys(e)\"></section>\n@code {\n private Task Keys(KeyboardEventArgs k) => SaveAsync(false);\n private Task SaveAsync(bool a) => Actions.CompleteTodo(1, t, c);\n}")]
    public void ActionCall_Pattern_FlagsOtherCallers(string razor)
    {
        Assert.NotEmpty(ActionCallProblems(razor));
    }

    // AC-9: only ConfirmDialog creates a Confirmation.
    [Fact]
    public void Confirmation_CreatedOnlyByConfirmDialog()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor"))
        {
            if (Regex.IsMatch(File.ReadAllText(Path.Combine(root, file)), @"new\s+Confirmation\s*\("))
            {
                found.Add(file);
            }
        }

        Assert.Equal(["src/Context.Desktop/ConfirmDialog.razor"], found);
    }

    // AC-10: every <button> in the components has type="button".
    [Fact]
    public void Razor_ButtonsHaveTypeButton()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        int buttons = 0;
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            buttons += Regex.Matches(text, "<button", RegexOptions.IgnoreCase).Count;
            found.AddRange(UntypedButtons(text).Select(b => $"{file}: {b}"));
        }

        Assert.True(buttons >= 2, "Expected the confirm and Cancel buttons, found " + buttons);
        Assert.True(found.Count == 0, "Buttons without type=\"button\": " + string.Join(", ", found));
    }

    // AC-10 (control): the button check.
    [Theory]
    [InlineData("<button type=\"button\" @onclick=\"() => Go(1)\">x</button>", 0)]
    [InlineData("<button class=\"done\" type=\"button\">x</button>", 0)]
    [InlineData("<button>x</button>", 1)]
    [InlineData("<button type=\"submit\">x</button>", 1)]
    [InlineData("<button data-type=\"button\">x</button>", 1)]
    [InlineData("<BUTTON @onclick=\"() => Go(1)\">x</BUTTON>", 1)]
    public void ButtonType_Pattern(string razor, int untyped)
    {
        Assert.Equal(untyped, UntypedButtons(razor).Count);
    }

    // 080 round 2 / 094 AC-12 / 136 / 178: every <input> is a checkbox, text, date, time, search or password field (the recovery
    // password in SensitiveSetup), nothing binds a value with
    // @bind and there is no <textarea> (the note editor is the bundled editor, not a form field).
    [Fact]
    public void Razor_InputsAreAllowedTypesOnly()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        int inputs = 0;
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            inputs += Regex.Matches(text, "<input", RegexOptions.IgnoreCase).Count;
            found.AddRange(BadInputs(text).Select(i => $"{file}: {i}"));
        }

        Assert.True(inputs >= 1, "Expected the todo checkboxes, found " + inputs);
        Assert.True(found.Count == 0, "Inputs other than checkbox, text, date, time, search or password: " + string.Join(", ", found));
    }

    // 080 round 2 / 094 (control): the input check.
    [Theory]
    [InlineData("<input type=\"checkbox\" checked=\"@x\" @onchange=\"() => Ask(t)\" />", 0)]
    [InlineData("<input class=\"done\" type=\"checkbox\">", 0)]
    [InlineData("<input type=\"text\" value=\"@x\" @oninput=\"Typed\" />", 0)]
    [InlineData("<input type=\"date\" @onchange=\"Chosen\" />", 0)]
    [InlineData("<input type=\"search\" />", 0)]
    [InlineData("<input>", 1)]
    [InlineData("<input type=\"password\" />", 0)]
    [InlineData("<input type=\"password\" @bind=\"secret\" />", 1)]
    [InlineData("<input type=\"text\" @bind=\"x\" />", 1)]
    [InlineData("<input type=\"hidden\" />", 1)]
    [InlineData("<INPUT type=\"file\">", 1)]
    [InlineData("<input data-type=\"checkbox\">", 1)]
    [InlineData("<input type=\"checkbox\" @bind=\"done\" />", 1)]
    [InlineData("<input type=\"checkbox\" @bind-value=\"done\" />", 1)]
    [InlineData("<textarea>x</textarea>", 1)]
    public void Input_Pattern(string razor, int bad)
    {
        Assert.Equal(bad, BadInputs(razor).Count);
    }

    // AC-10: no plain HTML event attributes (on<event>= without '@') on elements.
    [Fact]
    public void Razor_HasNoPlainEventAttributes()
    {
        string root = RepoFiles.Root();
        HashSet<string> components = Components(root);
        List<string> found = [];
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(PlainEvents(text, components).Select(e => $"{file}: {e}"));
        }

        Assert.True(found.Count == 0, "Plain HTML event attributes: " + string.Join(", ", found));
    }

    // AC-10 (control): plain event attributes are caught; Blazor's @on<event> and component callbacks are not.
    [Theory]
    [InlineData("<button type=\"button\" onclick=\"alert(1)\">x</button>", 1)]
    [InlineData("<div ONMOUSEOVER='x'>x</div>", 1)]
    [InlineData("<p class=\"a\"\n   onfocus = \"x\">x</p>", 1)]
    [InlineData("<ConfirmDialog onclick=\"x\" />", 1)]
    [InlineData("<button type=\"button\" @onclick=\"Go\">x</button>", 0)]
    [InlineData("<div @onkeydown=\"Key\" @onclick:preventDefault=\"true\">x</div>", 0)]
    [InlineData("<ConfirmDialog OnConfirm=\"Go\" OnCancel=\"Close\" />", 0)]
    [InlineData("<p title=\"turn onclick= off\">x</p>", 0)]
    public void PlainEvent_Pattern(string razor, int count)
    {
        Assert.Equal(count, PlainEvents(razor, ["ConfirmDialog"]).Count);
    }

    // 138 AC-9: the calendar link's type is named only where it is wired, read for the settings and written.
    [Fact]
    public void Sources_NameTheCalendarLinkOnlyInProgramStateAndActions()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => LinkType.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([ActionsFile, StateFile, ProgramFile], found.Order(StringComparer.Ordinal).ToArray());
    }

    // 138 AC-9: nothing in the desktop project reads the link's text. 354: the URL launcher is the one exception; it
    // gets only a checked web link's Uri (never the calendar link) and must pass its AbsoluteUri to the shell.
    [Fact]
    public void Sources_NeverReadTheCalendarLinksText()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => f != RealUrlLauncherFile)
            .Where(f => LinkText.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.True(found.Count == 0, "The link's text is read in: " + string.Join(", ", found));
    }

    // 138 AC-9: the calendar link is changed only by DesktopActions; components see only the masked view.
    [Fact]
    public void Sources_RunTheCalendarLinkCommandOnlyInDesktopActions()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        List<string> runs = files.Where(f => LinkRun.IsMatch(File.ReadAllText(Path.Combine(root, f)))).ToList();
        List<string> razor = files
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => LinkCoreType.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([ActionsFile], runs.ToArray());
        Assert.True(razor.Count == 0, "A component names a calendar-link type other than the view: " + string.Join(", ", razor));
        Assert.Contains("CalendarLinkView", File.ReadAllText(Path.Combine(root, SettingsFile)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CalendarLink link", true, false, false, true)]
    [InlineData("new CalendarLink(root)", true, false, false, true)]
    [InlineData("CalendarLink? calendarLink = null", true, false, false, true)]
    [InlineData("State.CalendarLinkSetting()", false, false, false, false)]
    [InlineData("Actions.SetCalendarLink(typed, null)", false, false, false, false)]
    [InlineData("Actions.RemoveCalendarLink(confirmed)", false, false, false, false)]
    [InlineData("CalendarLinkView view; CalendarLinkViewKind.Shown", false, false, false, false)]
    [InlineData("DesktopActions.MaxCalendarLinkLength", false, false, false, false)]
    [InlineData("calendarLink", false, false, false, false)]
    [InlineData("link.Get()", false, true, false, false)]
    [InlineData("link . Get ( )", false, true, false, false)]
    [InlineData("uri.AbsoluteUri", false, true, false, false)]
    [InlineData("uri.OriginalString", false, true, false, false)]
    [InlineData("uri.PathAndQuery", false, true, false, false)]
    [InlineData("uri.Query", false, true, false, false)]
    [InlineData("items.GetValueOrDefault(1)", false, false, false, false)]
    [InlineData("tags.QueryTags()", false, false, false, false)]
    [InlineData("CalendarLinkView.Run(link, feed, value)", false, false, true, false)]
    [InlineData("CalendarLinkView . Run(link, feed, value)", false, false, true, false)]
    [InlineData("CalendarLinkView.Show(link, feed)", false, false, false, false)]
    [InlineData("Run(() => 1)", false, false, false, false)]
    [InlineData("CalendarFeed feed", false, false, false, true)]
    [InlineData("catch (CalendarLinkException)", false, false, false, true)]
    [InlineData("CalendarFeedStatus.Stale", false, false, false, false)]
    public void CalendarLink_Patterns(string source, bool type, bool text, bool run, bool core)
    {
        Assert.Equal(type, LinkType.IsMatch(source));
        Assert.Equal(text, LinkText.IsMatch(source));
        Assert.Equal(run, LinkRun.IsMatch(source));
        Assert.Equal(core, LinkCoreType.IsMatch(source));
    }

    // 138 AC-9: nothing can put the link into browser storage or a URL.
    [Fact]
    public void Sources_UseNoBrowserStorageOrNavigation()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        files.Add(RepoFiles.DesktopFolder + "/wwwroot/index.html");
        string editor = Path.Combine(root, RepoFiles.DesktopFolder, "editor", "src");
        files.AddRange(Directory.EnumerateFiles(editor, "*.js").Select(f => RepoFiles.Relative(root, f)));
        Assert.Contains(RepoFiles.DesktopFolder + "/editor/src/index.js", files);

        List<string> found = files.Where(f => Storage.IsMatch(File.ReadAllText(Path.Combine(root, f)))).ToList();

        Assert.True(found.Count == 0, "Browser storage or navigation in: " + string.Join(", ", found));
    }

    [Theory]
    [InlineData("localStorage.setItem('a', b)", true)]
    [InlineData("window.sessionStorage", true)]
    [InlineData("indexedDB.open('x')", true)]
    [InlineData("document.cookie = x", true)]
    [InlineData("document . cookie", true)]
    [InlineData("@inject NavigationManager Navigation", true)]
    [InlineData("nav.NavigateTo(url)", true)]
    [InlineData("document.createElement('div')", false)]
    [InlineData("storage", false)]
    public void Storage_Pattern(string source, bool match)
    {
        Assert.Equal(match, Storage.IsMatch(source));
    }

    // 138 AC-9: the files that hold the link, or the typed value, log nothing.
    [Theory]
    [InlineData(SettingsFile)]
    [InlineData(ActionsFile)]
    [InlineData(StateFile)]
    public void LinkFiles_LogNothing(string file)
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), file));

        Assert.False(Logging.IsMatch(text), file + " logs");
    }

    [Theory]
    [InlineData("ILogger<Main> log", true)]
    [InlineData("Console.WriteLine(x)", true)]
    [InlineData("Console . Error.WriteLine(x)", true)]
    [InlineData("Debug.WriteLine(x)", true)]
    [InlineData("System.Diagnostics.Trace.TraceError(x)", true)]
    [InlineData("AnsiConsole text", false)]
    [InlineData("StackTrace trace", false)]
    [InlineData("IConsole console", false)]
    public void Logging_Pattern(string source, bool match)
    {
        Assert.Equal(match, Logging.IsMatch(source));
    }

    // 138 AC-10: the settings and the key table are styled.
    [Theory]
    [InlineData(".settings-panel {")]
    [InlineData(".calendar-link-entry {")]
    [InlineData(".key-details td {")]
    [InlineData(".notes-bar-buttons {")]
    public void AppCss_StylesTheSettings(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains(selector, css, StringComparison.Ordinal);
    }

    // AC-10 / 094 AC-12: only EditorInterop.cs uses JavaScript interop (IJSRuntime, [JSInvokable]).
    [Fact]
    public void Sources_MentionJSRuntimeOnlyInEditorInterop()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        Assert.Contains(InteropFile, files);
        Assert.True(MentionsJSRuntime(File.ReadAllText(Path.Combine(root, InteropFile))));

        List<string> found = files
            .Where(f => f != InteropFile)
            .Where(f =>
            {
                string text = File.ReadAllText(Path.Combine(root, f));
                return MentionsJSRuntime(text) || text.Contains("JSInvokable", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(found.Count == 0, "JavaScript interop in: " + string.Join(", ", found));
    }

    // 094 AC-9: the [JSInvokable] methods only raise EditorInterop's events; none calls Actions or DesktopActions.
    // 156 AC-10: OnPerson is one more of them. 177 AC-6, AC-9: OnDrop and OnAttachment too. 244 AC-4: OnPasteLink too.
    [Fact]
    public void JSInvokable_MethodsChangeNothing()
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), InteropFile));
        MatchCollection invokable = Regex.Matches(
            text,
            @"\[JSInvokable\]\s*public\s+Task\s+(\w+)\((?:string raw|bool dirty|int count, DroppedFile\[\] files|string href|string url|)\)\s*=>\s*([^;]*);");

        // 203: OnChanged (no parameters) only raises Changed; the editor decides when to save.
        Assert.Equal(
            ["OnJournalBottom", "OnTag", "OnLink", "OnPerson", "OnDirty", "OnDrop", "OnAttachment", "OnWebLink", "OnChanged", "OnPasteLink"],
            invokable.Select(m => m.Groups[1].Value).ToArray());
        Assert.Equal(13, Regex.Matches(text, @"\[JSInvokable").Count);
        // 354: OnWebLink only raises WebLinkClicked.
        Assert.Contains("public Task OnWebLink(string href) => WebLinkClicked?.Invoke(href) ?? Task.CompletedTask;", text, StringComparison.Ordinal);

        // 343, 349: OnDecision only raises DecisionClicked and OnCopiedDecision only DecisionCopied, each only for a
        // reference that parses; DecisionPicks only reads through DecisionPicksSource (DesktopState.DecisionPicks).
        string unix = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach ((string method, string raised) in new[] { ("OnDecision", "DecisionClicked"), ("OnCopiedDecision", "DecisionCopied") })
        {
            Assert.Contains(
                $"public Task {method}(string reference) =>\n        reference is not null && reference.Length <= 30 && DecisionParser.TryParseReference(reference, out DecisionRef parsed)\n"
                + $"            ? {raised}?.Invoke(parsed) ?? Task.CompletedTask\n            : Task.CompletedTask;",
                unix,
                StringComparison.Ordinal);
        }

        Assert.Contains("public async Task<string> DecisionPicks(string query)", unix, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bActions\b|DesktopActions", unix);
        Assert.All(invokable, m => Assert.Matches(
            @"^\s*(JournalBottom\?\.Invoke\(\)|TagClicked\?\.Invoke\(raw\)|LinkClicked\?\.Invoke\(raw\)|PersonClicked\?\.Invoke\(raw\)|DirtyChanged\?\.Invoke\(dirty\)"
            + @"|FilesDropped\?\.Invoke\(new DroppedFiles\(count, files\)\)|AttachmentClicked\?\.Invoke\(href\)|WebLinkClicked\?\.Invoke\(href\)|LinkPasted\?\.Invoke\(url\)|Changed\?\.Invoke\(\)) \?\? Task\.CompletedTask$",
            m.Groups[2].Value));
        Assert.Equal(
            "DirtyChanged?.Invoke(dirty) ?? Task.CompletedTask",
            invokable.Single(m => m.Groups[1].Value == "OnDirty").Groups[2].Value);
        Assert.Contains("js.InvokeAsync<bool>(\"ContextEditor.isDirty\", element)", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(ActionsCall, text);
        Assert.DoesNotMatch(ActionsName, text);
    }

    // 141 AC-9: the editor source and the committed bundle both report dirty flips through the OnDirty callback.
    [Theory]
    [InlineData("editor/src/view.js")]
    [InlineData("wwwroot/editor/editor.js")]
    public void Editor_ReportsDirtyFlips(string file)
    {
        string text = File.ReadAllText(Path.Combine([RepoFiles.Root(), RepoFiles.DesktopFolder, .. file.Split('/')]));

        Assert.Contains("\"OnDirty\"", text, StringComparison.Ordinal);
    }

    // 142 AC-4: chipDom is the only place that makes a tab stop, and only the tag, noteLink and (156) mention nodes
    // use it.
    [Fact]
    public void EditorSchema_MakesOnlyChipsTabStops()
    {
        string text = EditorFile("editor/src/schema.js");

        Assert.Single(Regex.Matches(text, "tabindex"));
        Match chipDom = Regex.Match(text, @"export function chipDom\(kind, raw\) \{.*?\n\}\n", RegexOptions.Singleline);
        Assert.True(chipDom.Success, "chipDom not found");
        Assert.Contains("tabindex: \"0\"", chipDom.Value, StringComparison.Ordinal);

        // Each call sits in the toDOM of the node of the same name: the last node opened above it.
        MatchCollection calls = Regex.Matches(text, @"toDOM\(node\) \{ return chipDom\(""(\w+)"", node\.attrs\.raw\); \}");
        Assert.Equal(["tag", "noteLink", "mention"], calls.Select(m => m.Groups[1].Value));
        Assert.Equal(
            ["tag", "noteLink", "mention"],
            calls.Select(m => Regex.Matches(text[..m.Index], @"(?m)^    (\w+): \{").Last().Groups[1].Value));
        Assert.Equal(4, Regex.Matches(text, @"chipDom\(").Count);
    }

    // 142 AC-5: Enter on a focused chip goes through a keydown handler in the source and in the committed bundle,
    // and the page still calls back only through OnTag, OnLink, (141) OnDirty, (156) OnPerson, (177) OnDrop and
    // OnAttachment, (244) OnPasteLink and (354) OnWebLink.
    [Theory]
    [InlineData("editor/src/view.js")]
    [InlineData("wwwroot/editor/editor.js")]
    public void Editor_OpensChipsOnKeydown_WithoutANewCallback(string file)
    {
        string text = EditorFile(file);

        Assert.Matches(@"handleDOMEvents:\s*\{\s*(?://[^\n]*\n\s*)*keydown\(", text);
        Assert.Contains("opensChip", text, StringComparison.Ordinal);
        Assert.Equal(
            ["DecisionPicks", "OnAttachment", "OnChanged", "OnCopiedDecision", "OnDecision", "OnDirty", "OnDrop", "OnLink", "OnPasteLink", "OnPerson", "OnTag", "OnWebLink"],
            Regex.Matches(text, @"invokeMethodAsync\(""(\w+)""").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
    }

    // 142 AC-6: the chip rules are the same in the source and the committed stylesheet, exactly one of them is the
    // focus ring (the app's), and none of them removes an outline.
    [Fact]
    public void EditorCss_ShowsAFocusRingOnChips()
    {
        List<(string Selector, string Block)> source = ChipRules(EditorFile("editor/src/editor.css"));
        List<(string Selector, string Block)> committed = ChipRules(EditorFile("wwwroot/editor/editor.css"));

        Assert.Equal(source.Select(r => r.Block), committed.Select(r => r.Block));
        foreach (List<(string Selector, string Block)> rules in new[] { source, committed })
        {
            (string Selector, string Block) ring = Assert.Single(rules, r => r.Selector == ".note-editor-body .chip:focus-visible");
            Assert.Contains("outline: 2px solid var(--accent-strong)", ring.Block, StringComparison.Ordinal);
            Assert.Contains("outline-offset: 2px", ring.Block, StringComparison.Ordinal);
            Assert.All(rules, r => Assert.DoesNotMatch(@"outline:\s*(none|0)\b", r.Block));
        }
    }

    // Every rule block whose selector contains ".note-editor-body .chip", in file order: the selector and the text
    // from the selector to the closing brace.
    private static List<(string Selector, string Block)> ChipRules(string css)
    {
        return Regex.Matches(css.Replace("\r\n", "\n"), @"(?m)^([^\s{}/*][^{}]*)\{[^}]*\}")
            .Where(m => m.Groups[1].Value.Contains(".note-editor-body .chip", StringComparison.Ordinal))
            .Select(m => (m.Groups[1].Value.Trim(), m.Value))
            .ToList();
    }

    private static string EditorFile(string file) =>
        File.ReadAllText(Path.Combine([RepoFiles.Root(), RepoFiles.DesktopFolder, .. file.Split('/')]));

    // 094 AC-3: the editor's own sources build no HTML from strings and evaluate no code.
    [Fact]
    public void EditorSources_UseNoHtmlStringsOrEval()
    {
        string root = RepoFiles.Root();
        string folder = Path.Combine(root, RepoFiles.DesktopFolder, "editor");
        List<string> files = Directory.EnumerateFiles(Path.Combine(folder, "src"), "*.js").ToList();
        files.Add(Path.Combine(folder, "build.mjs"));
        Assert.True(files.Count >= 4, "Expected the editor sources, found " + files.Count);

        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            found.AddRange(EditorForbidden
                .Where(m => text.Contains(m, StringComparison.Ordinal))
                .Select(m => $"{RepoFiles.Relative(root, file)}: {m}"));
        }

        Assert.True(found.Count == 0, "Forbidden APIs in the editor sources: " + string.Join(", ", found));
    }

    // AC-10 (control): the JSRuntime check.
    [Theory]
    [InlineData("@inject IJSRuntime JS", true)]
    [InlineData("[Inject] private IJSRuntime js { get; set; }", true)]
    [InlineData("JSRuntime.InvokeVoidAsync(\"x\")", true)]
    [InlineData("await cancelButton.FocusAsync();", false)]
    public void JSRuntime_Pattern(string source, bool mentions)
    {
        Assert.Equal(mentions, MentionsJSRuntime(source));
    }

    // AC-8 (control): the pattern catches each forbidden call.
    [Theory]
    [InlineData("notes.Create(title)")]
    [InlineData("notes.Save(title, bytes)")]
    [InlineData("todos.Add(text, due)")]
    [InlineData("todos.Complete(1)")]
    [InlineData("todos.Complete(todo.Id)")]
    [InlineData("link.Set(value)")]
    [InlineData("link.Clear()")]
    [InlineData("link.Save(value)")]
    [InlineData("link.Delete()")]
    [InlineData("feed.ClearCache()")]
    [InlineData("notes.SaveIfUnchanged(opened, text, out saved)")]
    [InlineData("notes.Rename(source.Name, title)")]
    [InlineData("notes.Delete(name)")]
    [InlineData("notes . Rename (a, b)")]
    [InlineData("notes.CreateChild(owner.Name, normalized)")]
    [InlineData("attachments.MoveWithNote(source.Name, owner?.Name)")]
    [InlineData("attachments.RemoveUnlinked(plain, linked)")]
    [InlineData("attachments.Add(name, file.Name, file.Bytes)")]
    [InlineData("SealedNote.Seal(name, text, recipients)")]
    [InlineData("SealedNote.AddRecipient(name, file, key, agree, added)")]
    [InlineData("notes.DeleteSensitive(name)")]
    [InlineData("state.KeyStore.CreateMachineKey()")]
    [InlineData("keys.StoreRecoveryPublicKey(publicKey)")]
    [InlineData("RecoveryFile.Create(password, out publicKey)")]
    [InlineData("Inbox.Capture(notes, cleaned, localNow(), BeforeCaptureSave)")]
    [InlineData("Inbox . Capture (store, text, now)")]
    [InlineData("InboxTriage.Snooze(notes, item.Line, until, BeforeWrite)")]
    [InlineData("DailyNote.Ensure(notes, day, compose)")]
    [InlineData("Briefing.Ensure(notes, day, compose)")]
    [InlineData("notes.CreateWithText(prepared.Title, prepared.Text)")]
    [InlineData("dayEnd.CloseDay(day)")]
    [InlineData("dayEnd.RefreshWorked(day)")]
    [InlineData("dayEnd.RefreshStandup(day, calendar.Zone)")]
    [InlineData("dayEnd.MoveTodos(day)")]
    [InlineData("dayEnd.AutoClose(today, now)")]
    [InlineData("todos.MoveDue(day, day.AddDays(1))")]
    [InlineData("settings.SaveDayEnd(values)")]
    [InlineData("settings.SaveSavedSearches(searches)")]
    [InlineData("settings.SaveOutlook(on)")]
    [InlineData("settings.SaveMailDigest(on)")]
    [InlineData("todos.Tie(id, link)")]
    [InlineData("history.Keep(note.Path, bytes)")]
    [InlineData("state.HistoryStore.Keep(found.Path, current, found.Name)")]
    [InlineData("state.HistoryStore.RemoveHistory(path, \".md\")")]
    [InlineData("desktopActions.RestoreVersion(name, id, opened, confirmed)")]
    [InlineData("Actions.Save(text)")]
    [InlineData("x.Actions.Delete(name)")]
    public void Writes_Pattern_CatchesCalls(string call)
    {
        Assert.Matches(Writes, call);
    }

    [Theory]
    [InlineData("Actions.RestoreVersion(name, version.Id, request.Note, confirmed)")]
    [InlineData("Actions.MarkSensitive(name, confirmed)")]
    [InlineData("Actions.TriageSnooze(item, until)")]
    [InlineData("Actions.TieTodo(todo.Id, next)")]
    [InlineData("Actions.AddMeetingTodo(shown[0], newText)")]
    public void Writes_Pattern_LeavesCallsOnActions(string call)
    {
        Assert.DoesNotMatch(Writes, call);
    }

    // AC-8 (control): the pattern doesn't flag the reads the Desktop needs.
    [Theory]
    [InlineData("PhotinoBlazorAppBuilder.CreateDefault(args)")]
    [InlineData("builder.Services.AddSingleton(state)")]
    [InlineData("todos.List()")]
    [InlineData("Directory.CreateDirectory(root)")]
    [InlineData("builder.Services.AddSingleton(actions)")]
    [InlineData("Actions.CompleteTodo(n, todo, confirmed)")]
    [InlineData("Actions.SaveNote(request.Note, request.Text)")]
    [InlineData("Actions.AddTodo(newText, due)")]
    [InlineData("notes.ReadForEdit(found)")]
    [InlineData("Actions.CreateNote(newTitle)")]
    [InlineData("Actions.RenameNote(name, renameText, null)")]
    [InlineData("Actions.RenameNote(name, renameText, confirmed)")]
    [InlineData("Actions.DeleteNote(name, confirmed)")]
    [InlineData("Actions.OpenExternally(name)")]
    [InlineData("Actions.MarkSensitive(name, confirmed)")]
    [InlineData("Actions.MakePlain(name, confirmed)")]
    [InlineData("Actions.SetUpSensitive(password, path)")]
    [InlineData("Actions.RestoreFromRecovery(path, password)")]
    [InlineData("Actions.CaptureText(typed)")]
    [InlineData("Actions.OpenDailyNote()")]
    [InlineData("Actions.DailyNoteName()")]
    [InlineData("@onkeydown=\"e => Capture(e)\"")]
    [InlineData("SealedNote.Open(found.Name, file, machine, KeyStore.Agree)")]
    [InlineData("State.ReadSensitive(Name)")]
    [InlineData("State.Session.Lock()")]
    [InlineData("State.Session.Touch()")]
    [InlineData("Dialog.SaveRecoveryFile()")]
    [InlineData("builder.Services.AddSingleton<IFileDialog>(dialog)")]
    public void Writes_Pattern_AllowsReads(string call)
    {
        Assert.DoesNotMatch(Writes, call);
    }

    // AC-8 (control): the DesktopActions name check.
    [Theory]
    [InlineData("private readonly DesktopActions actions;", true)]
    [InlineData("new DesktopActions(notes, todos, this)", true)]
    [InlineData("<see cref=\"DesktopActions\"/>", true)]
    [InlineData("DesktopActionsTests", false)]
    [InlineData("internal DesktopState State", false)]
    public void ActionsName_Pattern(string source, bool mentions)
    {
        Assert.Equal(mentions, ActionsName.IsMatch(source));
    }

    // 133 AC-12: only the real update source and Program.cs name Velopack; nothing else in the app can reach it.
    [Fact]
    public void Sources_NameVelopackOnlyInTheUpdateSourceAndProgram()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("Velopack", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal([ProgramFile, RealUpdateSourceFile], found);
    }

    // 133 AC-12: Velopack's hooks run before anything else in Main (an install or update run exits there).
    [Fact]
    public void Program_RunsVelopackAppFirst_AndStartsTheUpdateWithTheBuildsChannel()
    {
        string program = File.ReadAllText(Path.Combine(RepoFiles.Root(), ProgramFile)).ReplaceLineEndings("\n");

        Assert.Matches(@"private static void Main\(string\[\] args\)\n    \{\n        VelopackApp\.Build\(\)\.Run\(\);\n", program);
        Assert.Single(Regex.Matches(program, @"VelopackApp"));
        Assert.Contains(
            "new " + RealUpdateSource + "(StartupUpdate.IncludePrereleases(StartupUpdate.CurrentVersion))",
            program,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(program, @"new StartupUpdate\("));
        Assert.Contains("_ = update.Start();", program, StringComparison.Ordinal);
        // Fire and forget: the window never waits for the update.
        Assert.DoesNotMatch(@"update\.Start\(\)\s*\.\s*(Wait|Result|GetAwaiter)", program);
        Assert.DoesNotContain("await", program, StringComparison.Ordinal);
    }

    // 396 AC-6: only UpdateLog writes, only .logs/update.log, through PrivateFile and the crash log's no-link rule;
    // Program.cs builds it on the data folder and starts the one update after the hand-over return.
    [Fact]
    public void UpdateLog_WritesOnlyThroughPrivateFile_AndProgramWiresIt()
    {
        string root = RepoFiles.Root();
        string source = File.ReadAllText(Path.Combine(root, "src/Context.Desktop/UpdateLog.cs"));

        Assert.DoesNotMatch(@"\bNoteStore\b|\bDesktopActions\b|\bDesktopState\b|\bConsole\.|\bVelopack|\.Move\(|\.Delete\(|File\.Replace\(", source);
        Assert.Contains("PrivateFile.CreateFolder(", source, StringComparison.Ordinal);
        Assert.Contains("PrivateFile.OpenNew(", source, StringComparison.Ordinal);
        Assert.Contains("PrivateFile.Rewrite(", source, StringComparison.Ordinal);
        Assert.Contains("CrashLog.IsPlainFolder(", source, StringComparison.Ordinal);

        string program = File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n");
        int handOver = program.IndexOf("if (start.HandOver)", StringComparison.Ordinal);
        Assert.True(handOver >= 0);
        Assert.True(program.IndexOf("new StartupUpdate(", StringComparison.Ordinal) > handOver);
        Assert.Single(Regex.Matches(program, @"new UpdateLog\("));
        Assert.Matches(@"new UpdateLog\(\s*NoteStore\.DefaultFolder\(\),", program);
        Assert.Contains("builder.Services.AddSingleton(updateStatus);", program, StringComparison.Ordinal);
    }

    // 133 AC-12, AC-10: the update classes know nothing about the data folder, the actions, the environment or the
    // command line, and write no output.
    [Theory]
    [InlineData("NoteStore")]
    [InlineData("DefaultFolder")]
    [InlineData("DesktopActions")]
    [InlineData("DesktopState")]
    [InlineData("Environment.GetEnvironmentVariable")]
    [InlineData("args")]
    [InlineData("Console.")]
    [InlineData("Debug.")]
    [InlineData("Trace.")]
    [InlineData("File.")]
    [InlineData("Directory.")]
    [InlineData("Process.")]
    public void UpdateClasses_DoNotMention(string forbidden)
    {
        string root = RepoFiles.Root();
        Assert.All(UpdateFiles, file =>
        {
            string path = Path.Combine(root, file);
            Assert.True(File.Exists(path), path + " is missing");
            Assert.DoesNotContain(forbidden, File.ReadAllText(path), StringComparison.Ordinal);
        });
    }

    // 133 AC-11: GitHub releases of this repository over HTTPS, no token, no explicit channel (the installed one
    // stays), no downgrade; installed means Windows and not the portable copy; applying asks for no restart.
    // 135 AC-10: the null token is what keeps a draft release out of the updater's sight (GitHub lists drafts only
    // to a caller with write access), so a release is never installed before the publish job made it public.
    [Fact]
    public void RealUpdateSource_UsesThisRepositorysReleases_WithoutTokenChannelOrDowngrade()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealUpdateSourceFile));

        Assert.Contains(
            "private const string Repository = \"https://github.com/nothisisnotdaniel/context\";",
            source,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"https?://"));
        Assert.Single(Regex.Matches(source, @"new GithubSource\("));
        Assert.Contains("new GithubSource(Repository, null, prereleases)", source, StringComparison.Ordinal);
        Assert.Contains("this.prereleases = prereleases;", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"new UpdateManager\("));
        Assert.Contains("new UpdateOptions { AllowVersionDowngrade = false }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExplicitChannel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", source, StringComparison.Ordinal);
        Assert.DoesNotContain("token", source.Replace("No access token", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!OperatingSystem.IsWindows())", source, StringComparison.Ordinal);
        Assert.Contains("updates.IsInstalled && !updates.IsPortable", source, StringComparison.Ordinal);

        // After exit, silent, no restart; never one of the calls that restart or close the app.
        Assert.Contains("WaitExitThenApplyUpdates(found.TargetFullRelease, true, false)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyUpdatesAndRestart", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyUpdatesAndExit", source, StringComparison.Ordinal);
    }

    // 133 AC-12: no test touches the real source (it would reach the network); tests use FakeUpdateSource.
    [Fact]
    public void Tests_NeverReferenceTheRealUpdateSource()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealUpdateSource, StringComparison.Ordinal)));
        }

        Assert.True(found.Count == 0, "Tests name the real update source: " + string.Join(", ", found));
        Assert.Contains(
            "tests/Context.Desktop.Tests/FakeUpdateSource.cs",
            RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs"));
    }

    // 134 AC-9, 178, 260, 282: only the real launcher, the real notifier, the real area picker and the real Times runner
    // start a process; Process and System.Diagnostics appear nowhere else in the app.
    [Fact]
    public void Sources_UseProcessOnlyInTheLauncher()
    {
        string root = RepoFiles.Root();
        Regex process = new Regex(@"\bProcess\b|System\.Diagnostics", RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => process.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal(
            new[] { RealLauncherFile, RealNotifierFile, RealScreenCaptureFile, RealUrlLauncherFile }.Order(StringComparer.Ordinal),
            found.Order(StringComparer.Ordinal));
    }

    // 134 AC-9: the real launcher is named only by itself and Program.cs, which wires it into the actions.
    [Fact]
    public void Sources_NameTheLauncherOnlyInItselfAndProgram()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealLauncher, StringComparison.Ordinal))
            .ToList();

        Assert.Equal([ProgramFile, RealLauncherFile], found);
        Assert.Contains(
            "notes, todos, state, new " + RealLauncher + "(), calendarLink, feed, () => DateTime.Now, calendar,",
            File.ReadAllText(Path.Combine(root, ProgramFile)),
            StringComparison.Ordinal);
    }

    // 171 AC-10, 172 AC-10: the day-end timer runs the two automatic writes: Program.cs calls exactly AutoDayEnd and
    // AutoBriefing as methods on the actions (in AutoWrites, which runs once at start and in the timer; the
    // constructor and DesktopActions.DayEndInterval are allowed), and no other .cs or .razor file calls either.
    [Fact]
    public void Program_CallsOnlyTheAutomaticWritesOnTheActions()
    {
        string root = RepoFiles.Root();
        string program = File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n");

        string[] calls = Regex.Matches(program, @"\bactions\s*\.\s*(\w+)\s*\(")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        Assert.Equal(
            // 329: ConvertTodoMarkers runs once at start, before todos.Migrate(); 349: MigrateDecisions right after.
            ["ConvertTodoMarkers", "MigrateDecisions", "AutoDayEnd", "AutoBriefing", "AutoReminders", "AutoOpenToday", "AutoReminders", "OpenRequestedItem", "ImportScreenshot", "CaptureScreenshot"],
            calls);
        Assert.Contains(
            "static Task AutoWrites(DesktopActions actions) =>\n            actions.AutoDayEnd().ContinueWith("
            + "_ => actions.AutoBriefing(), TaskScheduler.Default).Unwrap();\n",
            program,
            StringComparison.Ordinal);

        // 178 AC-6, AC-7: the reminders run once at start before the automatic writes, then on their own timer.
        // 216 AC-4: today's note is made sure of and offered once at start, after the reminders and before the writes.
        Assert.Contains(
            "        _ = actions.AutoReminders()\n"
            + "            .ContinueWith(_ => actions.AutoOpenToday(), TaskScheduler.Default).Unwrap()\n"
            + "            .ContinueWith(_ => AutoWrites(actions), TaskScheduler.Default).Unwrap();\n",
            program,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(program, @"\bAutoOpenToday\b"));
        Assert.Contains(
            "using Timer reminders = new Timer(\n            _ => _ = actions.AutoReminders(), null, "
            + "DesktopActions.ReminderInterval / 2, DesktopActions.ReminderInterval);",
            program,
            StringComparison.Ordinal);
        Assert.Equal(
            2, Regex.Matches(program, @"\bDesktopActions\s*\.\s*ReminderInterval\b").Count);
        Assert.Contains(
            "using Timer dayEnd = new Timer(\n            _ => _ = AutoWrites(actions), null, DesktopActions.DayEndInterval, DesktopActions.DayEndInterval);",
            program,
            StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(program, @"\bAutoWrites\s*\(").Count);
        Assert.Equal(
            2, Regex.Matches(program, @"\bDesktopActions\s*\.\s*DayEndInterval\b").Count);
        Assert.DoesNotMatch(@"\bDesktopActions\s*\.\s*(AutoDayEnd|EndDay|AutoBriefing|OpenBriefing|AutoReminders|AutoOpenToday)\b", program);

        foreach (string name in new[] { "AutoDayEnd", "AutoBriefing", "AutoReminders", "AutoOpenToday" })
        {
            List<string> others = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
                .Where(f => f != ProgramFile && f != ActionsFile)
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))
                .ToList();
            Assert.Empty(others);
        }
    }

    // 172 AC-10: Briefing.Ensure and the offer (DesktopState.OfferBriefing) are called only in DesktopActions.cs.
    [Theory]
    [InlineData(@"\bBriefing\s*\.\s*Ensure\s*\(")]
    [InlineData(@"\.\s*OfferBriefing\s*\(")]
    [InlineData(@"\.\s*OfferStartNote\s*\(")]
    public void Sources_CallBriefingWritesOnlyInDesktopActions(string pattern)
    {
        string root = RepoFiles.Root();
        Regex call = new Regex(pattern, RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => call.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([ActionsFile], found);
    }

    // 178 AC-11: MarkNotified is defined in TodoStore.cs and called only in DesktopActions.cs (tests are not checked).
    [Fact]
    public void Sources_NameMarkNotifiedOnlyInTodoStoreAndDesktopActions()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("MarkNotified", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["src/Context.Core/TodoStore.cs", ActionsFile], found);
    }

    // 287 AC-14: the store write .Tie( is called only in DesktopActions.cs (in the Core and the desktop sources), and
    // the meeting todos panel reaches the new writes only through Actions.TieTodo( and Actions.AddMeetingTodo(.
    [Fact]
    public void Sources_CallTieOnlyInDesktopActions()
    {
        string root = RepoFiles.Root();
        Regex tie = new(@"\.\s*Tie\s*\(", RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => tie.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([ActionsFile], found);
        string panel = File.ReadAllText(Path.Combine(root, "src/Context.Desktop/MeetingTodosPanel.razor"));
        Assert.Contains("Actions.TieTodo(", panel, StringComparison.Ordinal);
        Assert.Contains("Actions.AddMeetingTodo(", panel, StringComparison.Ordinal);
    }

    // 237 AC-12: the crash log names no data store, no actions and no console; its session file is written in place
    // (no temp file, no move), and Program.cs hooks both exception events and ends the session after the window.
    [Fact]
    public void CrashLog_NamesNoStoreActionsOrConsole()
    {
        string root = RepoFiles.Root();
        string source = File.ReadAllText(Path.Combine(root, CrashLogFile));

        Assert.DoesNotMatch(@"\bNoteStore\b|\bDesktopActions\b|\bConsole\.|\.Move\(|File\.Replace\(", source);
        // 274: the logs are opened through PrivateFile.OpenNew (FileMode.CreateNew, 0600 off Windows).
        Assert.Contains("PrivateFile.OpenNew(path)", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, Regex.Escape(CrashLogDelete)));

        string program = File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n");
        Assert.Matches(@"VelopackApp\.Build\(\)\.Run\(\);\n\n(?:        //[^\n]*\n)*        CrashLog crashLog = new CrashLog\(", program);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException +=", program, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException +=", program, StringComparison.Ordinal);
        Assert.Contains("e.SetObserved();", program, StringComparison.Ordinal);
        Assert.Contains("        app.Run();\n        crashLog.End();\n", program, StringComparison.Ordinal);
    }

    // 334 AC-8: Program.cs records ProcessExit through the crash log and starts the session end watcher only on
    // Windows, after the hand-over return; the watcher is named only by itself and Program.cs.
    [Fact]
    public void Program_RecordsTheExitReason()
    {
        string root = RepoFiles.Root();
        string program = File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n");

        Assert.Contains(
            "AppDomain.CurrentDomain.ProcessExit += (_, _) => crashLog.RecordExit(ExitReason.ProcessExit);",
            program,
            StringComparison.Ordinal);
        int handOver = program.IndexOf("if (start.HandOver)", StringComparison.Ordinal);
        Assert.True(handOver >= 0, "Program.cs has no hand-over return");
        Match started = Regex.Match(
            program,
            @"if \(OperatingSystem\.IsWindows\(\)\)\n\s*\{\n\s*_ = new " + RealSessionWatcher + @"\(crashLog\)\.Start\(\);\n\s*\}");
        Assert.True(started.Success, "Program.cs doesn't start the watcher inside an OperatingSystem.IsWindows() branch");
        Assert.True(started.Index > handOver, "The watcher starts before the hand-over return");
        Assert.Single(Regex.Matches(program, @"\bnew " + RealSessionWatcher + @"\b"));

        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealSessionWatcher, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
            new[] { ProgramFile, RealSessionWatcherFile }.Order(StringComparer.Ordinal),
            found.Order(StringComparer.Ordinal));
    }

    // 334 AC-8: no test constructs the session end watcher or calls into it (it would make a user32 window), and the
    // job adds no package.
    [Fact]
    public void Tests_NeverLoadTheSessionEndWatcher()
    {
        string root = RepoFiles.Root();
        Regex use = new(@"\bnew\s+" + RealSessionWatcher + @"\b|\b" + RealSessionWatcher + @"\s*\.|typeof\s*\(\s*"
            + RealSessionWatcher + @"\b|<\s*" + RealSessionWatcher + @"\s*>", RegexOptions.CultureInvariant);
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => use.IsMatch(File.ReadAllText(Path.Combine(root, f)))));
        }

        Assert.True(found.Count == 0, "Tests load the session end watcher: " + string.Join(", ", found));
        // Every desktop package is one CLAUDE.md's stack names, so the watcher brought no interop package with it.
        string project = File.ReadAllText(Path.Combine(root, "src/Context.Desktop/Context.Desktop.csproj"));
        string conventions = File.ReadAllText(Path.Combine(root, "CLAUDE.md"));
        MatchCollection packages = Regex.Matches(project, @"<PackageReference Include=""([^""]+)""");
        Assert.NotEmpty(packages);
        Assert.All(packages, m => Assert.Contains("`" + m.Groups[1].Value + "`", conventions, StringComparison.Ordinal));
    }

    // 315 AC-9: Program.cs takes the running lock through DesktopStartup before the window is built, never calls
    // crashLog.Begin() or PrivateFile.OpenLock itself, and brings the window to the front on a show request.
    [Fact]
    public void Program_StartsThroughDesktopStartup()
    {
        string root = RepoFiles.Root();
        string program = File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n");

        int begin = program.IndexOf("DesktopStartup.Begin(", StringComparison.Ordinal);
        int builder = program.IndexOf("PhotinoBlazorAppBuilder", StringComparison.Ordinal);
        Assert.True(begin >= 0, "Program.cs doesn't call DesktopStartup.Begin");
        Assert.True(builder > begin, "DesktopStartup.Begin comes after PhotinoBlazorAppBuilder");
        Assert.DoesNotContain("crashLog.Begin()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateFile.OpenLock", program, StringComparison.Ordinal);
        Assert.Contains("openRequests.ShowRequested +=", program, StringComparison.Ordinal);
        Assert.Matches(@"if \(start\.HandOver\)\n\s*\{\n\s*return;\n\s*\}", program);
    }

    // 178 AC-11: the real notifier is named only by itself and Program.cs, which hands it to the actions.
    [Fact]
    public void Sources_NameTheNotifierOnlyInItselfAndProgram()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealNotifier, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([ProgramFile, RealNotifierFile], found);
        Assert.Contains(
            "string? timesFolder = TimesFolder.Resolve(Environment.GetEnvironmentVariable(TimesFolder.VariableName));\n",
            File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Contains(
            "            timesFolder, new " + RealNotifier + "(),\n"
                + "            ScreenCapture(folder), new " + RealUrlLauncher + "(), timesRunner);",
            File.ReadAllText(Path.Combine(root, ProgramFile)).ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    // 178 AC-11: the title and body reach the fixed scripts only as separate arguments or environment values, the
    // programs are absolute paths started without a shell, and no script text is built from them.
    [Fact]
    public void Notifier_PassesTextOnlyAsArgumentsOrEnvironment()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealNotifierFile)).ReplaceLineEndings("\n");

        Assert.Contains("mac.ArgumentList.Add(title);\n", source, StringComparison.Ordinal);
        Assert.Contains("mac.ArgumentList.Add(body);\n", source, StringComparison.Ordinal);
        Assert.Contains("windows.Environment[TitleVariable] = title;\n", source, StringComparison.Ordinal);
        Assert.Contains("windows.Environment[BodyVariable] = body;\n", source, StringComparison.Ordinal);
        Assert.Contains("\"/usr/bin/osascript\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "Path.Combine(Environment.SystemDirectory, \"WindowsPowerShell\", \"v1.0\", \"powershell.exe\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false,", source, StringComparison.Ordinal);
        Assert.Contains("\"on run argv\",", source, StringComparison.Ordinal);
        Assert.Contains("$env:\" + TitleVariable + \"", source, StringComparison.Ordinal);
        Assert.Contains("$env:\" + BodyVariable + \"", source, StringComparison.Ordinal);
        Assert.Contains("CreateTextNode(", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"Process\.Start\("));

        // title and body appear only in Show, Start's parameters and the two arguments or environment values.
        string code = Regex.Replace(
            string.Join("\n", source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))),
            @"""[^""\n]*""",
            "\"\"");
        Assert.Equal(5, Regex.Matches(code, @"\btitle\b").Count);
        Assert.Equal(5, Regex.Matches(code, @"\bbody\b").Count);
        Assert.DoesNotContain("Arguments =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Format", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Concat", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Join", source, StringComparison.Ordinal);
    }

    // 178 AC-11: no test touches the real notifier (it would show notifications); tests use FakeNotifier.
    [Fact]
    public void Tests_NeverReferenceTheRealNotifier()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealNotifier, StringComparison.Ordinal)));
        }

        Assert.True(found.Count == 0, "Tests name the real notifier: " + string.Join(", ", found));
        Assert.Contains(
            "tests/Context.Desktop.Tests/FakeNotifier.cs",
            RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs"));
    }

    // 282 AC-9, 381: only the Times runner's own test names the real runner (it starts temp scripts); every other test
    // uses FakeTimesRunner (WorkItemSourceTests only reads its source). In the apps only the runner itself (in Core
    // since job 381) and the two Program.cs name it.
    [Fact]
    public void Sources_NameTheTimesRunnerOnlyInItselfProgramAndItsTest()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests", RepoFiles.DesktopFolder, "src/Context.Core", "src/Context" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".razor", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealTimesRunner, StringComparison.Ordinal)));
        }

        Assert.Equal(
            new[] { ProgramFile, CliProgramFile, RealTimesRunnerFile, RealTimesRunnerTestFile, "tests/Context.Tests/WorkItemSourceTests.cs" }
                .Order(StringComparer.Ordinal),
            found.Order(StringComparer.Ordinal));
        Assert.Contains(
            "tests/Context.Tests/FakeTimesRunner.cs",
            RepoFiles.Sources(root, "tests/Context.Tests", ".cs"));
        Assert.Contains(
            "<Compile Include=\"..\\Context.Tests\\FakeTimesRunner.cs\" Link=\"FakeTimesRunner.cs\" />",
            File.ReadAllText(Path.Combine(root, "tests/Context.Desktop.Tests/Context.Desktop.Tests.csproj")),
            StringComparison.Ordinal);
    }

    // 282 AC-9: the bar page sets shown text only through textContent and runs no built code.
    [Fact]
    public void CommandBarScript_UsesNoHtmlOrEval()
    {
        string script = File.ReadAllText(Path.Combine(RepoFiles.Root(), CommandBarFolder, "commandbar.js"));

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\beval\s*\(", RegexOptions.CultureInvariant), script);
        Assert.DoesNotContain("new Function", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }

    // 282 AC-9: the bar page is local-only: a strict CSP and its one script.
    [Fact]
    public void CommandBarPage_HasTheCspAndOneLocalScript()
    {
        string root = RepoFiles.Root();
        string page = File.ReadAllText(Path.Combine(root, CommandBarFolder, "commandbar.html"));

        Assert.Contains(
            "content=\"default-src 'none'; script-src 'self'; style-src 'self'; base-uri 'none'; form-action 'none'\"",
            page,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "<script", RegexOptions.CultureInvariant));
        Assert.Contains("<script src=\"commandbar.js\"></script>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("http", page.Replace("http-equiv", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(
            ["commandbar.css", "commandbar.html", "commandbar.js"],
            Directory.GetFiles(Path.Combine(root, CommandBarFolder)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // 282: the bar window serves only its three files.
    [Theory]
    [InlineData("ctxbar://localhost/commandbar.html", "commandbar.html")]
    [InlineData("ctxbar://localhost/commandbar.js?v=1", "commandbar.js")]
    [InlineData("ctxbar://localhost/commandbar.css#x", "commandbar.css")]
    [InlineData("ctxbar://localhost/../appsettings.json", null)]
    [InlineData("ctxbar://localhost/index.html", null)]
    [InlineData("ctxbar://localhost/commandbar/commandbar.js", null)]
    [InlineData("ctxbar://evil/commandbar.js", null)]
    [InlineData("https://localhost/commandbar.js", null)]
    [InlineData("ctxbar://localhost/COMMANDBAR.JS", null)]
    public void CommandBarWindow_ServesOnlyItsFiles(string url, string? file)
    {
        Assert.Equal(file, PhotinoCommandBarWindow.FileFor(url));
    }

    // 134 AC-9: the launcher passes the path as FileName through the shell, with no arguments.
    [Fact]
    public void Launcher_UsesShellExecuteWithThePathOnly()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealLauncherFile));

        Assert.Contains("FileName = path,", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true,", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"Process\.Start\("));
        Assert.DoesNotContain("Arguments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentList", source, StringComparison.Ordinal);
    }

    // 354: the URL launcher passes only the checked URI's AbsoluteUri as FileName through the shell, with no arguments.
    [Fact]
    public void UrlLauncher_UsesShellExecuteWithTheAbsoluteUriOnly()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealUrlLauncherFile));

        Assert.Contains("FileName = uri.AbsoluteUri,", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true,", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"Process\.Start\("));
        Assert.DoesNotContain("Arguments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentList", source, StringComparison.Ordinal);
    }

    // 354: the real URL launcher is named only by itself and Program.cs, no test names it (tests use FakeUrlLauncher),
    // and only DesktopActions calls Open on a URL launcher.
    [Fact]
    public void UrlLauncher_NamedOnlyInItselfAndProgram_NeverInTests()
    {
        string root = RepoFiles.Root();
        List<string> named = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealUrlLauncher, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal([ProgramFile, RealUrlLauncherFile], named);

        List<string> inTests = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            inTests.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealUrlLauncher, StringComparison.Ordinal)));
        }

        Assert.True(inTests.Count == 0, "Tests name the real URL launcher: " + string.Join(", ", inTests));
        Assert.Contains("tests/Context.Desktop.Tests/FakeUrlLauncher.cs", RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs"));

        List<string> opening = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("urlLauncher.Open(", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["src/Context.Desktop/DesktopActions.cs"], opening);
    }

    // 354 AC-9: a link in the editor is the accent colour and keeps its underline, and --accent-strong reaches 4.5:1 on
    // the editor background in the light and both dark theme blocks.
    [Fact]
    public void Css_EditorLinks_AccentColourReadable()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        foreach (string css in new[] { Path.Combine(desktop, "editor", "src", "editor.css"), Path.Combine(desktop, "wwwroot", "editor", "editor.css") })
        {
            string link = ThemeBlock(File.ReadAllText(css), ".note-editor-body .link {");
            Assert.Contains("color: var(--accent-strong);", link, StringComparison.Ordinal);
            Assert.Contains("text-decoration: underline;", link, StringComparison.Ordinal);
        }

        string app = File.ReadAllText(Path.Combine(desktop, "wwwroot", "app.css"));
        foreach (string theme in new[] { ThemeBlock(app, ":root {"), ThemeBlock(app, "@media (prefers-color-scheme: dark) {"), ThemeBlock(app, ":root[data-theme=\"dark\"] {") })
        {
            double ratio = Contrast(Colour(theme, "--accent-strong"), Colour(theme, "--background"));
            Assert.True(ratio >= 4.5, $"--accent-strong on --background: {ratio:F2}");
        }
    }

    // 134 AC-9: no test touches the real launcher (it would open apps); tests use FakeNoteLauncher.
    [Fact]
    public void Tests_NeverReferenceTheRealLauncher()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealLauncher, StringComparison.Ordinal)));
        }

        Assert.True(found.Count == 0, "Tests name the real launcher: " + string.Join(", ", found));
        Assert.Contains(
            "tests/Context.Desktop.Tests/FakeNoteLauncher.cs",
            RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs"));
    }

    // 136 AC-10, 217 AC-8: native calls live only in the macOS key store and the Outlook COM source (334: and the
    // Windows session end watcher).
    [Fact]
    public void Sources_HaveNativeCallsOnlyInTheMacStore()
    {
        string root = RepoFiles.Root();
        string[] native = ["DllImport", "LibraryImport", "extern "];
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => native.Any(n => File.ReadAllText(Path.Combine(root, f)).Contains(n, StringComparison.Ordinal)))
            .ToList();

        Assert.Equal(
            new[]
            {
                RealHotkeyFile, RealMacStoreFile, RealOutlookFile, RealSessionWatcherFile, RealMacProStoreFile,
                RealWindowsProStoreFile,
            }.Order(StringComparer.Ordinal),
            found.Order(StringComparer.Ordinal));
    }

    // 260 AC-10, AC-11: the real area picker and hotkey are named only by themselves and Program.cs, which picks them
    // on macOS only.
    [Theory]
    [InlineData(RealScreenCapture, RealScreenCaptureFile, "return new " + RealScreenCapture + "(root);")]
    [InlineData(RealHotkey, RealHotkeyFile, "return new " + RealHotkey + "(keyCode, modifiers, id);")]
    public void Sources_NameTheScreenshotPartsOnlyInThemselvesAndProgram(string name, string file, string made)
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(new[] { file, ProgramFile }.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
        string program = File.ReadAllText(Path.Combine(root, ProgramFile));
        Assert.Single(Regex.Matches(program, @"\bnew " + name + @"\b"));
        Assert.Matches(@"if \(OperatingSystem\.IsMacOS\(\)\)\s*\{\s*" + Regex.Escape(made), program);
    }

    // 260 AC-11: no test names the real area picker or hotkey (they would open the screen picker or register a
    // system-wide key); tests use FakeScreenCapture and FakeGlobalHotkey.
    [Fact]
    public void Tests_NeverReferenceTheRealScreenshotParts()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f =>
                {
                    string text = File.ReadAllText(Path.Combine(root, f));
                    return text.Contains(RealScreenCapture, StringComparison.Ordinal)
                        || text.Contains(RealHotkey, StringComparison.Ordinal);
                }));
        }

        Assert.True(found.Count == 0, "Tests name the real screenshot parts: " + string.Join(", ", found));
    }

    // 260: the area picker runs only the fixed screencapture program with fixed arguments and an app-made temp path,
    // without a shell.
    [Fact]
    public void ScreenCapture_RunsAFixedProgramWithoutAShell()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealScreenCaptureFile));

        Assert.Contains("\"/usr/sbin/screencapture\"", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
        Assert.Contains("\"-i\", \"-x\", temp", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\bnew ProcessStartInfo\b"));
    }

    // 217 AC-8: the Outlook COM source is named only by itself and Program.cs, which picks it on Windows.
    [Fact]
    public void Sources_NameTheOutlookSourceOnlyInItselfAndProgram()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealOutlook, StringComparison.Ordinal))
            .ToList();

        Assert.Equal([RealOutlookFile, ProgramFile], found.Order(StringComparer.Ordinal));
        string program = File.ReadAllText(Path.Combine(root, ProgramFile));
        Assert.Contains("return new " + RealOutlook + "();", program, StringComparison.Ordinal);
        Assert.Contains("return new UnsupportedOutlookSource();", program, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows()", program, StringComparison.Ordinal);
    }

    // 217 AC-8: no test touches the real Outlook source (it would talk to COM); tests use FakeOutlookSource.
    [Fact]
    public void Tests_NeverReferenceTheRealOutlookSource()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(RealOutlook, StringComparison.Ordinal)));
        }

        Assert.True(found.Count == 0, "Tests name the real Outlook source: " + string.Join(", ", found));
        Assert.Contains(
            "tests/Context.Desktop.Tests/FakeOutlookSource.cs",
            RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs"));
    }

    // 217 AC-8: the Outlook source only attaches to a running Outlook (GetActiveObject) and never starts one or
    // writes to it.
    [Theory]
    [InlineData("Activator.CreateInstance")]
    [InlineData("CoCreateInstance")]
    [InlineData(".Save(")]
    [InlineData(".Send(")]
    [InlineData(".Delete(")]
    [InlineData(".Move(")]
    [InlineData(".Display(")]
    [InlineData("CreateItem")]
    public void OutlookSource_AttachesOnlyAndNeverWrites(string forbidden)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile));

        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("GetActiveObject(", source, StringComparison.Ordinal);
        Assert.Contains("CLSIDFromProgID(", source, StringComparison.Ordinal);
        Assert.Contains("Marshal.FinalReleaseComObject(", source, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"windows\")]", source, StringComparison.Ordinal);
    }

    // 217 AC-8: the Outlook source reads no HTML body and no recipients, and never asks a folder for its Count.
    // 286 AC-11: the plain Body is allowed only as the Mail digest's capped read (OutlookSource_ReadsBodyOnlyCapped).
    [Theory]
    [InlineData("HTMLBody")]
    [InlineData("Recipients")]
    [InlineData("RequiredAttendees")]
    [InlineData("OptionalAttendees")]
    [InlineData("SenderEmailAddress")]
    [InlineData("Attachments")]
    [InlineData(".Count")]
    public void OutlookSource_ReadsNoBodyOrRecipients(string forbidden)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile));

        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    // 223 AC-3, 286 AC-11: the Outlook source reads no sender, no received time and no Inbox folder outside the
    // Mail digest's two mail methods (comments aside), and those read only the listed fields.
    [Theory]
    [InlineData("SenderName")]
    [InlineData("ReceivedTime")]
    [InlineData("InboxFolder")]
    [InlineData("Inbox")]
    public void OutlookSource_ReadsMailOnlyInTheMailMethods(string name)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile)).ReplaceLineEndings("\n");
        string recent = Between(source, "public OutlookRead<MailMessage> RecentMail(", "// The running Outlook");
        string read = Between(source, "private static MailMessage? ReadMail(", "// A wall-clock time");
        string rest = source.Replace(recent, string.Empty, StringComparison.Ordinal).Replace(read, string.Empty, StringComparison.Ordinal);
        string code = string.Join('\n', rest.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain(name, code, StringComparison.Ordinal);
        Assert.Contains(name, recent + read, StringComparison.Ordinal);
    }

    // 286 AC-11: the mail read asks each item for exactly these properties, and nothing else.
    [Fact]
    public void OutlookSource_ReadMail_ReadsOnlyTheListedFields()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile)).ReplaceLineEndings("\n");
        string read = Between(source, "private static MailMessage? ReadMail(", "// A wall-clock time");

        string[] names = [.. Regex.Matches(read, @"\bitem\.(\w+)").Select(m => m.Groups[1].Value).Distinct()];

        Assert.Equal(["Sensitivity", "Subject", "SenderName", "ReceivedTime", "MessageClass", "Body"], names);
    }

    // 286 AC-11: the plain Body is read once, in the mail read, and cut to MailDigest.MaxBodyLength right after.
    [Fact]
    public void OutlookSource_ReadsBodyOnlyCapped()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile)).ReplaceLineEndings("\n");
        string read = Between(source, "private static MailMessage? ReadMail(", "// A wall-clock time");

        Assert.Single(Regex.Matches(source, @"\bBody\b"));
        Assert.Single(Regex.Matches(read, @"\bBody\b"));
        int body = read.IndexOf("item.Body", StringComparison.Ordinal);
        int cap = read.IndexOf("body.Length > MailDigest.MaxBodyLength", StringComparison.Ordinal);
        int cut = read.IndexOf("body = body[..keep]", StringComparison.Ordinal);
        Assert.True(body >= 0 && cap > body && cut > cap);
        Assert.Equal(4000, MailDigest.MaxBodyLength);
    }

    // 286 AC-11: the mail read never writes, sends, deletes, moves or shows an item.
    [Theory]
    [InlineData(".Save(")]
    [InlineData(".Send(")]
    [InlineData(".Delete(")]
    [InlineData(".Move(")]
    [InlineData(".Display(")]
    [InlineData("HTMLBody")]
    [InlineData("Recipients")]
    [InlineData("SenderEmailAddress")]
    public void OutlookSource_MailRead_StillRefuses(string forbidden)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile));

        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    // 286 AC-11: the mail read, too, reads each item's Sensitivity and skips private ones before anything else.
    [Fact]
    public void OutlookSource_ReadMail_ReadsSensitivityFirst()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile)).ReplaceLineEndings("\n");
        string read = Between(source, "private static MailMessage? ReadMail(", "// A wall-clock time");

        int sensitivity = read.IndexOf("item.Sensitivity", StringComparison.Ordinal);
        int check = read.IndexOf("OutlookSensitivity.IsPrivate(", StringComparison.Ordinal);
        int subject = read.IndexOf("item.Subject", StringComparison.Ordinal);
        Assert.True(sensitivity >= 0 && check > sensitivity && subject > check);
        Assert.Equal(sensitivity, read.IndexOf("item.", StringComparison.Ordinal));
    }

    // The text from start up to (not including) end; both must be found.
    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    // 227 AC-6: the Outlook source reads each item's Sensitivity and skips private ones before reading its Subject.
    [Fact]
    public void OutlookSource_ReadsSensitivityBeforeSubject()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealOutlookFile));
        int method = source.IndexOf("Appointment? ReadAppointment(", StringComparison.Ordinal);
        Assert.True(method >= 0);
        string body = source[method..];

        Assert.Contains("item.Sensitivity", source, StringComparison.Ordinal);
        Assert.Contains("OutlookSensitivity.IsPrivate(", source, StringComparison.Ordinal);
        int sensitivity = body.IndexOf("item.Sensitivity", StringComparison.Ordinal);
        int check = body.IndexOf("OutlookSensitivity.IsPrivate(", StringComparison.Ordinal);
        int subject = body.IndexOf("item.Subject", StringComparison.Ordinal);
        Assert.True(sensitivity >= 0 && check > sensitivity && subject > check);
        Assert.True(body.IndexOf("item.", StringComparison.Ordinal) == sensitivity);
    }

    // 223 AC-2: the Mail panel is gone, and no source names its summary or view (286 brings back only RecentMail).
    [Fact]
    public void Sources_HaveNoMailPanel()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f =>
            {
                string text = File.ReadAllText(Path.Combine(root, f));
                return new[] { "MailSummary", "RecentMailView", "RecentMailPanel" }
                    .Any(n => text.Contains(n, StringComparison.Ordinal));
            })
            .ToList();

        Assert.False(File.Exists(Path.Combine(root, RepoFiles.DesktopFolder, "RecentMailPanel.razor")));
        Assert.Empty(found);
    }

    // 223 AC-4: the stylesheet has no mail rules left.
    [Theory]
    [InlineData("recent-mail")]
    [InlineData("mail-item")]
    public void AppCss_HasNoMailRules(string forbidden)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.DoesNotContain(forbidden, css, StringComparison.Ordinal);
    }

    // 223 AC-5, 286: the settings page mentions mail only for the Mail digest's checkbox.
    [Fact]
    public void SettingsPanel_MentionsMailOnlyForTheDigest()
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "SettingsPanel.razor"));
        string rest = Regex.Replace(
            text.Replace("Suggest todos from Outlook mail", string.Empty, StringComparison.Ordinal),
            "mail-?digest",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.DoesNotContain("mail", rest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read calendar from Outlook", text, StringComparison.Ordinal);
        Assert.Contains("Suggest todos from Outlook mail", text, StringComparison.Ordinal);
    }

    // 223 AC-8: README's Outlook section mentions no mail or Inbox.
    [Fact]
    public void Readme_OutlookSection_MentionsNoMail()
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), "README.md")).ReplaceLineEndings("\n");
        int start = text.IndexOf("\n### Outlook\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = text.IndexOf("\n#", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? text[start..] : text[start..end];

        Assert.Contains("Read calendar from Outlook", section, StringComparison.Ordinal);
        Assert.DoesNotContain("mail", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inbox", section, StringComparison.OrdinalIgnoreCase);
    }

    // 223 AC-8: CLAUDE.md names no removed mail panel or summary.
    [Theory]
    [InlineData("RecentMailPanel")]
    [InlineData("MailSummary")]
    public void ClaudeMd_NamesNoMailPanel(string forbidden)
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), "CLAUDE.md"));

        Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
    }

    // 286 AC-12: CLAUDE.md no longer says Context never reads the Inbox; it names the Mail digest's read as the only one.
    [Fact]
    public void ClaudeMd_NamesTheMailDigestInboxRead()
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), "CLAUDE.md"));

        Assert.DoesNotContain("never reads the Inbox", text, StringComparison.Ordinal);
        Assert.Contains("The Mail digest's read is the only Inbox read", text, StringComparison.Ordinal);
    }

    // 136 AC-10: the certificate store and CNG are used only in the Windows key store.
    [Theory]
    [InlineData("X509Store")]
    [InlineData("CngKey")]
    public void Sources_UseTheCertificateStoreOnlyInTheWindowsStore(string name)
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.Equal([RealWindowsStoreFile], found);
    }

    // 136: the Windows key store is left out of the writing-calls scan, so it must not know the data folder at all.
    [Theory]
    [InlineData("NoteStore")]
    [InlineData("TodoStore")]
    [InlineData("CalendarLink")]
    [InlineData("CalendarFeed")]
    [InlineData("DesktopState")]
    [InlineData("File.")]
    [InlineData("Directory.")]
    [InlineData("Path.")]
    public void WindowsStore_TouchesNoDataFolder(string forbidden)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RealWindowsStoreFile));

        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    // 136: the real key stores and the real file dialog are created only in Program.cs.
    [Fact]
    public void Sources_NameTheRealKeyStoresAndFileDialogOnlyInThemselvesAndProgram()
    {
        string root = RepoFiles.Root();
        Assert.All(new[] { RealWindowsStore, RealMacStore, RealFileDialog }, name =>
        {
            List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
                .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
            string[] expected = [ProgramFile, "src/Context.Desktop/" + name + ".cs"];

            Assert.Equal(expected.Order(StringComparer.Ordinal), found);
        });
    }

    // 136 AC-10 / AC-11: no test touches a real key store, Keychain or file dialog; tests use the fakes.
    [Fact]
    public void Tests_NeverReferenceTheRealKeyStoresOrFileDialog()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string folder in new[] { "tests/Context.Tests", "tests/Context.Desktop.Tests" })
        {
            found.AddRange(RepoFiles.Sources(root, folder, ".cs", ".csproj")
                .Where(f => new[] { RealWindowsStore, RealMacStore, RealFileDialog, RealMacProStore, RealWindowsProStore }.Any(
                    name => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))));
        }

        Assert.True(found.Count == 0, "Tests name a real key store or the real file dialog: " + string.Join(", ", found));
        List<string> tests = RepoFiles.Sources(root, "tests/Context.Desktop.Tests", ".cs");
        Assert.Contains("tests/Context.Desktop.Tests/FakeKeyStore.cs", tests);
        Assert.Contains("tests/Context.Desktop.Tests/FakeFileDialog.cs", tests);
        Assert.Contains("tests/Context.Tests/FakeLicenceStore.cs", RepoFiles.Sources(root, "tests/Context.Tests", ".cs"));
    }

    // 367 AC-9: the licence files only verify: no private key import, no signing, no network, no private key PEM, and
    // nothing that logs. Other files (the calendar feed, the sensitive-note key stores) are not checked.
    [Fact]
    public void LicenceFiles_OnlyVerify()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, "src/Context.Core", ".cs")
            .Where(f => Path.GetFileName(f).StartsWith("Licence", StringComparison.Ordinal) || f == "src/Context.Core/Pro.cs")
            .Concat([RealMacProStoreFile, RealWindowsProStoreFile])
            .ToList();
        Assert.Equal(7, files.Count);
        string[] banned =
        [
            "ImportECPrivateKey", "ImportPkcs8PrivateKey", "SignData", "HttpClient", "PRIVATE" + " KEY", "Console.",
            "CrashLog", "Debug.Write", "Trace.",
        ];

        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            Assert.All(banned, b => Assert.False(text.Contains(b, StringComparison.Ordinal), file + " mentions " + b));
        }
    }

    // 367 AC-9: the real licence stores are named only by themselves and Program.cs, which picks one per OS.
    [Theory]
    [InlineData(RealMacProStore, RealMacProStoreFile)]
    [InlineData(RealWindowsProStore, RealWindowsProStoreFile)]
    public void Sources_NameTheLicenceStoresOnlyInThemselvesAndProgram(string name, string file)
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, "src", ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(new[] { file, ProgramFile }.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
        Assert.Single(Regex.Matches(File.ReadAllText(Path.Combine(root, ProgramFile)), @"\bnew " + name + @"\b"));
    }

    // 139 AC-10: the Core calls that read an import source and write an export copy are named only in DesktopActions.cs.
    [Theory]
    [InlineData("WritePlainCopy")]
    [InlineData("ReadImport")]
    public void Sources_NameTheTransferCallsOnlyInDesktopActions(string call)
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains(call, StringComparison.Ordinal))
            .ToList();

        Assert.Equal([ActionsFile], found);
    }

    // 139 AC-10: no component touches the file system itself; paths only pass through to DesktopActions.
    [Fact]
    public void Razor_NamesNoFileSystemClass()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor");
        Assert.Contains("src/Context.Desktop/NotesPanel.razor", files);
        List<string> found = [];
        foreach (string file in files)
        {
            found.AddRange(FileSystemClass.Matches(File.ReadAllText(Path.Combine(root, file))).Select(m => $"{file}: {m.Value}"));
        }

        Assert.True(found.Count == 0, "Components name a file system class: " + string.Join(", ", found));
    }

    // 240 AC-8: the recently opened list belongs to the desktop app; the command-line tool never names it.
    [Fact]
    public void Cli_NamesNoRecentNotes()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, "src/Context", ".cs", ".csproj", ".json");
        Assert.Contains("src/Context/Cli.cs", files);
        List<string> found = files
            .Where(file =>
            {
                string text = File.ReadAllText(Path.Combine(root, file));
                return text.Contains("recent.json", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("RecentNotes", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(found.Count == 0, "The CLI names the recent notes: " + string.Join(", ", found));
    }

    // 278 AC-9: the pinned list belongs to the desktop app; the command-line tool never names it.
    [Fact]
    public void Cli_NamesNoPinnedNotes()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, "src/Context", ".cs", ".csproj", ".json");
        Assert.Contains("src/Context/Cli.cs", files);
        List<string> found = files
            .Where(file =>
            {
                string text = File.ReadAllText(Path.Combine(root, file));
                return text.Contains("pinned.json", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("PinnedNotes", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(found.Count == 0, "The CLI names the pinned notes: " + string.Join(", ", found));
    }

    // 139 AC-10 (control): whole words only, so RecoveryFile. does not count.
    [Theory]
    [InlineData("File.Exists(path)", true)]
    [InlineData("System.IO.Directory.CreateDirectory(x)", true)]
    [InlineData("Path.Combine(a, b)", true)]
    [InlineData("@RecoveryFile.MinPasswordLength", false)]
    [InlineData("line.Source", false)]
    [InlineData("note.Path)", false)]
    [InlineData("Dialog.OpenMarkdownFiles()", false)]
    public void FileSystemClass_Pattern(string source, bool match)
    {
        Assert.Equal(match, FileSystemClass.IsMatch(source));
    }

    // 139 AC-6 / AC-8: the import row and its result lines are styled.
    [Theory]
    [InlineData(".note-import {")]
    [InlineData(".import-result {")]
    public void AppCss_StylesTheImport(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains(selector, css, StringComparison.Ordinal);
    }

    // 140 AC-10: Inbox.Capture is called only in DesktopActions.cs, and the capture box calls the action only.
    [Fact]
    public void Sources_CallInboxCaptureOnlyInDesktopActions()
    {
        string root = RepoFiles.Root();
        Regex call = new Regex(@"\bInbox\s*\.\s*Capture\s*\(", RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => call.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([ActionsFile], found);
    }

    // 179: the inbox triage writes (InboxTriage.Remove and InboxTriage.Snooze) are called only in DesktopActions.cs.
    [Theory]
    [InlineData(@"\bInboxTriage\s*\.\s*Remove\s*\(")]
    [InlineData(@"\bInboxTriage\s*\.\s*Snooze\s*\(")]
    public void Sources_CallInboxTriageWritesOnlyInDesktopActions(string pattern)
    {
        string root = RepoFiles.Root();
        Regex call = new Regex(pattern, RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => call.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([ActionsFile], found);
    }

    // 179: the triage panel passes the component rules and shows only fixed texts or text that went through
    // DesktopText.OneLine.
    [Fact]
    public void TriagePanel_FollowsTheComponentRules()
    {
        string razor = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "TriagePanel.razor"));

        Assert.DoesNotContain(RawMarkup, m => razor.Contains(m, StringComparison.Ordinal));
        Assert.DoesNotContain(RazorForbidden, m => razor.Contains(m, StringComparison.Ordinal));
        Assert.Equal(
            ["AppendToNote", "CreateFromItem", "DelegateItem", "DropConfirmed", "PanelKeyDown", "SnoozeChosen", "SnoozeTyped", "TodoFromItem"],
            ActionCallers(razor).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(ActionCallProblems(razor));
        Assert.Empty(UntypedButtons(razor));
        Assert.Empty(BadInputs(razor));
        Assert.DoesNotMatch(FileSystemClass, razor);
        Assert.DoesNotContain("@bind", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("new Confirmation(", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Lock(", razor, StringComparison.Ordinal);

        List<string> shown = Regex.Matches(razor, @"ShowStatus\(([^;]*)\);").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(shown);
        Assert.All(
            shown,
            s => Assert.True(
                s.StartsWith("DesktopText.OneLine(", StringComparison.Ordinal)
                || s.StartsWith("DesktopActions.Triage", StringComparison.Ordinal)
                || s.StartsWith("string.Empty, string.Empty", StringComparison.Ordinal),
                s));
    }

    // 179: the triage panel is styled.
    [Theory]
    [InlineData(".triage-panel {")]
    [InlineData(".triage-buttons,")]
    [InlineData(".triage-status:empty {")]
    public void AppCss_StylesTheTriagePanel(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains(selector, css, StringComparison.Ordinal);
    }

    // 136: the sensitive session and the sealed format are not named by the console or the Core (see
    // CoreDependencyTests); in the Desktop a Confirmation still comes only from ConfirmDialog, also for Mark
    // sensitive and Make plain.
    [Fact]
    public void Main_AsksInAConfirmDialogBeforeMarkAndMakePlain()
    {
        string main = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "Main.razor"));

        Assert.Contains("OnConfirm=\"MarkConfirmed\"", main, StringComparison.Ordinal);
        Assert.Contains("OnConfirm=\"MakePlainConfirmed\"", main, StringComparison.Ordinal);
        Assert.Contains("Actions.MarkSensitive(name, confirmed)", main, StringComparison.Ordinal);
        Assert.Contains("Actions.MakePlain(name, confirmed)", main, StringComparison.Ordinal);
    }

    // 349 AC-6: a state change never waits on the index for the clash lines; it starts the read only while one is due.
    [Fact]
    public void Main_StateChange_NeverAwaitsTheClashRead()
    {
        string main = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "Main.razor"));
        string changed = Between(main, "protected override async Task OnStateChangedAsync()", "TakeConversion();");

        Assert.Contains("if (State.DecisionClashesDue)", changed, StringComparison.Ordinal);
        Assert.Contains("ShowDecisionClashesLater();", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("await TakeDecisionClashesAsync", changed, StringComparison.Ordinal);
    }

    private static bool MentionsJSRuntime(string source) => source.Contains("JSRuntime", StringComparison.Ordinal);

    private static HashSet<string> Components(string root) =>
        RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.Ordinal);

    // Every <input> that is not type="checkbox", "text", "date", "time", "search" or "password" or that uses @bind, and every
    // <textarea>.
    private static List<string> BadInputs(string razor) =>
        Tag.Matches(MarkupOf(razor))
            .Where(m => m.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase)
                ? !Regex.IsMatch(m.Groups[2].Value, @"(?<![\w-])type\s*=\s*""(checkbox|text|date|time|search|password)""")
                    || m.Groups[2].Value.Contains("@bind", StringComparison.Ordinal)
                : m.Groups[1].Value.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Value)
            .ToList();

    // Every <button ...> tag (any case) without type="button".
    private static List<string> UntypedButtons(string razor) =>
        Tag.Matches(MarkupOf(razor))
            .Where(m => string.Equals(m.Groups[1].Value, "button", StringComparison.OrdinalIgnoreCase))
            .Where(m => !ButtonType.IsMatch(m.Groups[2].Value))
            .Select(m => m.Value)
            .ToList();

    // Every on<event>= attribute without '@' on an HTML element (a tag that names no component). Quoted values
    // are blanked first, so text inside an attribute value is not an attribute.
    private static List<string> PlainEvents(string razor, IReadOnlyCollection<string> components)
    {
        List<string> found = [];
        foreach (Match tag in Tag.Matches(MarkupOf(razor)))
        {
            string attributes = Regex.Replace(tag.Groups[2].Value, @"""[^""]*""|'[^']*'", "\"\"");
            bool component = components.Contains(tag.Groups[1].Value);
            foreach (Match plain in PlainEvent.Matches(attributes))
            {
                if (!component || !char.IsUpper(plain.Value[0]))
                {
                    found.Add($"<{tag.Groups[1].Value} {plain.Value}");
                }
            }
        }

        return found;
    }

    // The markup part of a .razor file: everything outside its @code blocks.
    private static string MarkupOf(string razor)
    {
        string markup = razor;
        foreach ((int start, int end) in CodeBlocks(razor).OrderByDescending(b => b.Start))
        {
            markup = markup.Remove(start, end - start);
        }

        return markup;
    }

    // The @code blocks of a .razor file as (start of "@code", index after its closing brace).
    private static List<(int Start, int End)> CodeBlocks(string razor)
    {
        List<(int Start, int End)> blocks = [];
        foreach (Match code in Regex.Matches(razor, @"@code\s*\{"))
        {
            int open = code.Index + code.Length - 1;
            int close = MatchingBrace(razor, open);
            blocks.Add((code.Index, close + 1));
        }

        return blocks;
    }

    private static int MatchingBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return text.Length - 1;
    }

    // The top-level methods of the @code blocks: name and body (block or expression).
    private static List<(string Name, string Body)> CodeMethods(string razor)
    {
        List<(string Name, string Body)> methods = [];
        foreach ((int start, int end) in CodeBlocks(razor))
        {
            string code = razor[start..end];
            int bodyStart = code.IndexOf('{') + 1;
            foreach (Match header in MethodHeader.Matches(code, bodyStart))
            {
                if (Keywords.Contains(header.Groups[1].Value) || Depth(code, bodyStart, header.Index) != 0)
                {
                    continue;
                }

                string body;
                if (header.Groups[3].Value == "{")
                {
                    int open = header.Index + header.Length - 1;
                    body = code[open..(MatchingBrace(code, open) + 1)];
                }
                else
                {
                    int semicolon = code.IndexOf(';', header.Index + header.Length);
                    body = code[(header.Index + header.Length)..(semicolon < 0 ? code.Length : semicolon)];
                }

                methods.Add((header.Groups[1].Value, body));
            }
        }

        return methods;
    }

    private static int Depth(string code, int from, int to)
    {
        int depth = 0;
        for (int i = from; i < to; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}')
            {
                depth--;
            }
        }

        return depth;
    }

    private static List<string> ActionCallers(string razor) =>
        CodeMethods(razor).Where(m => ActionsCall.IsMatch(m.Body)).Select(m => m.Name).ToList();

    // Why the @code methods that call Actions. aren't plain event handlers (empty when they all are).
    private static List<string> ActionCallProblems(string razor)
    {
        List<(string Name, string Body)> methods = CodeMethods(razor);
        List<string> bindings = EventBinding.Matches(MarkupOf(razor)).Select(m => m.Groups[1].Value).ToList();
        List<string> problems = [];
        // 240: recording an open (Actions.RecordOpened, run by Main.ShowNote on every route that opens a note) changes
        // no note or other user data, is never refused as Busy and never refreshes, so it is the one call let through.
        foreach ((string name, string body) in methods.Where(m => ActionsCall.IsMatch(m.Body.Replace(RecordOpenedCall, string.Empty, StringComparison.Ordinal))))
        {
            Regex word = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
            if (Lifecycle.Any(l => name.StartsWith(l, StringComparison.Ordinal)))
            {
                problems.Add(name + " is a lifecycle method");
            }

            // 209: with the Save button gone, the note editor's SaveAsync is bound only through the bound key handler
            // Save (Ctrl/Cmd+S); only this pair counts as bound through its caller.
            bool boundThrough = methods.Any(m => KeyCallers.Contains((m.Name, name)) && word.IsMatch(m.Body)
                && IsBoundHandler(m.Name, bindings));
            // 282: the command bar's runner (Main's RunFromBar) is bound to the bar window's Enter key through
            // CommandBarContext: it counts as bound only when OnInitialized registers exactly it.
            bool barBound = IsBarRunner(name, methods);
            if (!bindings.Any(b => word.IsMatch(b)) && !boundThrough && !barBound)
            {
                problems.Add(name + " is bound by no event attribute");
            }

            // 199: a helper shared by handlers (Main's CaptureAsync, run by the palette's RunCommand and bound to the
            // top bar's OnCapture) may be called by another method only when that caller is itself a bound,
            // non-lifecycle event handler.
            // 203: the note editor's one save path (SaveAsync, run by Ctrl/Cmd+S and /save) is also run by the pause after
            // the last edit (AutoSaveAsync) and by the parent before it leaves the note (SaveFirstAsync); only these
            // two pairs are exempt.
            if (methods.Any(m => m.Name != name && word.IsMatch(m.Body) && !IsBoundHandler(m.Name, bindings)
                && !SaveCallers.Contains((m.Name, name))
                && !(barBound && m.Name == "OnInitialized")))
            {
                problems.Add(name + " is called by another method");
            }
        }

        return problems;
    }

    private const string RecordOpenedCall = "Actions.RecordOpened(";

    private const string BarRunner = "RunFromBar";

    // 282: RunFromBar is the bar's runner when OnInitialized stores exactly it and registers that with CommandBar.
    private static bool IsBarRunner(string name, List<(string Name, string Body)> methods) =>
        name == BarRunner
        && methods.Any(m => m.Name == "OnInitialized"
            && m.Body.Contains("barRunner = " + BarRunner + ";", StringComparison.Ordinal)
            && m.Body.Contains("CommandBar.Register(barRunner);", StringComparison.Ordinal));

    private static readonly HashSet<(string Caller, string Callee)> SaveCallers =
        [("SaveFirstAsync", "SaveAsync"), ("AutoSaveAsync", "SaveAsync")];

    private static readonly HashSet<(string Caller, string Callee)> KeyCallers = [("Save", "SaveAsync")];

    private static bool IsBoundHandler(string name, List<string> bindings)
    {
        Regex word = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
        return !Lifecycle.Any(l => name.StartsWith(l, StringComparison.Ordinal)) && bindings.Any(b => word.IsMatch(b));
    }

    // 136 round 2 (ui 1): the focus and disabled looks of a field apply only in that state, for every input type.
    [Theory]
    [InlineData("text")]
    [InlineData("date")]
    [InlineData("search")]
    [InlineData("password")]
    public void AppCss_FocusAndDisabledRules_NameTheStateForEveryInputType(string type)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string bare = $"input[type=\"{type}\"]";

        Assert.Single(Regex.Matches(css, Regex.Escape(bare) + @"(?=[,\s]*[,{])"));
        Assert.Single(Regex.Matches(css, Regex.Escape(bare + ":focus-visible")));
        Assert.Single(Regex.Matches(css, Regex.Escape(bare + ":disabled")));
    }

    // 193 AC-9: the page, close and hidden note-area rules exist and use theme variables only.
    [Theory]
    [InlineData(".page {")]
    [InlineData(".page .close {")]
    [InlineData(".note-area[hidden] {")]
    public void AppCss_PageRules_ExistWithoutColourLiterals(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        int at = css.IndexOf("\n" + selector, StringComparison.Ordinal);
        Assert.True(at >= 0);
        string block = css[at..css.IndexOf('}', at)];
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(|\b(white|black|red|green|blue|gray|grey|transparent|orange|yellow|purple|silver)\b", block);
    }

    // 194 AC-1: the capture bar starts at the top edge: no top padding on the region, no top margin or padding on the bar,
    // and the dead button rule is gone.
    [Fact]
    public void Css_CaptureBarIsFlushAtTheTop()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string region = css[css.IndexOf(".current-region {", StringComparison.Ordinal)..];
        region = region[..region.IndexOf('}')];
        Assert.Contains("padding-top: 0;", region);
        string bar = css[css.IndexOf(".top-bar {", StringComparison.Ordinal)..];
        bar = bar[..bar.IndexOf('}')];
        Assert.Contains("padding: 0;", bar);
        Assert.DoesNotContain(".capture-box", css);
    }

    // 194 AC-8, 197 AC-4/AC-5: every submit-only box has exactly its short placeholder, and the submit buttons are gone.
    [Theory]
    [InlineData("NotesPanel.razor", "New note title", "class=\"note-new\"")]
    [InlineData("TodoPanel.razor", "New todo", "class=\"add\"")]
    [InlineData("SettingsPanel.razor", "https:// or webcal:// link", "class=\"calendar-link-save\"")]
    [InlineData("Main.razor", "Empty: top level", "class=\"move-here\"")]
    [InlineData("Main.razor", "Subpage title", "class=\"create-subpage\"")]
    [InlineData("Main.razor", "New name", "class=\"save-name\"")]
    [InlineData("TriagePanel.razor", "yyyy-MM-dd", "class=\"triage-submit\"")]
    [InlineData("TriagePanel.razor", "Note name", "class=\"triage-submit\"")]
    [InlineData("TriagePanel.razor", "Todo text", "class=\"triage-submit\"")]
    [InlineData("TriagePanel.razor", "Note title", "class=\"triage-submit\"")]
    public void Razor_SubmitButtonGone_PlaceholderIsShort(string file, string placeholder, string removed)
    {
        string razor = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, file));
        Assert.Contains("placeholder=\"" + placeholder + "\"", razor);
        Assert.DoesNotContain("Return to", razor);
        Assert.DoesNotContain(removed, razor);
    }

    // 197 AC-2, AC-5, AC-7
    [Fact]
    public void Css_NoLogoRule_BarAlignsRight_NoReturnHints_ClaudeMdUpdated()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        Assert.DoesNotContain(".logo", css);
        string bar = css[css.IndexOf(".notes-bar {", StringComparison.Ordinal)..];
        bar = bar[..bar.IndexOf('}')];
        Assert.Contains("justify-content: flex-end;", bar);
        foreach (string f in Directory.EnumerateFiles(Path.Combine(root, RepoFiles.DesktopFolder), "*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            Assert.DoesNotContain("Return to", File.ReadAllText(f));
        }

        string md = File.ReadAllText(Path.Combine(root, "CLAUDE.md"));
        Assert.DoesNotContain("Sidebar logo", md);
        Assert.DoesNotContain("svg.logo", md);
        Assert.Contains("Return to", md);
    }

    // 198 AC-2: the top bar's buttons align right, the icons are 18px like the sidebar's, no URL in TopBar.razor.
    [Fact]
    public void TopBar_CssAndMarkup()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string razor = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "TopBar.razor"));
        string buttons = css[css.IndexOf(".top-bar-buttons {", StringComparison.Ordinal)..];
        buttons = buttons[..buttons.IndexOf('}')];
        Assert.Contains("justify-content: flex-end;", buttons);
        string svg = css[css.IndexOf(".top-bar-buttons button svg {", StringComparison.Ordinal)..];
        svg = svg[..svg.IndexOf('}')];
        Assert.Contains("width: 18px;", svg);
        Assert.Contains("height: 18px;", svg);
        Assert.DoesNotContain("http", razor, StringComparison.Ordinal);
        // Capture, Search (job 218), Today, Pick a day, and (job 213) the calendar's Previous and Next month arrows,
        // which job 289 moved into DayPicker.razor.
        string picker = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "DayPicker.razor"));
        Assert.Equal(4, Regex.Matches(razor, "stroke=\"currentColor\"").Count);
        Assert.Equal(2, Regex.Matches(picker, "stroke=\"currentColor\"").Count);
        Assert.DoesNotContain("http", picker, StringComparison.Ordinal);
    }

    // 199 AC-1, AC-8, AC-9: the Capture icon is inline (no URL, no img), the popover is styled like the day pick, and
    // Main captures in one place.
    [Fact]
    public void TopBar_CaptureIconPopoverAndOneCaptureCall()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string razor = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "TopBar.razor"));
        string main = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "Main.razor"));

        Assert.DoesNotContain("http", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", razor, StringComparison.Ordinal);
        Assert.Contains("class=\"capture-open\"", razor, StringComparison.Ordinal);
        Assert.Contains("Func<string, Task<bool>>", razor, StringComparison.Ordinal);

        string backdrop = css[css.IndexOf(".capture-pop-backdrop {", StringComparison.Ordinal)..];
        backdrop = backdrop[..backdrop.IndexOf('}')];
        Assert.Contains("position: fixed;", backdrop);
        Assert.Contains("inset: 0;", backdrop);
        string pop = css[css.IndexOf(".capture-pop {", StringComparison.Ordinal)..];
        pop = pop[..pop.IndexOf('}')];
        Assert.Contains("top: 100%;", pop);
        Assert.Contains("background: var(--panel);", pop);
        Assert.Contains("border: 1px solid var(--line);", pop);
        Assert.Contains(".capture-pop input {", css, StringComparison.Ordinal);
        string button = css[css.IndexOf(".top-bar-buttons button {", StringComparison.Ordinal)..];
        button = button[..button.IndexOf('}')];
        Assert.Contains("width: 30px;", button);
        Assert.Contains("color: var(--text);", button);
        Assert.Contains(".top-bar-buttons button:hover {\n    color: var(--accent-strong);", css.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains(".top-bar-buttons button:focus-visible {", css, StringComparison.Ordinal);

        // 282: the second call is the command bar's capture line, in its runner RunFromBar.
        Assert.Equal(2, Regex.Matches(main, Regex.Escape("Actions.CaptureText(")).Count);
        Assert.Single(Regex.Matches(main, Regex.Escape("captured = await Actions.CaptureText(text);")));
        Assert.Single(Regex.Matches(main, Regex.Escape("captured = await Actions.CaptureText(rest);")));
        Assert.Contains("OnCapture=\"CaptureAsync\"", main, StringComparison.Ordinal);
        Assert.Contains("Task<bool> CaptureAsync(string text)", main, StringComparison.Ordinal);
    }

    // 218 AC-9: the Search popover and its backdrop are styled like Capture's, with theme colours only (var(--…) or
    // transparent), so light and dark mode both follow the theme; Main hands the text to ShowSearch.
    [Fact]
    public void TopBar_SearchPopover_ThemeColoursOnly()
    {
        string root = RepoFiles.Root();
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css")).ReplaceLineEndings("\n");
        string main = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "Main.razor"));

        string backdrop = CssBlock(css, ".search-pop-backdrop {");
        Assert.Contains("position: fixed;", backdrop);
        Assert.Contains("inset: 0;", backdrop);
        Assert.Contains("background: transparent;", backdrop);
        string pop = CssBlock(css, ".search-pop {");
        Assert.Contains("top: 100%;", pop);
        Assert.Contains("background: var(--panel);", pop);
        Assert.Contains("border: 1px solid var(--line);", pop);
        Assert.Contains(".search-pop input {", css, StringComparison.Ordinal);
        foreach (string block in new[] { backdrop, pop, CssBlock(css, ".search-pop input {") })
        {
            foreach (Match m in Regex.Matches(block, @"(?m)^\s*(color|background|background-color|border|border-color)\s*:\s*([^;]*);"))
            {
                string value = m.Groups[2].Value;
                string colour = Regex.Replace(value, @"^\d+px\s+solid\s+", string.Empty);
                Assert.True(colour == "transparent" || Regex.IsMatch(colour, @"^var\(--[a-z-]+\)$"), m.Value);
            }
        }

        Assert.Contains("OnSearch=\"ShowSearch\"", main[..main.IndexOf("@code", StringComparison.Ordinal)][main.IndexOf("<TopBar", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    private static string CssRule(string css, string selector)
    {
        List<string> bodies = CssRules(css).Where(r => Regex.Replace(r.Selector, @"\s+", " ") == selector).Select(r => r.Body).ToList();
        Assert.True(bodies.Count == 1, selector + " should be one rule, found " + bodies.Count);
        return bodies[0];
    }

    // Declarations of a rule body as name -> value.
    private static Dictionary<string, string> CssDeclarations(string body)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (string part in body.Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon > 0)
            {
                map[part[..colon].Trim()] = part[(colon + 1)..].Trim();
            }
        }

        return map;
    }

    // 226 AC-1: Pick a day has its own rule, the same popover box as before plus width: max-content.
    [Fact]
    public void DayPick_OwnRule_MaxContentWidth()
    {
        Dictionary<string, string> d = CssDeclarations(CssRule(AppCss(), ".day-pick"));
        Assert.Equal("max-content", d["width"]);
        Assert.Equal("var(--panel)", d["background"]);
        Assert.Equal("1px solid var(--line)", d["border"]);
        Assert.Equal("8px", d["padding"]);
        Assert.Equal("absolute", d["position"]);
        Assert.Equal("8px", d["right"]);
        Assert.Equal("100%", d["top"]);
        Assert.Equal("3", d["z-index"]);
    }

    // 226 AC-2: Capture keeps its popover box with no width, and its input rule is unchanged.
    [Fact]
    public void CapturePop_Unchanged_NoWidth()
    {
        string css = AppCss();
        Dictionary<string, string> d = CssDeclarations(CssRule(css, ".capture-pop"));
        Assert.Equal("var(--panel)", d["background"]);
        Assert.Equal("1px solid var(--line)", d["border"]);
        Assert.Equal("8px", d["padding"]);
        Assert.Equal("absolute", d["position"]);
        Assert.Equal("8px", d["right"]);
        Assert.Equal("100%", d["top"]);
        Assert.Equal("3", d["z-index"]);
        Assert.False(d.ContainsKey("width"));
        Dictionary<string, string> input = CssDeclarations(CssRule(css, ".capture-pop input"));
        Assert.Equal("320px", input["width"]);
        Assert.Equal("calc(100vw - 48px)", input["max-width"]);
    }

    // 226 AC-3: day cells are 1.8em wide with 3px 0 padding; no Pick a day rule keeps 2.2em.
    [Fact]
    public void DayPick_Day_NarrowWidth()
    {
        string css = AppCss();
        Dictionary<string, string> d = CssDeclarations(CssRule(css, ".day-pick .day"));
        Assert.Equal("1.8em", d["width"]);
        Assert.Equal("3px 0", d["padding"]);
        Assert.False(d.ContainsKey("min-width"));
        foreach ((string selector, string body) in CssRules(css).Where(r => r.Selector.StartsWith(".day-pick", StringComparison.Ordinal)))
        {
            Assert.False(body.Contains("2.2em", StringComparison.Ordinal), selector);
        }
    }

    // 226 AC-4: grid cells have no padding; the week column keeps its line with 4px right padding.
    [Fact]
    public void DayPick_GridPadding_Tight()
    {
        string css = AppCss();
        Assert.Equal("0", CssDeclarations(CssRule(css, ".day-pick .day-pick-grid td"))["padding"]);
        Dictionary<string, string> week = CssDeclarations(CssRule(css, ".day-pick .day-pick-grid th.week"));
        Assert.Equal("4px", week["padding-right"]);
        Assert.Equal("1px solid var(--line)", week["border-right"]);
    }

    // 226 AC-5: every Pick a day rule uses theme colours only, and the focus ring stays.
    [Fact]
    public void DayPick_ThemeColoursOnly()
    {
        string css = AppCss();
        List<(string Selector, string Body)> rules = CssRules(css).Where(r => r.Selector.StartsWith(".day-pick", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(rules);
        foreach ((string selector, string body) in rules)
        {
            Assert.False(Regex.IsMatch(body, @"#[0-9a-fA-F]|rgb\(|hsl\(", RegexOptions.IgnoreCase), selector);
            foreach ((string name, string value) in CssDeclarations(body))
            {
                if (name != "color" && name != "background" && !name.StartsWith("border", StringComparison.Ordinal)
                    && name != "box-shadow" && name != "outline")
                {
                    continue;
                }

                // Border properties that take no colour (collapse, radius, width, style, spacing) are not checked.
                if (Regex.IsMatch(name, @"^border(-[a-z]+)*-(collapse|radius|width|style|spacing)$"))
                {
                    continue;
                }

                foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    bool skip = Regex.IsMatch(token, @"^-?\d*\.?\d+([a-z]+|%)?$") || token == "solid" || token == "inset";
                    bool colour = Regex.IsMatch(token, @"^var\(--[a-z0-9-]+\)$") || token == "transparent" || token == "none";
                    Assert.True(skip || colour, selector + " " + name + ": " + value);
                }
            }
        }

        Assert.Equal("2px solid var(--accent-strong)", CssDeclarations(CssRule(css, ".day-pick .day:focus-visible"))["outline"]);
    }

    private static string CssBlock(string css, string head)
    {
        int at = css.IndexOf(head, StringComparison.Ordinal);
        Assert.True(at >= 0, head);
        string block = css[at..];
        return block[..block.IndexOf('}')];
    }

    // 212 AC-8: the read-only day view is gone; no desktop source names it.
    [Fact]
    public void NoDayView_NoShownDay()
    {
        string root = RepoFiles.Root();
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor", ".cs"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            Assert.False(text.Contains("DayView", StringComparison.Ordinal), file + " mentions DayView");
            Assert.False(text.Contains("shownDay", StringComparison.Ordinal), file + " mentions shownDay");
        }
    }

    // 216 AC-5: the dashboard is gone: no desktop source names CurrentPanel, and its file and styles are removed.
    [Fact]
    public void NoCurrentPanel()
    {
        string root = RepoFiles.Root();
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor", ".cs", ".css", ".html"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            Assert.False(text.Contains("CurrentPanel", StringComparison.Ordinal), file + " mentions CurrentPanel");
        }

        Assert.False(File.Exists(Path.Combine(root, RepoFiles.DesktopFolder, "CurrentPanel.razor")));
        Assert.Null(typeof(Main).Assembly.GetType("Context.CurrentPanel"));
        string css = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        Assert.DoesNotMatch(@"(^|[\s,}])\.current\b(?!-)", css);
    }

    // 216 AC-11: the start offer is made only by the actions, and no lifecycle method of Main calls the actions.
    [Fact]
    public void Main_TakesTheStartNoteWithoutCallingActionsInLifecycle()
    {
        string root = RepoFiles.Root();
        string main = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "Main.razor")).ReplaceLineEndings("\n");
        Assert.Contains("State.TakeStartNote()", main, StringComparison.Ordinal);
        Assert.DoesNotContain("OfferStartNote", main, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoOpenToday", main, StringComparison.Ordinal);
    }

    // 224 AC-5: the editor source registers the contextmenu listener and the committed bundle was rebuilt with it.
    [Fact]
    public void Editor_RegistersContextMenuListener()
    {
        string root = RepoFiles.Root();
        string index = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "editor", "src", "index.js"));
        string bundle = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.js"));

        Assert.Contains("globalThis.addEventListener(\"contextmenu\"", index, StringComparison.Ordinal);
        Assert.Contains("contextmenu", bundle, StringComparison.Ordinal);
    }

    // 224 AC-6: dev tools only in Debug, set only through SetDevToolsEnabled(DevTools); no source names the platform
    // context-menu switch, in code or comments.
    [Fact]
    public void Program_DevToolsOnlyInDebug_NoContextMenuSwitch()
    {
        string root = RepoFiles.Root();
        string program = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "Program.cs")).ReplaceLineEndings("\n");

        Assert.Matches(@"#if DEBUG\s*\n\s*const bool DevTools = true;\s*\n\s*#else\s*\n\s*const bool DevTools = false;\s*\n\s*#endif", program);
        Assert.Single(Regex.Matches(program, @"SetDevToolsEnabled\s*\("));
        Assert.Contains(".SetDevToolsEnabled(DevTools)", program, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(program, @"const bool DevTools = true;"));

        IEnumerable<string> files = Directory.EnumerateDirectories(Path.Combine(root, "src"))
            .SelectMany(d => RepoFiles.Sources(root, RepoFiles.Relative(root, d), ".cs", ".razor", ".js", ".mjs", ".html", ".css", ".csproj", ".json"));
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            Assert.False(text.Contains("ContextMenuEnabled", StringComparison.Ordinal), file + " names the context-menu switch");
        }
    }

    // ---- 329: global todos and note checkboxes ----

    // 329 AC-7: both line backgrounds are defined in app.css's light and dark blocks, and in each theme the text and
    // the grey (done) text have a contrast of at least 4.5:1 on each of them.
    [Fact]
    public void TaskLineColours_BothThemes_Readable()
    {
        string app = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string light = ThemeBlock(app, ":root {");
        string dark = ThemeBlock(app, "@media (prefers-color-scheme: dark) {");
        string chosenDark = ThemeBlock(app, ":root[data-theme=\"dark\"] {");

        foreach (string theme in new[] { light, dark, chosenDark })
        {
            foreach (string background in new[] { "--task-local-bg", "--task-global-bg" })
            {
                foreach (string text in new[] { "--text", "--grey" })
                {
                    double ratio = Contrast(Colour(theme, text), Colour(theme, background));
                    Assert.True(ratio >= 4.5, $"{text} on {background}: {ratio:F2}");
                }
            }
        }

        Assert.NotEqual(Colour(light, "--task-local-bg"), Colour(light, "--task-global-bg"));
        Assert.NotEqual(Colour(dark, "--task-local-bg"), Colour(dark, "--task-global-bg"));
    }

    // 329 AC-7: editor.css paints the whole li line by its kind and draws the todo marker; view.js names each box's
    // kind and adds the marker (aria-hidden) to a global item only; the committed bundle has both.
    [Fact]
    public void TaskItems_ClassesLabelsAndMarker()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string editorCss = File.ReadAllText(Path.Combine(desktop, "editor", "src", "editor.css"));
        string view = File.ReadAllText(Path.Combine(desktop, "editor", "src", "view.js"));
        string schema = File.ReadAllText(Path.Combine(desktop, "editor", "src", "schema.js"));
        string bundle = File.ReadAllText(Path.Combine(desktop, "wwwroot", "editor", "editor.js"));
        string builtCss = File.ReadAllText(Path.Combine(desktop, "wwwroot", "editor", "editor.css"));

        Assert.Matches(@"\.note-editor-body li\.task\.local \{[^}]*background: var\(--task-local-bg\)", editorCss);
        Assert.Matches(@"\.note-editor-body li\.task\.global \{[^}]*background: var\(--task-global-bg\)", editorCss);
        Assert.Matches(@"\.note-editor-body \.task-marker \{[^}]*background: var\(--accent-strong\)", editorCss);
        Assert.Contains(editorCss, builtCss, StringComparison.Ordinal);
        Assert.Contains("\"task \" + kind + (node.attrs.checked ? \" done\" : \"\")", schema, StringComparison.Ordinal);
        Assert.Contains("box.setAttribute(\"aria-label\", global ? \"Todo\" : node.attrs.kind === \"carry\" ? \"Next meeting\" : \"Checkbox\");", view, StringComparison.Ordinal);
        Assert.Matches(
            @"if \(global\) \{\s*const marker = document\.createElement\(""span""\);\s*marker\.className = ""task-marker"";\s*marker\.setAttribute\(""aria-hidden"", ""true""\);",
            view);
        foreach (string needle in new[] { "task-marker", "\"Todo\"", "\"Checkbox\"", "toggledMarkdown" })
        {
            Assert.Contains(needle, bundle, StringComparison.Ordinal);
        }
    }

    // ---- 330: carry a line to the next meeting ----

    // 330 AC-10: the box handles contextmenu (preventDefault and toggleCarry only for an open local item), a carried
    // box is named "Next meeting", every open local box names the right click and the key, editor.css draws the arrow,
    // and the committed bundle and stylesheet have it all.
    [Fact]
    public void CarryBox_ContextMenuLabelsKeyAndArrow()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string editorCss = File.ReadAllText(Path.Combine(desktop, "editor", "src", "editor.css"));
        string view = File.ReadAllText(Path.Combine(desktop, "editor", "src", "view.js"));
        string bundle = File.ReadAllText(Path.Combine(desktop, "wwwroot", "editor", "editor.js"));
        string builtCss = File.ReadAllText(Path.Combine(desktop, "wwwroot", "editor", "editor.css"));

        Assert.Matches(
            @"box\.addEventListener\(""contextmenu"", event => \{\s*const pos = getPos\(\);\s*if \(typeof pos !== ""number"" \|\| !carriable\(node\)\) \{\s*return;\s*\}\s*event\.preventDefault\(\);\s*const tr = toggleCarry\(view\.state, pos\);",
            view);
        Assert.Matches(
            @"function carriable\(node\) \{\s*return node\.type === schema\.nodes\.list_item && node\.attrs\.checked === false\s*&& \(node\.attrs\.kind === ""local"" \|\| node\.attrs\.kind === ""carry""\);",
            view);
        Assert.Contains("node.attrs.kind === \"carry\" ? \"Next meeting\" : \"Checkbox\"", view, StringComparison.Ordinal);
        Assert.Matches(
            @"if \(carriable\(node\)\) \{[^}]*box\.setAttribute\(""aria-keyshortcuts"", carryKeyAria\);\s*box\.title = ""Right-click or "" \+ carryKeyTitle",
            view);
        Assert.Contains("const carryKeyTitle = onMac ? \"Cmd+Option+N\" : \"Ctrl+Alt+N\";", view, StringComparison.Ordinal);
        Assert.Contains("const carryKeyAria = onMac ? \"Meta+Alt+N\" : \"Control+Alt+N\";", view, StringComparison.Ordinal);
        Assert.Contains("\"Mod-Alt-n\": carryCommand,", view, StringComparison.Ordinal);
        Assert.Matches(@"\.note-editor-body li\.task\.carry > \.task-box::before \{[^}]*content: ", editorCss);
        Assert.Contains(editorCss, builtCss, StringComparison.Ordinal);
        foreach (string needle in new[] { "Next meeting", "carriedMarkdown", "aria-keyshortcuts", "contextmenu" })
        {
            Assert.Contains(needle, bundle, StringComparison.Ordinal);
        }
    }

    // 330 AC-11: the /todo help text and the README's Meeting notes and in-note todos sections name "- [>]", the right
    // click and the key.
    [Fact]
    public void CarryMarker_PaletteAndReadme_NameMarkerClickAndKey()
    {
        string root = RepoFiles.Root();
        string palette = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "CommandPalette.razor"));
        string todo = palette.Split('\n').Single(l => l.Contains("new PaletteCommand(\"/todo\"", StringComparison.Ordinal));
        foreach (string needle in new[] { "- [>]", "right-click", "Ctrl/Cmd+Alt+N" })
        {
            Assert.Contains(needle, todo, StringComparison.Ordinal);
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md")).Replace("\r", string.Empty, StringComparison.Ordinal);
        foreach (string heading in new[] { "### Meeting notes\n", "### Todos inside notes\n" })
        {
            int start = readme.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(start >= 0, heading);
            int end = readme.IndexOf("\n#", start + heading.Length, StringComparison.Ordinal);
            string section = readme[start..(end < 0 ? readme.Length : end)];
            foreach (string needle in new[] { "- [>]", "right-click", "Ctrl+Alt+N", "Cmd+Option+N" })
            {
                Assert.Contains(needle, section, StringComparison.Ordinal);
            }
        }
    }

    // 329: Program.cs converts the todo markers once, before todos.Migrate(); only DesktopActions runs the conversion.
    [Fact]
    public void TodoMarkerConversion_RunsOnceAtStart_OnlyThroughTheActions()
    {
        string root = RepoFiles.Root();
        string program = File.ReadAllText(Path.Combine(root, ProgramFile));

        int convert = program.IndexOf("actions.ConvertTodoMarkers()", StringComparison.Ordinal);
        Assert.True(convert >= 0);
        Assert.Equal(convert, program.LastIndexOf("actions.ConvertTodoMarkers()", StringComparison.Ordinal));
        Assert.True(convert < program.IndexOf("todos.Migrate();", StringComparison.Ordinal));
        List<string> callers = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("TodoMarkerConversion.Run(", StringComparison.Ordinal))
            .ToList();
        Assert.Equal([ActionsFile], callers);
    }

    // ---- 341: the theme setting ----

    // 341 AC-7: data-theme="light" sets color-scheme light and keeps the light variables (no variable of its own),
    // data-theme="dark" sets color-scheme dark and exactly the media block's dark variables, the media block applies
    // only without data-theme="light", and app.css is the only stylesheet the page and editor use that queries the
    // system scheme.
    [Fact]
    public void Css_ThemeBlocks_FollowDataTheme()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string app = File.ReadAllText(Path.Combine(desktop, "wwwroot", "app.css"));
        string light = ThemeBlock(app, ":root {");
        string media = ThemeBlock(app, "@media (prefers-color-scheme: dark) {");
        string chosenLight = ThemeBlock(app, ":root[data-theme=\"light\"] {");
        string chosenDark = ThemeBlock(app, ":root[data-theme=\"dark\"] {");

        Assert.Contains("color-scheme: light dark;", light, StringComparison.Ordinal);
        Assert.Contains(":root:not([data-theme=\"light\"]) {", media, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(app, @"@media \(prefers-color-scheme"));
        Assert.Single(Regex.Matches(app, @":root\[data-theme=""light""\] \{"));
        Assert.Single(Regex.Matches(app, @":root\[data-theme=""dark""\] \{"));
        Assert.Matches(@"\{\s*color-scheme: light;\s*\}", chosenLight);
        Assert.Empty(Variables(chosenLight));
        Assert.Contains("color-scheme: dark;", chosenDark, StringComparison.Ordinal);

        Dictionary<string, string> mediaVars = Variables(media);
        Dictionary<string, string> darkVars = Variables(chosenDark);
        Assert.True(mediaVars.Count >= 20, string.Join(", ", mediaVars.Keys));
        Assert.Equal(mediaVars.OrderBy(p => p.Key, StringComparer.Ordinal), darkVars.OrderBy(p => p.Key, StringComparer.Ordinal));
        foreach (string name in darkVars.Keys)
        {
            Assert.Contains(name + ":", light, StringComparison.Ordinal);
        }

        // Text and grey keep 4.5:1 on the background and the panel in every block.
        foreach (string theme in new[] { light, media, chosenDark })
        {
            foreach (string background in new[] { "--background", "--panel" })
            {
                foreach (string text in new[] { "--text", "--grey" })
                {
                    double ratio = Contrast(Colour(theme, text), Colour(theme, background));
                    Assert.True(ratio >= 4.5, $"{text} on {background}: {ratio:F2}");
                }
            }
        }

        // Only .css files are scanned: theme.js needs the query to pick Mermaid's theme (AC-8).
        List<string> querying = Directory.EnumerateFiles(Path.Combine(desktop, "wwwroot"), "*.css", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(Path.Combine(desktop, "wwwroot"), f).Replace('\\', '/').StartsWith("commandbar/", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(Path.Combine(desktop, "editor", "src"), "*.css", SearchOption.AllDirectories))
            .Where(f => File.ReadAllText(f).Contains("prefers-color-scheme", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(desktop, f).Replace('\\', '/'))
            .ToList();
        Assert.Equal(["wwwroot/app.css"], querying);
    }

    // 341 (review round 1): the theme buttons sit in a row with a gap, the pressed one is filled with --accent-strong
    // and --on-accent text (4.5:1 in every theme block), and the empty result line takes no room.
    [Fact]
    public void Css_ThemeChoice_RowAndVisiblePressedState()
    {
        string app = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Matches(@"\.settings-panel \.theme-choice \{[^}]*display: flex;[^}]*gap: 6px;", app);
        string pressed = ThemeBlock(app, ".settings-panel .theme-choice button[aria-pressed=\"true\"] {");
        Assert.Contains("background: var(--accent-strong);", pressed, StringComparison.Ordinal);
        Assert.Contains("border-color: var(--accent-strong);", pressed, StringComparison.Ordinal);
        Assert.Contains("color: var(--on-accent);", pressed, StringComparison.Ordinal);
        Assert.Matches(@"\.settings-panel \.theme-result:empty \{\s*display: none;\s*\}", app);

        string light = ThemeBlock(app, ":root {");
        foreach (string theme in new[] { light, ThemeBlock(app, "@media (prefers-color-scheme: dark) {"), ThemeBlock(app, ":root[data-theme=\"dark\"] {") })
        {
            // Every block sets both --accent-strong and --on-accent.
            double ratio = Contrast(Colour(theme, "--on-accent"), Colour(theme, "--accent-strong"));
            Assert.True(ratio >= 4.5, $"--on-accent on --accent-strong: {ratio:F2}");
        }
    }

    // 341: text fields take the root's colour scheme, so a chosen theme reaches their native parts too.
    [Fact]
    public void Css_TextFields_InheritColourScheme()
    {
        string app = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Single(Regex.Matches(app, "color-scheme: light dark;"));
        Assert.Matches(@"input\[type=""password""\] \{[^}]*color-scheme: inherit;", app);
    }

    // The custom properties (--name: value) set directly in a block, by name.
    private static Dictionary<string, string> Variables(string block) =>
        Regex.Matches(block, @"(--[a-z0-9-]+):\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);

    // The text of the first block that opens with start, up to its matching brace (nested blocks included).
    private static string ThemeBlock(string css, string start)
    {
        int at = css.IndexOf(start, StringComparison.Ordinal);
        Assert.True(at >= 0, start);
        int depth = 0;
        for (int i = at; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return css[at..(i + 1)];
                }
            }
        }

        return css[at..];
    }

    // The #rrggbb value of the custom property name in block.
    private static (double R, double G, double B) Colour(string block, string name)
    {
        Match match = Regex.Match(block, Regex.Escape(name) + @":\s*#([0-9a-fA-F]{6});");
        Assert.True(match.Success, name);
        string hex = match.Groups[1].Value;
        double Channel(int i) => Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
        return (Channel(0), Channel(2), Channel(4));
    }

    // The WCAG 2 contrast ratio of two sRGB colours.
    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double Luminance((double R, double G, double B) c) =>
            (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
