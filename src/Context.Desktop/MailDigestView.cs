namespace Context;

/// <summary>
/// One Mail digest read (<see cref="DesktopState.MailDigest"/>, job 286): how the Outlook read ended and, when it was
/// read, the suggestions (raw outside text).
/// </summary>
internal sealed record MailDigestView
{
    public MailDigestView(OutlookStatus status, IReadOnlyList<MailSuggestion> suggestions)
    {
        Status = status;
        Suggestions = suggestions;
    }

    /// <summary><see cref="OutlookStatus.Off"/> when the digest is off; <see cref="OutlookStatus.Ok"/> when read.</summary>
    public OutlookStatus Status { get; }

    /// <summary>The suggestions; empty unless <see cref="Status"/> is <see cref="OutlookStatus.Ok"/>.</summary>
    public IReadOnlyList<MailSuggestion> Suggestions { get; }

    /// <summary>Only the type name, like the other view records.</summary>
    public override string ToString() => nameof(MailDigestView);
}
