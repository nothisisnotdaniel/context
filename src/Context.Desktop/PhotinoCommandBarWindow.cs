using Photino.NET;

namespace Context;

/// <summary>
/// The real <see cref="ICommandBarWindow"/> (job 282): a second Photino window, a child of the main one, 640x300,
/// chromeless, topmost, centred and not resizable. It serves only the three files of <c>wwwroot/commandbar</c>
/// through its own <c>ctxbar:</c> scheme (anything else is not found) and passes the page's messages to a new
/// <see cref="CommandBarHost"/> per bar. Every window call runs on the main window's thread.
/// </summary>
internal sealed class PhotinoCommandBarWindow : ICommandBarWindow
{
    private const string Scheme = "ctxbar";
    private const string StartPage = Scheme + "://localhost/commandbar.html";

    private static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["commandbar.html"] = "text/html; charset=utf-8",
        ["commandbar.js"] = "text/javascript; charset=utf-8",
        ["commandbar.css"] = "text/css; charset=utf-8",
    };

    private readonly PhotinoWindow main;
    private readonly Func<CommandBarHost> newHost;
    private readonly object gate = new object();
    private bool open;
    private PhotinoWindow? bar;
    private CommandBarHost? host;

    public PhotinoCommandBarWindow(PhotinoWindow main, Func<CommandBarHost> newHost)
    {
        this.main = main;
        this.newHost = newHost;
    }

    public bool IsOpen
    {
        get
        {
            lock (gate)
            {
                return open;
            }
        }
    }

    public void Show()
    {
        lock (gate)
        {
            if (open)
            {
                return;
            }

            // Claimed now, so a second press before the window exists focuses instead of opening another.
            open = true;
            host = newHost();
        }

        main.Invoke(Open);
    }

    public void Focus()
    {
        main.Invoke(() =>
        {
            PhotinoWindow? shown;
            CommandBarHost? shownHost;
            lock (gate)
            {
                shown = bar;
                shownHost = host;
            }

            if (shown is null || shownHost is null)
            {
                return;
            }

            shown.SetTopMost(true);
            shownHost.Focus();
        });
    }

    /// <summary>The file a <c>ctxbar:</c> URL names, or null for any other URL.</summary>
    internal static string? FileFor(string url)
    {
        string prefix = Scheme + "://localhost/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = url[prefix.Length..];
        int end = name.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            name = name[..end];
        }

        return Files.ContainsKey(name) ? name : null;
    }

    private void Open()
    {
        PhotinoWindow window;
        CommandBarHost? shownHost;
        try
        {
            window = new PhotinoWindow(main);
        }
        catch (Exception)
        {
            lock (gate)
            {
                open = false;
                host = null;
            }

            return;
        }

        lock (gate)
        {
            bar = window;
            shownHost = host;
        }

        if (shownHost is null)
        {
            return;
        }

        try
        {
            shownHost.Send += json => main.Invoke(() => window.SendWebMessage(json));
            shownHost.CloseRequested += () => main.Invoke(() => window.Close());
            window
                .SetTitle("Context command bar")
                .SetChromeless(true)
                .SetTopMost(true)
                .SetResizable(false)
                .SetSize(640, 300)
                .Center()
                .SetGrantBrowserPermissions(false)
                .SetDevToolsEnabled(false)
                .RegisterCustomSchemeHandler(Scheme, Serve)
                .RegisterWebMessageReceivedHandler((_, message) => _ = shownHost.Receive(message))
                .RegisterWindowClosingHandler((_, _) =>
                {
                    Closed(window);
                    return false;
                })
                .Load(new Uri(StartPage));

            // A child window is created here and the call returns: the main window's loop already runs.
            window.WaitForClose();
        }
        catch (Exception)
        {
            Closed(window);
        }
    }

    private void Closed(PhotinoWindow window)
    {
        lock (gate)
        {
            if (ReferenceEquals(bar, window))
            {
                bar = null;
                host = null;
                open = false;
            }
        }
    }

    private static Stream Serve(object sender, string scheme, string url, out string contentType)
    {
        string? name = FileFor(url);
        if (name is null)
        {
            contentType = "text/plain";
            return new MemoryStream();
        }

        contentType = Files[name];
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "commandbar", name);
            return new MemoryStream(BoundedFile.Read(path, 256 * 1024));
        }
        catch (Exception)
        {
            return new MemoryStream();
        }
    }
}
