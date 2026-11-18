using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Photino.NET;
using Velopack;

namespace Context;

/// <summary>
/// Wires up the desktop window; reads live in <see cref="DesktopState"/>, changes in <see cref="DesktopActions"/>, the
/// page's editor is reached only through <see cref="EditorInterop"/>, a newer build through <see cref="StartupUpdate"/>,
/// the OS default editor through <see cref="ShellNoteLauncher"/>, system notifications through
/// <see cref="SystemNotifier"/>, the OS key store for sensitive notes through
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

        // Classic Outlook's COM interface, read-only; other systems have none.
        static IOutlookSource Outlook()
        {
            if (OperatingSystem.IsWindows())
            {
                return new OutlookComSource();
            }

            return new UnsupportedOutlookSource();
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
            notes, todos, index, calendar, feed, Today, () => DateTime.UtcNow, KeyStore(), session, calendarLink, Outlook());
        DesktopActions actions = new DesktopActions(
            notes, todos, state, new ShellNoteLauncher(), calendarLink, feed, () => DateTime.Now, calendar,
            TimesFolder.Resolve(Environment.GetEnvironmentVariable(TimesFolder.VariableName)), new SystemNotifier());

        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(actions);
        builder.Services.AddTransient<EditorInterop>();
        builder.Services.AddSingleton<IAutoSavePause>(new AutoSavePause());
        // Filled in once the window exists; the guard only calls it after a save-first finished.
        PhotinoWindow? window = null;
        CloseGuard closeGuard = new CloseGuard(() => window?.Invoke(() => window.Close()));
        builder.Services.AddSingleton(closeGuard);
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
        window = app.MainWindow;

        // Closing with unsaved changes cancels the close and saves first (CloseGuard; the save runs asynchronously, so
        // this handler never waits on the Blazor dispatcher); after the save, or Discard, the guard closes the window
        // again. An open sensitive note is locked when the window does close and after five minutes without use.
        app.MainWindow.RegisterWindowClosingHandler((_, _) =>
        {
            if (closeGuard.Closing())
            {
                return true;
            }

            session.Lock();
            return false;
        });
        using Timer idle = new Timer(_ => session.Tick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        // Todos stored without a usable id get their derived ids written once, before the watcher and the first
        // refresh; an unreadable file is left alone and shows as unreadable.
        todos.Migrate();

        // The two automatic writes, always through DesktopActions. Once now, then on their own timer (never from
        // DesktopState, the watcher or Tick): the day's end (Times' worked hours into the daily note at a leaving, and
        // the due todos moved once a day, see DayEnd.AutoClose), and once it has finished, the morning briefing
        // (written once a day at the first Times arrival or a weekday start, see Briefing.ShouldCreate).
        static Task AutoWrites(DesktopActions actions) =>
            actions.AutoDayEnd().ContinueWith(_ => actions.AutoBriefing(), TaskScheduler.Default).Unwrap();

        // The timed-todo reminders, also through DesktopActions: once now, before the automatic writes (so a todo whose
        // time passed while the app was closed shows at start), then on their own timer, half an interval after the
        // day-end timer so the two rarely meet (a call refused as busy is retried on the next tick). Between them, once
        // at start only, today's daily note is made sure of (created when missing) and offered to Main, which opens it
        // while nothing else is shown.
        _ = actions.AutoReminders()
            .ContinueWith(_ => actions.AutoOpenToday(), TaskScheduler.Default).Unwrap()
            .ContinueWith(_ => AutoWrites(actions), TaskScheduler.Default).Unwrap();
        using Timer dayEnd = new Timer(
            _ => _ = AutoWrites(actions), null, DesktopActions.DayEndInterval, DesktopActions.DayEndInterval);
        using Timer reminders = new Timer(
            _ => _ = actions.AutoReminders(), null, DesktopActions.ReminderInterval / 2, DesktopActions.ReminderInterval);

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
