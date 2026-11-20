using System.Text;

namespace Context.Tests;

// 329 AC-8, AC-9, AC-10: the one-off conversion of old "- [ ]" / "- [x]" note todos to "- { }" / "- {x}".
public sealed class TodoMarkerConversionTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-todomarkers-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly NoteHistory history;

    public TodoMarkerConversionTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        history = new NoteHistory(store, () => Now);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PathOf(string name, string extension = ".md") => Path.Combine(root, name + extension);

    private void Write(string name, byte[] bytes, string extension = ".md")
    {
        string path = PathOf(name, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private TodoMarkerConversionResult Run() => TodoMarkerConversion.Run(store, history, () => Now);

    private NoteInfo Note(string name) => store.List().Single(n => n.Name == name);

    // AC-8: only the three box characters of each old todo line change; CRLF, BOM, indent, fences, topic lines,
    // numbered lists, shapes that are no todo and every other byte stay. The old bytes are a history version.
    [Fact]
    public void Run_ConvertsOnlyOldTodoLines_KeepingEveryOtherByte()
    {
        string before = "# A\r\n- [ ] one due:2026-11-20\r\n\t  * [x] two\r\n+ [X] three\r\n```\r\n- [ ] fenced\r\n```\r\n"
            + "1. [ ] numbered\r\n- [ ] next: @[Ann] topic\r\n- [x] next: @[Ann] done topic\r\n- [ ]x no\r\n- [ ] \r\n"
            + "-  [ ] two spaces\r\n- [ ] four [ ] boxes\r\nend";
        string after = "# A\r\n- { } one due:2026-11-20\r\n\t  * {x} two\r\n+ {x} three\r\n```\r\n- [ ] fenced\r\n```\r\n"
            + "1. [ ] numbered\r\n- [ ] next: @[Ann] topic\r\n- [x] next: @[Ann] done topic\r\n- [ ]x no\r\n- [ ] \r\n"
            + "-  [ ] two spaces\r\n- { } four [ ] boxes\r\nend";
        Write("A", Utf8(before, bom: true));
        Write("B", Utf8("- [ ] b\n"));
        Write("C", Utf8("no todos\n- { } already\n"));

        TodoMarkerConversionResult result = Run();

        Assert.True(result.Ran);
        Assert.Equal((5, 2), (result.Lines, result.Notes));
        Assert.Empty(result.Skipped);
        Assert.Equal(Utf8(after, bom: true), File.ReadAllBytes(PathOf("A")));
        Assert.Equal(Utf8("- { } b\n"), File.ReadAllBytes(PathOf("B")));
        Assert.Equal(Utf8("no todos\n- { } already\n"), File.ReadAllBytes(PathOf("C")));
        NoteVersion version = Assert.Single(history.Versions(Note("A")));
        Assert.True(history.TryRead(Note("A"), version.Id, out byte[]? kept));
        Assert.Equal(Utf8(before, bom: true), kept);
        Assert.Empty(history.Versions(Note("C")));
        Assert.Equal(
            ["Changed 5 todo line(s) in 2 note(s) to - { }."],
            TodoMarkerConversion.Report(result, name => name));
    }

    // AC-8: a sensitive .ctxs note is never opened, so its bytes stay.
    [Fact]
    public void Run_SensitiveNote_BytesUnchanged()
    {
        byte[] sealedBytes = Utf8("- [ ] not plain text at all");
        Write("Secret", sealedBytes, ".ctxs");

        TodoMarkerConversionResult result = Run();

        Assert.Equal((true, 0, 0), (result.Ran, result.Lines, result.Notes));
        Assert.Equal(sealedBytes, File.ReadAllBytes(PathOf("Secret", ".ctxs")));
        Assert.Empty(TodoMarkerConversion.Report(result, name => name));
    }

    // AC-9: the record is written through PrivateFile with the injected time; a second run reads and writes nothing,
    // so a later "- [ ]" stays a checkbox.
    [Fact]
    public void Run_WritesRecord_SecondRunChangesNothing()
    {
        Write("A", Utf8("- [ ] one\n"));

        Assert.True(Run().Ran);

        string record = TodoMarkerConversion.RecordPath(root);
        Assert.Equal(Path.Combine(root, ".migrations", "todo-braces"), record);
        Assert.Equal(
            "todo-braces\nconverted: 2026-10-29T09:00:00Z\nlines: 1\nnotes: 1\nskipped: 0\n",
            File.ReadAllText(record));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(record));
        }

        Write("A", Utf8("- { } one\n- [ ] later\n"));
        TodoMarkerConversionResult second = Run();

        Assert.Equal((false, 0, 0), (second.Ran, second.Lines, second.Notes));
        Assert.Equal(Utf8("- { } one\n- [ ] later\n"), File.ReadAllBytes(PathOf("A")));
        Assert.Empty(TodoMarkerConversion.Report(second, name => name));
    }

    // AC-9: an existing record (a file or a folder) stops the run before any note is read.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RecordExists_NothingConverted(bool folder)
    {
        string record = TodoMarkerConversion.RecordPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        if (folder)
        {
            Directory.CreateDirectory(record);
        }
        else
        {
            File.WriteAllText(record, "x");
        }

        Write("A", Utf8("- [ ] one\n"));

        Assert.False(Run().Ran);
        Assert.Equal(Utf8("- [ ] one\n"), File.ReadAllBytes(PathOf("A")));
    }

    // AC-9: without a data folder nothing is read or written, and no folder is made.
    [Fact]
    public void Run_NoRoot_NothingWritten()
    {
        string missing = Path.Combine(root, "missing");
        NoteStore other = new NoteStore(missing);

        TodoMarkerConversionResult result = TodoMarkerConversion.Run(other, new NoteHistory(other, () => Now), () => Now);

        Assert.False(result.Ran);
        Assert.False(Directory.Exists(missing));
    }

    // AC-10: a note over MaxNoteBytes is skipped and named; it keeps its bytes; the record is still written.
    [Fact]
    public void Run_TooLargeNote_SkippedAndNamed()
    {
        byte[] big = new byte[NoteStore.MaxNoteBytes + 1];
        Array.Fill(big, (byte)'a');
        Utf8("- [ ] one\n").CopyTo(big, 0);
        Write("Big", big);
        Write("Small", Utf8("- [ ] two\n"));

        TodoMarkerConversionResult result = Run();

        Assert.Equal(["Big"], result.Skipped);
        Assert.Equal(big, File.ReadAllBytes(PathOf("Big")));
        Assert.Equal(Utf8("- { } two\n"), File.ReadAllBytes(PathOf("Small")));
        Assert.Equal(
            ["Changed 1 todo line(s) in 1 note(s) to - { }.", "Can't convert the todos in Big."],
            TodoMarkerConversion.Report(result, name => name));
        Assert.Contains("skipped: 1\n", File.ReadAllText(TodoMarkerConversion.RecordPath(root)), StringComparison.Ordinal);
    }

    // AC-10: a note in a read-only folder (no write) and an unreadable note are skipped, named and unchanged.
    [Fact]
    public void Run_ReadOnlyAndUnreadableNotes_Skipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("Locked", Utf8("parent page\n"));
        Write(Path.Combine("Locked", "Plan"), Utf8("- [ ] one\n"));
        Write("Hidden", Utf8("- [ ] two\n"));
        string locked = Path.Combine(root, "Locked");
        UnixFileMode mode = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(PathOf(Path.Combine("Locked", "Plan")), UnixFileMode.UserRead);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.SetUnixFileMode(PathOf("Hidden"), UnixFileMode.None);
        TodoMarkerConversionResult result;
        try
        {
            result = Run();
        }
        finally
        {
            File.SetUnixFileMode(locked, mode);
            File.SetUnixFileMode(PathOf(Path.Combine("Locked", "Plan")), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(PathOf("Hidden"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(["Hidden", "Locked/Plan"], result.Skipped.Order(StringComparer.Ordinal));
        Assert.Equal(0, result.Lines);
        Assert.Equal(Utf8("- [ ] one\n"), File.ReadAllBytes(PathOf(Path.Combine("Locked", "Plan"))));
        Assert.Equal(Utf8("- [ ] two\n"), File.ReadAllBytes(PathOf("Hidden")));
        Assert.Equal(
            ["Can't convert the todos in Hidden.", "Can't convert the todos in Locked/Plan."],
            TodoMarkerConversion.Report(result, name => name).Order(StringComparer.Ordinal));
    }

    // AC-8, AC-10: an edit between the read and the save wins: the note is not written and is named.
    [Fact]
    public void Run_EditInBetween_NotWritten_Skipped()
    {
        Write("A", Utf8("- [ ] one\n"));
        byte[] edited = Utf8("- [ ] one\nedited\n");

        TodoMarkerConversionResult result = TodoMarkerConversion.Run(store, history, () => Now, _ => Write("A", edited));

        Assert.Equal(["A"], result.Skipped);
        Assert.Equal(edited, File.ReadAllBytes(PathOf("A")));
        Assert.Empty(history.Versions(Note("A")));
    }

    // AC-8, AC-10: a note with an old todo line that is not strict UTF-8 (an invalid byte, UTF-16 LE, UTF-32 LE with
    // BOM) would change beyond the box on save, so it is skipped, named and keeps its bytes. A note with no old todo
    // line is not named.
    [Fact]
    public void Run_NotStrictUtf8_SkippedBytesUnchanged()
    {
        byte[] invalid = [.. Utf8("- [ ] one caf"), 0xE9, .. Utf8("\n")];
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("- [ ] two\n")];
        byte[] utf32 = [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes("- [ ] three\n")];
        byte[] quiet = [.. Utf8("no todo caf"), 0xE9, .. Utf8("\n")];
        Write("Invalid", invalid);
        Write("Wide", utf16);
        Write("Wider", utf32);
        Write("Quiet", quiet);
        Write("Fine", Utf8("- [ ] four\n", bom: true));

        TodoMarkerConversionResult result = Run();

        Assert.Equal(["Invalid", "Wide", "Wider"], result.Skipped.Order(StringComparer.Ordinal));
        Assert.Equal((1, 1), (result.Lines, result.Notes));
        Assert.Equal(invalid, File.ReadAllBytes(PathOf("Invalid")));
        Assert.Equal(utf16, File.ReadAllBytes(PathOf("Wide")));
        Assert.Equal(utf32, File.ReadAllBytes(PathOf("Wider")));
        Assert.Equal(quiet, File.ReadAllBytes(PathOf("Quiet")));
        Assert.Equal(Utf8("- { } four\n", bom: true), File.ReadAllBytes(PathOf("Fine")));
        Assert.Empty(history.Versions(Note("Invalid")));
        Assert.Empty(history.Versions(Note("Wide")));
    }

    // AC-9: a link at the record path (a dangling one too) counts as the record, so nothing is converted.
    [Fact]
    public void Run_RecordIsDanglingLink_NothingConverted()
    {
        string record = TodoMarkerConversion.RecordPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        try
        {
            File.CreateSymbolicLink(record, Path.Combine(root, "nowhere"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No right to make links here (Windows without developer mode).
            return;
        }

        Write("A", Utf8("- [ ] one\n"));

        Assert.False(Run().Ran);
        Assert.Equal(Utf8("- [ ] one\n"), File.ReadAllBytes(PathOf("A")));
    }

    // AC-10: the report cleans each skipped name with the given function; the result never prints a name.
    [Fact]
    public void Report_CleansNames_ResultToStringIsTypeName()
    {
        TodoMarkerConversionResult result = new TodoMarkerConversionResult(true, 0, 0, ["bad\u0007name"]);

        Assert.Equal(["Can't convert the todos in bad?name."], TodoMarkerConversion.Report(result, n => n.Replace('\u0007', '?')));
        Assert.Equal(nameof(TodoMarkerConversionResult), result.ToString());
    }

    // 330 AC-4: the conversion leaves a "[>]" line byte for byte, also next to a converted line.
    [Theory]
    [InlineData("- [>] text")]
    [InlineData("  * [>] text\r\n")]
    [InlineData("+ [>] a\n- [ ] b\n")]
    public void Convert_CarryLine_Unchanged(string text)
    {
        string converted = TodoMarkerConversion.Convert(text, out int changed);

        Assert.Equal(text.Replace("- [ ] b", "- { } b", StringComparison.Ordinal), converted);
        Assert.Equal(text.Contains("[ ]", StringComparison.Ordinal) ? 1 : 0, changed);
    }
}
