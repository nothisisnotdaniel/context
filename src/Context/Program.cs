using Context;
using Spectre.Console;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

CommandRegistry registry = CommandRegistry.CreateDefault();
string folder = NoteStore.DefaultFolder();
NoteStore notes = new NoteStore(folder);
NoteCommands.Register(registry, notes);
TagIndex index = new TagIndex(folder);
TagCommands.Register(registry, index, notes);
TodoStore todos = new TodoStore(folder);
TodoCommands.Register(registry, todos, Today);
CalendarStore calendar = new CalendarStore(folder, TimeZoneInfo.Local);
CurrentCommands.Register(registry, todos, index, notes, calendar, Today);
return new App(AnsiConsole.Console, registry, new TodoSidebar(todos, Today)).Run();
