namespace Context;

/// <summary>
/// Proof that the user confirmed a destructive action in <see cref="ConfirmDialog"/>, the only place that creates
/// one. Destructive desktop actions refuse to run without it. Public only because a component's
/// event callback carries it; nothing outside this assembly can create one.
/// </summary>
public sealed class Confirmation
{
    internal Confirmation()
    {
    }
}
