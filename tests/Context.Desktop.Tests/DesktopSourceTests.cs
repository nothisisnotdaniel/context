using System.Text.RegularExpressions;

namespace Context.Tests;

// Source and file scans of Context.Desktop: raw markup, risky elements, the page's CSP and the guarded write path.
public sealed class DesktopSourceTests
{
    private const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; base-uri 'none'; form-action 'none'";

    // The Core calls that change data (AC-8). CalendarLink's real names are Set/Clear; Save/Delete are listed too.
    // 134: NoteStore.Rename and NoteStore.Delete.
    private static readonly Regex Writes = new(
        @"\.\s*(Create|Save|SaveIfUnchanged|Add|Complete|Set|Clear|Delete|Rename|ClearCache)\s*\(",
        RegexOptions.CultureInvariant);

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

    // Spelled in pieces: no test file may hold the real source's name (see Tests_NeverReferenceTheRealUpdateSource).
    private const string RealUpdateSource = "Velopack" + "UpdateSource";
    private const string RealUpdateSourceFile = "src/Context.Desktop/" + RealUpdateSource + ".cs";

    // 134: spelled in pieces too: no test file may hold the real launcher's name.
    private const string RealLauncher = "Shell" + "NoteLauncher";
    private const string RealLauncherFile = "src/Context.Desktop/" + RealLauncher + ".cs";

    private static readonly string[] UpdateFiles =
    [
        "src/Context.Desktop/IUpdateSource.cs", "src/Context.Desktop/StartupUpdate.cs", RealUpdateSourceFile,
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
        Assert.Contains("src/Context.Desktop/CurrentPanel.razor", files);

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

    // AC-6 / 094 AC-12: exactly the CSP meta tag, two external scripts (the editor bundle and blazor.webview.js), no
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
            [" src=\"editor/editor.js\"", " src=\"_framework/blazor.webview.js\""],
            scripts.Select(m => m.Groups[1].Value).ToArray());
        Assert.All(scripts, m => Assert.Equal(string.Empty, m.Groups[2].Value));
        Assert.Equal(2, Regex.Matches(html, "<script", RegexOptions.IgnoreCase).Count);
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
    [InlineData(".editor-host:focus-within")]
    [InlineData(".note-entry {")]
    [InlineData(".note-toolbar {")]
    public void AppCss_StylesEditingComponents(string selector)
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains(selector, css, StringComparison.Ordinal);
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
        Assert.All(selectors, sel => Assert.True(
            sel.StartsWith(".note-editor-body", StringComparison.Ordinal) || sel.Contains("ProseMirror", StringComparison.Ordinal),
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

    // 080 round 2 / 094 AC-12: every <input> is a checkbox, text, date or search field, nothing binds a value with
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
        Assert.True(found.Count == 0, "Inputs other than checkbox, text, date or search: " + string.Join(", ", found));
    }

    // 080 round 2 / 094 (control): the input check.
    [Theory]
    [InlineData("<input type=\"checkbox\" checked=\"@x\" @onchange=\"() => Ask(t)\" />", 0)]
    [InlineData("<input class=\"done\" type=\"checkbox\">", 0)]
    [InlineData("<input type=\"text\" value=\"@x\" @oninput=\"Typed\" />", 0)]
    [InlineData("<input type=\"date\" @onchange=\"Chosen\" />", 0)]
    [InlineData("<input type=\"search\" />", 0)]
    [InlineData("<input>", 1)]
    [InlineData("<input type=\"password\" />", 1)]
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
    [Fact]
    public void JSInvokable_MethodsChangeNothing()
    {
        string text = File.ReadAllText(Path.Combine(RepoFiles.Root(), InteropFile));
        MatchCollection invokable = Regex.Matches(text, @"\[JSInvokable\]\s*public\s+Task\s+(\w+)\(string raw\)\s*=>\s*([^;]*);");

        Assert.Equal(["OnTag", "OnLink"], invokable.Select(m => m.Groups[1].Value).ToArray());
        Assert.Equal(2, Regex.Matches(text, @"\[JSInvokable").Count);
        Assert.All(invokable, m => Assert.Matches(@"^(TagClicked|LinkClicked)\?\.Invoke\(raw\) \?\? Task\.CompletedTask$", m.Groups[2].Value));
        Assert.DoesNotMatch(ActionsCall, text);
        Assert.DoesNotMatch(ActionsName, text);
    }

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
    public void Writes_Pattern_CatchesCalls(string call)
    {
        Assert.Matches(Writes, call);
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
    [InlineData("Actions.RenameNote(name, renameText)")]
    [InlineData("Actions.DeleteNote(name, confirmed)")]
    [InlineData("Actions.OpenExternally(name)")]
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

    // 134 AC-9: only the real launcher starts a process; Process and System.Diagnostics appear nowhere else in the app.
    [Fact]
    public void Sources_UseProcessOnlyInTheLauncher()
    {
        string root = RepoFiles.Root();
        Regex process = new Regex(@"\bProcess\b|System\.Diagnostics", RegexOptions.CultureInvariant);
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => process.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Equal([RealLauncherFile], found);
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
            "new DesktopActions(notes, todos, state, new " + RealLauncher + "())",
            File.ReadAllText(Path.Combine(root, ProgramFile)),
            StringComparison.Ordinal);
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

    private static bool MentionsJSRuntime(string source) => source.Contains("JSRuntime", StringComparison.Ordinal);

    private static HashSet<string> Components(string root) =>
        RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.Ordinal);

    // Every <input> that is not type="checkbox", "text", "date" or "search" or that uses @bind, and every <textarea>.
    private static List<string> BadInputs(string razor) =>
        Tag.Matches(MarkupOf(razor))
            .Where(m => m.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase)
                ? !Regex.IsMatch(m.Groups[2].Value, @"(?<![\w-])type\s*=\s*""(checkbox|text|date|search)""")
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
        foreach ((string name, string body) in methods.Where(m => ActionsCall.IsMatch(m.Body)))
        {
            Regex word = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
            if (Lifecycle.Any(l => name.StartsWith(l, StringComparison.Ordinal)))
            {
                problems.Add(name + " is a lifecycle method");
            }

            if (!bindings.Any(b => word.IsMatch(b)))
            {
                problems.Add(name + " is bound by no event attribute");
            }

            if (methods.Any(m => m.Name != name && word.IsMatch(m.Body)))
            {
                problems.Add(name + " is called by another method");
            }
        }

        return problems;
    }
}
