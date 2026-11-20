namespace Context.Tests;

/// <summary>
/// The names of the removed interactive console (job 145). Each type name is spelled in pieces, so no test file
/// contains the words <c>RemovedFeatureSourceTests</c> scans for, and that scan needs no exception for any file.
/// </summary>
internal static class RemovedConsole
{
    /// <summary>The types that had a source file in <c>src/Context/</c> and a test class of their own.</summary>
    private static readonly string[] SourceTypes =
    [
        "Command" + "Popup",
        "Command" + "Registry",
        "Line" + "Editor",
        "Todo" + "Sidebar",
        "Note" + "Commands",
        "Todo" + "Commands",
        "Tag" + "Commands",
        "Current" + "Commands",
        "CalendarLink" + "Commands",
    ];

    /// <summary>The test helper that wired a console like <c>Program.cs</c> did.</summary>
    private static readonly string Session = "Console" + "Session";

    /// <summary>
    /// The type names no <c>.cs</c> or <c>.razor</c> file under <c>src/</c> or <c>tests/</c> may use as a whole
    /// word.
    /// </summary>
    public static readonly string[] TypeNames = [.. SourceTypes, "Command" + "Result", Session];

    /// <summary>The deleted files, relative to the repository root with <c>/</c>.</summary>
    public static readonly string[] Files =
    [
        "src/Context/App.cs",
        .. SourceTypes.Select(t => $"src/Context/{t}.cs"),
        "tests/Context.Tests/AppTests.cs",
        .. SourceTypes.Select(t => $"tests/Context.Tests/{t}Tests.cs"),
        "tests/Context.Tests/ViewParityTests.cs",
        $"tests/Context.Tests/{Session}.cs",
        "tests/Context.Tests/AnsiWrites.cs",
    ];

    /// <summary>What <c>Program.cs</c> may not contain: reading keys, the terminal library, the loop, registering.</summary>
    public static readonly string[] ProgramWords = ["ReadKey", "AnsiConsole", "App(", "Register("];

    /// <summary>What <c>CLAUDE.md</c> may not contain.</summary>
    public static readonly string[] ConventionWords =
    [
        "Spectre",
        "Line" + "Editor",
        "Command" + "Popup",
        "Command" + "Registry",
        "Todo" + "Sidebar",
        "TestConsole",
        "/quit",
        "interactive session",
    ];
}
