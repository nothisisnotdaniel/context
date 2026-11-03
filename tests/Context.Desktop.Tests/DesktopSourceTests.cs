using System.Text.RegularExpressions;

namespace Context.Tests;

// Source and file scans of Context.Desktop: raw markup, risky elements, the page's CSP and the guarded write path.
public sealed class DesktopSourceTests
{
    private const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; base-uri 'none'; form-action 'none'";

    // The Core calls that change data (AC-8). CalendarLink's real names are Set/Clear; Save/Delete are listed too.
    private static readonly Regex Writes = new(
        @"\.\s*(Create|Save|Add|Complete|Set|Clear|Delete|ClearCache|Migrate|DecryptAll)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly string[] RawMarkup = ["MarkupString", "AddMarkupContent", "@(("];

    private static readonly string[] RazorForbidden = ["style=", "<a ", "<form", "<script"];

    // The only Desktop file allowed to call the writing methods.
    private const string ActionsFile = "src/Context.Desktop/DesktopActions.cs";

    // The .cs files that may name DesktopActions (besides DesktopActions.cs itself).
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

    // AC-6: exactly the CSP meta tag, one external script (blazor.webview.js), no inline script or style.
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
        Match script = Assert.Single(scripts);
        Assert.Equal(" src=\"_framework/blazor.webview.js\"", script.Groups[1].Value);
        Assert.Equal(string.Empty, script.Groups[2].Value);
        Assert.Single(Regex.Matches(html, "<script", RegexOptions.IgnoreCase));

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("\\son[a-z]+\\s*=", html);
        Assert.DoesNotContain("http:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https:", html, StringComparison.OrdinalIgnoreCase);
    }

    // AC-6: the stylesheet loads nothing from outside (no CDN, no external fonts).
    [Fact]
    public void AppCss_LoadsNothingExternal()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@font-face", css, StringComparison.OrdinalIgnoreCase);
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

        Assert.True(buttons >= 3, "Expected the Done, confirm and Cancel buttons, found " + buttons);
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

    // AC-10: no Desktop source uses JavaScript interop directly.
    [Fact]
    public void Sources_MentionNoJSRuntime()
    {
        string root = RepoFiles.Root();
        List<string> found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor")
            .Where(f => MentionsJSRuntime(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.True(found.Count == 0, "JSRuntime mentioned in: " + string.Join(", ", found));
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
    [InlineData("link.Set(value)")]
    [InlineData("link.Clear()")]
    [InlineData("link.Save(value)")]
    [InlineData("link.Delete()")]
    [InlineData("feed.ClearCache()")]
    [InlineData("encryption.Migrate()")]
    [InlineData("encryption.DecryptAll()")]
    [InlineData("Vault.Create(root, pass)")]
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
    public void Writes_Pattern_AllowsReads(string call)
    {
        Assert.DoesNotMatch(Writes, call);
    }

    // AC-8 (control): the DesktopActions name check.
    [Theory]
    [InlineData("private readonly DesktopActions actions;", true)]
    [InlineData("new DesktopActions(encryption, todos, this)", true)]
    [InlineData("<see cref=\"DesktopActions\"/>", true)]
    [InlineData("DesktopActionsTests", false)]
    [InlineData("internal DesktopState State", false)]
    public void ActionsName_Pattern(string source, bool mentions)
    {
        Assert.Equal(mentions, ActionsName.IsMatch(source));
    }

    private static bool MentionsJSRuntime(string source) => source.Contains("JSRuntime", StringComparison.Ordinal);

    private static HashSet<string> Components(string root) =>
        RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.Ordinal);

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
