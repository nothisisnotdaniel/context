namespace Context;

/// <summary>
/// The command bar's window (job 282). <c>Program.cs</c> passes the real <c>PhotinoCommandBarWindow</c>; tests use
/// <c>FakeCommandBarWindow</c>, so no test opens a real window.
/// </summary>
internal interface ICommandBarWindow
{
    /// <summary>True while a bar is open.</summary>
    public bool IsOpen { get; }

    /// <summary>Opens a new bar.</summary>
    public void Show();

    /// <summary>Brings the open bar to the front and focuses its input.</summary>
    public void Focus();
}
