using Context;
using Spectre.Console;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

CommandRegistry registry = CommandRegistry.CreateDefault();
string folder = NoteStore.DefaultFolder();
NoteStore notes = new NoteStore(folder);
NoteCommands.Register(registry, notes);
TagCommands.Register(registry, new TagIndex(folder), notes);
TodoStore todos = new TodoStore(folder);
TodoCommands.Register(registry, todos, Today);
return new App(AnsiConsole.Console, registry, new TodoSidebar(todos, Today)).Run();
