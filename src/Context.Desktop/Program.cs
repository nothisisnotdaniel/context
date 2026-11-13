using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Velopack;

namespace Context;

/// <summary>
/// Wires up the desktop window; reads live in <see cref="DesktopState"/>, changes in <see cref="DesktopActions"/>, the
/// page's editor is reached only through <see cref="EditorInterop"/>, a newer build through <see cref="StartupUpdate"/>,
/// the OS default editor through <see cref="ShellNoteLauncher"/>, the OS key store for sensitive notes through
/// <see cref="IKeyStore"/>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

        // The OS key store for sensitive notes; other systems have none.
        static IKeyStore KeyStore()
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsKeyStore();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacKeyStore();
            }

            return new UnsupportedKeyStore();
        }

        string folder = NoteStore.DefaultFolder();
        NoteStore notes = new NoteStore(folder);
        TagIndex index = new TagIndex(folder);
        TodoStore todos = new TodoStore(folder);
        CalendarStore calendar = new CalendarStore(folder, TimeZoneInfo.Local);
        CalendarLink calendarLink = new CalendarLink(folder);
        using SocketsHttpHandler http = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
        CalendarFeed feed = new CalendarFeed(folder, calendarLink, http, () => DateTime.UtcNow);
        SensitiveSession session = new SensitiveSession(() => DateTime.UtcNow);
        DesktopState state = new DesktopState(
            notes, todos, index, calendar, feed, Today, () => DateTime.UtcNow, KeyStore(), session);
        DesktopActions actions = new DesktopActions(notes, todos, state, new ShellNoteLauncher());

        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(actions);
        builder.Services.AddTransient<EditorInterop>();
        PhotinoFileDialog dialog = new PhotinoFileDialog();
        builder.Services.AddSingleton<IFileDialog>(dialog);
        builder.RootComponents.Add<App>("app");
        PhotinoBlazorApp app = builder.Build();

#if DEBUG
        const bool DevTools = true;
#else
        const bool DevTools = false;
#endif
        app.MainWindow
            .SetTitle("Context")
            .SetSize(1100, 750)
            .SetGrantBrowserPermissions(false)
            .SetDevToolsEnabled(DevTools);
        dialog.Window = app.MainWindow;

        // An open sensitive note is locked when the window closes and after five minutes without use.
        app.MainWindow.RegisterWindowClosingHandler((_, _) =>
        {
            session.Lock();
            return false;
        });
        using Timer idle = new Timer(_ => session.Tick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        using DataWatcher watcher = new DataWatcher(folder, state);
        watcher.Start();
        _ = state.Refresh();

        // Installed Windows copies only: look for a newer build in the background; it is put in place after exit.
        StartupUpdate update = new StartupUpdate(
            new VelopackUpdateSource(StartupUpdate.IncludePrereleases(StartupUpdate.CurrentVersion)));
        _ = update.Start();
        app.Run();
    }
}
