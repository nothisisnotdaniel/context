using System.Text;

namespace Context.Tests;

// 253 AC-4, AC-5: picking a meeting's topics and ticking one off in its note.
public sealed class MeetingTopicsTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-meetingtopics-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;

    public MeetingTopicsTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PathOf(string name) => Path.Combine(root, name + ".md");

    private void Write(string name, byte[] bytes) => File.WriteAllBytes(PathOf(name), bytes);

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private static NoteTopic Topic(string note, int line, string person) => new(note, line, person, "t", "next: @[" + person + "] t");

    // AC-4: an alias resolves to its person; order is name position, note ignoring case, then ordinal, then line.
    [Fact]
    public void For_ResolvesAliases_AndOrders()
    {
        PersonAliases aliases = new([("Ann Lee", ["Ann"])]);
        NoteTopic[] topics =
        [
            Topic("b", 2, "Ann"),
            Topic("B", 1, "Ann Lee"),
            Topic("a", 5, "ann lee"),
            Topic("a", 3, "Ann"),
            Topic("z", 1, "Bob"),
            Topic("c", 1, "Carl"),
        ];

        IReadOnlyList<NoteTopic> picked = MeetingTopics.For(topics, aliases, ["Bob", "Ann Lee"]);

        Assert.Equal(["z:1", "a:3", "a:5", "B:1", "b:2"], picked.Select(t => t.Note + ":" + t.Line));
    }

    // AC-4: a name given twice keeps its first position; no names give nothing.
    [Fact]
    public void For_FirstPositionWins_NoNamesNothing()
    {
        PersonAliases aliases = new([("Ann Lee", ["Ann"])]);
        NoteTopic[] topics = [Topic("x", 1, "Bob"), Topic("y", 1, "Ann Lee")];

        Assert.Equal(["y", "x"], MeetingTopics.For(topics, aliases, ["Ann", "Bob", "Ann Lee"]).Select(t => t.Note));
        Assert.Empty(MeetingTopics.For(topics, aliases, []));
    }

    // AC-4: through the index, an alias on people/Ann Lee resolves topics written with the alias.
    [Fact]
    public void For_IndexAliases()
    {
        File.WriteAllText(PathOf("people"), "# people\n");
        Directory.CreateDirectory(Path.Combine(root, "people"));
        File.WriteAllText(Path.Combine(root, "people", "Ann Lee.md"), "# Ann Lee\naliases:: Ann\n");
        File.WriteAllText(PathOf("Plan"), "next: @[Ann] budget\nnext: @[Bob] other\n");
        index.Sync(store);

        NoteTopic topic = Assert.Single(MeetingTopics.For(index.Topics(), index.Aliases(), ["Ann Lee"]));

        Assert.Equal("budget", topic.Text);
    }

    private NoteTopic Only() => Assert.Single(index.Topics());

    // AC-5: each open form becomes its done form; CRLF, BOM and every other byte stay; the topic is gone after Sync.
    [Theory]
    [InlineData("- [ ] next: @[Ann Lee] budget", "- [x] next: @[Ann Lee] budget")]
    [InlineData("  - next: @[Ann Lee] budget", "  - [x] next: @[Ann Lee] budget")]
    [InlineData("next: @[Ann Lee] budget", "- [x] next: @[Ann Lee] budget")]
    public void Complete_OpenForms_BecomeDone(string line, string done)
    {
        Write("Plan", Utf8("# Plan\r\n" + line + "\r\nafter\r\n", bom: true));
        index.Sync(store);
        NoteTopic topic = Only();
        Assert.Equal(2, topic.Line);

        Assert.Equal(NoteTodoResult.Done, MeetingTopics.Complete(store, topic));

        Assert.Equal(Utf8("# Plan\r\n" + done + "\r\nafter\r\n", bom: true), File.ReadAllBytes(PathOf("Plan")));
        index.Sync(store);
        Assert.Empty(index.Topics());
    }

    // AC-5: a changed line gives Changed and writes nothing.
    [Fact]
    public void Complete_ChangedLine_WritesNothing()
    {
        Write("Plan", Utf8("next: @[Ann Lee] budget\n"));
        index.Sync(store);
        NoteTopic topic = Only();
        byte[] changed = Utf8("next: @[Ann Lee] budget now\n");
        Write("Plan", changed);

        Assert.Equal(NoteTodoResult.Changed, MeetingTopics.Complete(store, topic));
        Assert.Equal(changed, File.ReadAllBytes(PathOf("Plan")));

        Write("Plan", Utf8("new first line\nnext: @[Ann Lee] budget\n"));
        Assert.Equal(NoteTodoResult.Changed, MeetingTopics.Complete(store, topic));
    }

    // AC-5: a write in between the read and the save gives Changed and keeps the other write.
    [Fact]
    public void Complete_WriteInBetween_Changed()
    {
        Write("Plan", Utf8("next: @[Ann Lee] budget\n"));
        index.Sync(store);
        byte[] other = Utf8("other\n");

        NoteTodoResult result = MeetingTopics.Complete(store, Only(), null, () => Write("Plan", other));

        Assert.Equal(NoteTodoResult.Changed, result);
        Assert.Equal(other, File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-5: a missing note gives Missing.
    [Fact]
    public void Complete_MissingNote_Missing()
    {
        Write("Plan", Utf8("next: @[Ann Lee] budget\n"));
        index.Sync(store);
        NoteTopic topic = Only();
        File.Delete(PathOf("Plan"));

        Assert.Equal(NoteTodoResult.Missing, MeetingTopics.Complete(store, topic));
    }
}
