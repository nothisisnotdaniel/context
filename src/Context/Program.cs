using Context;
using Spectre.Console;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

if (args.Length > 0)
{
    // One command, plain text, then exit: the scriptable command line.
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    using SocketsHttpHandler cliHttp = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
    return new Cli(
        Console.Out,
        Console.Error,
        NoteStore.DefaultFolder(),
        Today,
        () => DateTime.Now,
        cliHttp).Run(args);
}

CommandRegistry registry = CommandRegistry.CreateDefault();
string folder = NoteStore.DefaultFolder();
NoteStore notes = new NoteStore(folder);
NoteCommands.Register(registry, notes);
TagIndex index = new TagIndex(folder);
TagCommands.Register(registry, index, notes);
TodoStore todos = new TodoStore(folder);
TodoCommands.Register(registry, todos, Today);
CalendarStore calendar = new CalendarStore(folder, TimeZoneInfo.Local);
CalendarLink calendarLink = new CalendarLink(folder);
using SocketsHttpHandler http = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
CalendarFeed feed = new CalendarFeed(folder, calendarLink, http, () => DateTime.UtcNow);
CalendarLinkCommands.Register(registry, calendarLink, feed, TimeZoneInfo.Local);
CurrentCommands.Register(registry, todos, index, notes, calendar, Today, feed);
return new App(AnsiConsole.Console, registry, new TodoSidebar(todos, Today)).Run();
