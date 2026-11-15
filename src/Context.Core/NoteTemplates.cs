using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What <see cref="NoteTemplates.TryRead"/> found.</summary>
internal enum TemplateReadKind
{
    /// <summary>The template was found and read.</summary>
    Found,

    /// <summary>No template has that name.</summary>
    Missing,

    /// <summary>The template is over <see cref="NoteStore.MaxEditBytes"/> or can't be read.</summary>
    Unreadable,

    /// <summary>The top-level <c>templates</c> note is sensitive, so no template is read.</summary>
    TemplatesSensitive,
}

/// <summary>What <see cref="NoteTemplates.Prepare"/> found: whether the note can be created, and how.</summary>
internal enum TemplateNoteKind
{
    /// <summary>The note can be created with <see cref="TemplateNote.Text"/>.</summary>
    Ready,

    /// <summary>The title is invalid; <see cref="TemplateNote.Text"/> is the <see cref="NoteStore.ValidateTitle"/> text.</summary>
    InvalidTitle,

    /// <summary>A plain or sensitive note with that title exists (ignoring case).</summary>
    Exists,

    /// <summary>The top-level <c>templates</c> note is sensitive.</summary>
    TemplatesSensitive,

    /// <summary>No template has that name; <see cref="TemplateNote.Templates"/> lists the ones there are.</summary>
    Missing,

    /// <summary>The template is too large or can't be read.</summary>
    Unreadable,

    /// <summary>The filled text would be over <see cref="NoteStore.MaxEditBytes"/>.</summary>
    TooBig,
}

/// <summary>
/// The answer of <see cref="NoteTemplates.Prepare"/>: a kind, the normalized note title and template name (raw, never
/// cleaned), the filled text (or the title's validation text) and the template names when one is missing.
/// </summary>
internal sealed record TemplateNote
{
    public TemplateNote(TemplateNoteKind kind, string title, string template, string text, IReadOnlyList<string> templates)
    {
        Kind = kind;
        Title = title;
        Template = template;
        Text = text;
        Templates = templates;
    }

    public TemplateNoteKind Kind { get; }

    /// <summary>The note's title after <see cref="NoteStore.NormalizeTitle"/>.</summary>
    public string Title { get; }

    /// <summary>The template's name: the listed name when found, else the typed name after <see cref="NoteStore.NormalizeTitle"/>.</summary>
    public string Template { get; }

    /// <summary>The filled note text when <see cref="TemplateNoteKind.Ready"/>, the validation text when <see cref="TemplateNoteKind.InvalidTitle"/>, else empty.</summary>
    public string Text { get; }

    /// <summary>The template names (<see cref="NoteTemplates.List"/>) when <see cref="TemplateNoteKind.Missing"/>, else empty.</summary>
    public IReadOnlyList<string> Templates { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TemplateNote);
}

/// <summary>
/// Note templates: the regular <c>*.md</c> files directly in <c>&lt;root&gt;/templates/</c>. A typed template name is
/// only ever matched against <see cref="List"/>, never turned into a path, and reads are bounded. Never writes.
/// </summary>
internal sealed class NoteTemplates
{
    /// <summary>The folder (and the top-level note whose subpages they also are) holding the templates.</summary>
    public const string FolderName = "templates";

    private const string Extension = ".md";

    private readonly NoteStore notes;

    public NoteTemplates(NoteStore notes)
    {
        this.notes = notes;
    }

    /// <summary>
    /// The template names: the titles of the regular <c>*.md</c> files directly in <c>templates/</c> whose title
    /// passes <see cref="NoteStore.ValidateTitle"/>, sorted ignoring case, then ordinal. Subfolders, other files,
    /// symlinks, reparse points and names starting with <c>.</c> are skipped. A missing folder, or one that is a
    /// link or can't be read, gives an empty list; nothing is created.
    /// </summary>
    public IReadOnlyList<string> List() => Entries().Select(e => e.Name).ToList();

    /// <summary>
    /// Reads the template <paramref name="name"/> (normalized with <see cref="NoteStore.NormalizeTitle"/>, matched
    /// against <see cref="List"/> exactly first, then ignoring case). <paramref name="text"/> is the decoded text
    /// (a byte order mark dropped) when <see cref="TemplateReadKind.Found"/>, else empty. A sensitive top-level
    /// <c>templates</c> note gives <see cref="TemplateReadKind.TemplatesSensitive"/> before anything is listed.
    /// </summary>
    public TemplateReadKind TryRead(string name, out string text) => TryRead(name, out text, out _);

    /// <summary>Like <see cref="TryRead(string, out string)"/>, also giving the listed name of the template found.</summary>
    public TemplateReadKind TryRead(string name, out string text, out string? found)
    {
        text = string.Empty;
        found = null;
        if (notes.TryFindSensitive(FolderName, out NoteInfo? sealedNote) && sealedNote is not null)
        {
            return TemplateReadKind.TemplatesSensitive;
        }

        string wanted = NoteStore.NormalizeTitle(name);
        List<(string Name, string Path)> entries = Entries();
        (string Name, string Path)? match = null;
        foreach ((string Name, string Path) entry in entries)
        {
            if (string.Equals(entry.Name, wanted, StringComparison.Ordinal))
            {
                match = entry;
                break;
            }
        }

        if (match is null)
        {
            foreach ((string Name, string Path) entry in entries)
            {
                if (string.Equals(entry.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }
        }

        if (match is not { } chosen)
        {
            return TemplateReadKind.Missing;
        }

        found = chosen.Name;
        try
        {
            text = Decode(BoundedFile.Read(chosen.Path, NoteStore.MaxEditBytes));
            return TemplateReadKind.Found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TemplateReadKind.Unreadable;
        }
    }

    /// <summary>
    /// Fills <paramref name="text"/> in one left-to-right pass: every <c>{{date}}</c> becomes <c>yyyy-MM-dd</c>,
    /// every <c>{{time}}</c> <c>HH:mm</c> (both invariant, from <paramref name="localNow"/>) and every
    /// <c>{{title}}</c> the normalized <paramref name="title"/>, inserted literally (never expanded again). Any other
    /// <c>{{...}}</c> stays as written, line endings are kept and a leading byte order mark is dropped.
    /// </summary>
    public static string Fill(string text, string title, DateTime localNow)
    {
        string date = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string time = localNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        string normalized = NoteStore.NormalizeTitle(title);
        int start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        (string Token, string Value)[] tokens = [("{{date}}", date), ("{{time}}", time), ("{{title}}", normalized)];
        StringBuilder filled = new StringBuilder(text.Length);
        int i = start;
        while (i < text.Length)
        {
            int matched = -1;
            if (text[i] == '{')
            {
                for (int t = 0; t < tokens.Length; t++)
                {
                    if (string.CompareOrdinal(text, i, tokens[t].Token, 0, tokens[t].Token.Length) == 0)
                    {
                        matched = t;
                        break;
                    }
                }
            }

            if (matched >= 0)
            {
                filled.Append(tokens[matched].Value);
                i += tokens[matched].Token.Length;
            }
            else
            {
                filled.Append(text[i]);
                i++;
            }
        }

        return filled.ToString();
    }

    /// <summary>
    /// Everything needed to create the note <paramref name="title"/> from the template <paramref name="template"/>,
    /// checked in this order: the title (<see cref="NoteStore.ValidateTitle"/>), an existing plain or sensitive note
    /// (ignoring case), then the template (<see cref="TryRead(string, out string, out string?)"/>), then the size of
    /// the filled text. The template is only read when the title is free. Reads only; the front end writes the
    /// <see cref="TemplateNoteKind.Ready"/> text with <see cref="NoteStore.CreateWithText"/>.
    /// </summary>
    public TemplateNote Prepare(string title, string template, DateTime localNow)
    {
        string normalized = NoteStore.NormalizeTitle(title);
        string name = NoteStore.NormalizeTitle(template);
        string? invalid = NoteStore.ValidateTitle(normalized);
        if (invalid is not null)
        {
            return new TemplateNote(TemplateNoteKind.InvalidTitle, normalized, name, invalid, []);
        }

        if (notes.List().Any(n => string.Equals(n.Name, normalized, StringComparison.OrdinalIgnoreCase))
            || (notes.TryFindSensitive(normalized, out NoteInfo? sealedNote) && sealedNote is not null))
        {
            return new TemplateNote(TemplateNoteKind.Exists, normalized, name, string.Empty, []);
        }

        switch (TryRead(name, out string text, out string? found))
        {
            case TemplateReadKind.TemplatesSensitive:
                return new TemplateNote(TemplateNoteKind.TemplatesSensitive, normalized, name, string.Empty, []);
            case TemplateReadKind.Missing:
                return new TemplateNote(TemplateNoteKind.Missing, normalized, name, string.Empty, List());
            case TemplateReadKind.Unreadable:
                return new TemplateNote(TemplateNoteKind.Unreadable, normalized, found ?? name, string.Empty, []);
        }

        string filled = Fill(text, normalized, localNow);
        if (Encoding.UTF8.GetByteCount(filled) > NoteStore.MaxEditBytes)
        {
            return new TemplateNote(TemplateNoteKind.TooBig, normalized, found ?? name, string.Empty, []);
        }

        return new TemplateNote(TemplateNoteKind.Ready, normalized, found ?? name, filled, []);
    }

    // Decodes like File.ReadAllText: UTF-8 unless a byte order mark says otherwise; the mark is dropped.
    private static string Decode(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes, writable: false);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // The listed templates with their paths, sorted ignoring case, then ordinal.
    private List<(string Name, string Path)> Entries()
    {
        DirectoryInfo folder = new DirectoryInfo(Path.Combine(notes.Root, FolderName));
        try
        {
            if (!folder.Exists || NoteStore.IsLink(folder))
            {
                return [];
            }

            return folder.EnumerateFiles("*" + Extension, SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(f.Extension, Extension, StringComparison.OrdinalIgnoreCase)
                    && !f.Name.StartsWith('.')
                    && !NoteStore.IsLink(f))
                .Select(f => (Name: Path.GetFileNameWithoutExtension(f.Name), Path: f.FullName))
                .Where(t => NoteStore.ValidateTitle(t.Name) is null)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
