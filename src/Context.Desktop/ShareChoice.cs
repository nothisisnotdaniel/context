namespace Context;

/// <summary>
/// What ShareDialog's Choose file carries: the confirmation ConfirmDialog made (a sensitive note needs it) and whether
/// lines tagged #private are left out. Public only because a component's event callback carries it.
/// </summary>
/// <param name="Confirmed">The confirmation from the dialog's Choose file button.</param>
/// <param name="LeaveOutPrivate">True to leave out lines tagged #private.</param>
public sealed record ShareChoice(Confirmation Confirmed, bool LeaveOutPrivate);
