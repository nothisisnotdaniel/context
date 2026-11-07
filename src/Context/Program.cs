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
        Console.In,
        Environment.GetEnvironmentVariable,
        NoteStore.DefaultFolder(),
        Today,
        () => DateTime.Now,
        cliHttp).Run(args);
}

CommandRegistry registry = CommandRegistry.CreateDefault();
string folder = NoteStore.DefaultFolder();
Encryption encryption = new Encryption(folder);
NoteStore notes = new NoteStore(folder, encryption);
NoteCommands.Register(registry, notes, encryption);
using TagIndex index = new TagIndex(folder, encryption);
TagCommands.Register(registry, index, notes);
TodoStore todos = new TodoStore(folder, encryption: encryption);
TodoCommands.Register(registry, todos, Today);
CalendarStore calendar = new CalendarStore(folder, TimeZoneInfo.Local, encryption);
CalendarLink calendarLink = new CalendarLink(folder, encryption);
using SocketsHttpHandler http = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
CalendarFeed feed = new CalendarFeed(folder, calendarLink, http, () => DateTime.UtcNow, encryption: encryption);
CalendarLinkCommands.Register(registry, calendarLink, feed, TimeZoneInfo.Local);
CurrentCommands.Register(registry, todos, index, notes, calendar, Today, feed);
EncryptionCommands.Register(registry, encryption, new PassphrasePrompt(AnsiConsole.Console));
int exitCode = new App(AnsiConsole.Console, registry, new TodoSidebar(todos, Today), encryption).Run();
encryption.Use(null);
return exitCode;
