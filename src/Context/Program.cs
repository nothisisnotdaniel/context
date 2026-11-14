using Context;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

// One command, plain text, then exit: the scriptable command line. No arguments is a usage error.
Console.OutputEncoding = System.Text.Encoding.UTF8;
using SocketsHttpHandler http = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
return new Cli(
    Console.Out,
    Console.Error,
    NoteStore.DefaultFolder(),
    Today,
    () => DateTime.Now,
    http).Run(args);
