namespace Context;

/// <summary>The work item page's data plus whether <c>settings.json</c> could be read (job 280).</summary>
internal sealed record WorkItemPageView
{
    public WorkItemPageView(WorkItemView view, bool settingsUnreadable)
    {
        View = view;
        SettingsUnreadable = settingsUnreadable;
    }

    /// <summary>The Core view.</summary>
    public WorkItemView View { get; }

    /// <summary>True when <c>settings.json</c> could not be read, so no repository was read.</summary>
    public bool SettingsUnreadable { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(WorkItemPageView);
}

/// <summary>The repository folders of the settings page (job 280).</summary>
internal sealed record RepositoryFoldersView
{
    public RepositoryFoldersView(IReadOnlyList<string> folders, bool unreadable)
    {
        Folders = folders;
        Unreadable = unreadable;
    }

    /// <summary>The folders in saved order (raw).</summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>True when <c>settings.json</c> could not be read.</summary>
    public bool Unreadable { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(RepositoryFoldersView);
}
