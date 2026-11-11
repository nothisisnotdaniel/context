using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace Context;

/// <summary>
/// Wires up the desktop window; reads live in <see cref="DesktopState"/>, changes in <see cref="DesktopActions"/>, the
/// page's editor is reached only through <see cref="EditorInterop"/>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

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
        DesktopState state = new DesktopState(
            notes, todos, index, calendar, feed, Today, () => DateTime.UtcNow);
        DesktopActions actions = new DesktopActions(notes, todos, state);

        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(actions);
        builder.Services.AddTransient<EditorInterop>();
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

        using DataWatcher watcher = new DataWatcher(folder, state);
        watcher.Start();
        _ = state.Refresh();
        app.Run();
    }
}
