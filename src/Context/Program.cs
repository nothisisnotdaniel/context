using Context;

static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

// The installed Times command for meeting --book (job 381): the .NET tool's `tm` in ~/.dotnet/tools, else on PATH.
static string? TimesCommand() => TimesExecutable.Find(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment.GetEnvironmentVariable("PATH"),
    OperatingSystem.IsWindows(),
    File.Exists);

// One command, plain text, then exit: the scriptable command line. No arguments is a usage error.
Console.OutputEncoding = System.Text.Encoding.UTF8;
using SocketsHttpHandler http = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
return new Cli(
    Console.Out,
    Console.Error,
    NoteStore.DefaultFolder(),
    Today,
    () => DateTime.Now,
    http,
    TimesFolder.Resolve(Environment.GetEnvironmentVariable(TimesFolder.VariableName)),
    new ProcessDesktopLauncher(),
    new TimesProcessRunner(TimesCommand, TimeSpan.FromSeconds(10))).Run(args);
