using Spectre.Console.Testing;

namespace Context.Tests;

/// <summary>
/// The console app with every command registered as in <c>Program.cs</c>, over a test console and a fake download.
/// <see cref="RemovedFeatureSourceTests"/> checks that <c>Program.cs</c> registers exactly <see cref="Registrations"/>.
/// </summary>
internal sealed class ConsoleSession
{
    public const int Width = 100;
    public const int Height = 24;

    public static readonly DateOnly Today = new(2026, 10, 29);
    public static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The <c>*Commands</c> classes registered here, in order.</summary>
    public static readonly string[] Registrations =
    [
        nameof(NoteCommands), nameof(TagCommands), nameof(TodoCommands), nameof(CalendarLinkCommands),
        nameof(CurrentCommands),
    ];

    private readonly TodoSidebar sidebar;

    public ConsoleSession(string root)
    {
        NoteStore notes = new NoteStore(root);
        TagIndex index = new TagIndex(root);
        TodoStore todos = new TodoStore(root, () => Now);
        CalendarStore calendar = new CalendarStore(root, TimeZoneInfo.Utc);
        CalendarLink link = new CalendarLink(root);
        CalendarFeed feed = new CalendarFeed(
            root, link, FakeHttpHandler.Ok(CalendarFeedTests.Ics), () => Now, TimeSpan.FromSeconds(5));
        Registry = CommandRegistry.CreateDefault();
        NoteCommands.Register(Registry, notes);
        TagCommands.Register(Registry, index, notes);
        TodoCommands.Register(Registry, todos, () => Today);
        CalendarLinkCommands.Register(Registry, link, feed, TimeZoneInfo.Utc);
        CurrentCommands.Register(Registry, todos, index, notes, calendar, () => Today, feed);
        Console = new TestConsole().Interactive().EmitAnsiSequences();
        Console.Profile.Width = Width;
        Console.Profile.Height = Height;
        sidebar = new TodoSidebar(todos, () => Today);
    }

    public CommandRegistry Registry { get; }

    public TestConsole Console { get; }

    /// <summary>Queues every line (each with Enter), then <c>/quit</c>, runs the app and returns its exit code.</summary>
    public int Run(params string[] lines)
    {
        foreach (string line in lines)
        {
            Console.Input.PushTextWithEnter(line);
        }

        Console.Input.PushTextWithEnter("/quit");
        return new App(Console, Registry, sidebar).Run();
    }
}
