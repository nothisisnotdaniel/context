using Context;
using Spectre.Console;

CommandRegistry registry = CommandRegistry.CreateDefault();
NoteCommands.Register(registry, new NoteStore(NoteStore.DefaultFolder()));
return new App(AnsiConsole.Console, registry).Run();
