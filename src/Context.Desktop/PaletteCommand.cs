namespace Context;

/// <summary>A command the palette offers: its name (<c>/open</c>), how to type it and what it does.</summary>
internal sealed record PaletteCommand
{
    public PaletteCommand(string name, string usage, string description)
    {
        Name = name;
        Usage = usage;
        Description = description;
    }

    public string Name { get; }

    public string Usage { get; }

    public string Description { get; }
}
