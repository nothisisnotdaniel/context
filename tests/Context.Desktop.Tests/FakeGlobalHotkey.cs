namespace Context.Tests;

/// <summary>A hotkey that is pressed only by <see cref="Press"/>; it registers nothing with the system.</summary>
internal sealed class FakeGlobalHotkey : IGlobalHotkey
{
    public event EventHandler? Pressed;

    /// <summary>Whether <see cref="Register"/> was called.</summary>
    public bool Registered { get; private set; }

    /// <summary>Whether <see cref="Dispose"/> was called.</summary>
    public bool Disposed { get; private set; }

    public bool Register()
    {
        Registered = true;
        return true;
    }

    /// <summary>Raises <see cref="Pressed"/> as the system would.</summary>
    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
