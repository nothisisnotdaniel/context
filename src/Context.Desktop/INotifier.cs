namespace Context;

/// <summary>
/// Shows one system notification. The desktop write path is the only caller (the automatic reminders, for timed todos
/// whose time has come); it passes a fixed title and a one-line body.
/// </summary>
internal interface INotifier
{
    /// <summary>Shows <paramref name="title"/> and <paramref name="body"/> as one notification. May throw.</summary>
    public void Show(string title, string body);
}
