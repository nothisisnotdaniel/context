namespace Context;

/// <summary>What a save of the open note writes: the note as opened and the text to save.</summary>
internal sealed record SaveRequest
{
    public SaveRequest(EditableNote note, string text)
    {
        Note = note;
        Text = text;
    }

    public EditableNote Note { get; }

    public string Text { get; }

    /// <summary>The type name only, so a note's text never ends up in a log by accident.</summary>
    public override string ToString() => nameof(SaveRequest);
}
