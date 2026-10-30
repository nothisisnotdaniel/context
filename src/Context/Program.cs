using Context;
using Spectre.Console;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

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
