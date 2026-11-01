using System.Globalization;
using AngleSharp.Dom;
using Bunit;

namespace Context.Tests;

public sealed class NotesPanelTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4: NoteStore.List() names with their last-write time, in that order.
    [Fact]
    public async Task Render_Notes_NamesAndTimesInListOrder()
    {
        data.WriteNote("beta", "b");
        data.WriteNote("Alpha", "a");
        data.WriteFile(Path.Combine("sub", "Hidden.md"), "x");
        DateTime alpha = new(2026, 10, 20, 8, 5, 0, DateTimeKind.Local);
        DateTime beta = new(2026, 10, 21, 17, 45, 0, DateTimeKind.Local);
        File.SetLastWriteTime(Path.Combine(data.Root, "Alpha.md"), alpha);
        File.SetLastWriteTime(Path.Combine(data.Root, "beta.md"), beta);

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.note");
        Assert.Equal(["Alpha", "beta"], items.Select(i => i.QuerySelector(".name")!.TextContent).ToArray());
        Assert.Equal(
            [Time(alpha), Time(beta)],
            items.Select(i => i.QuerySelector(".time")!.TextContent).ToArray());
        Assert.Equal("Alpha", items[0].QuerySelector(".name")!.GetAttribute("title"));
    }

    // AC-4: an empty folder shows the empty state.
    [Fact]
    public async Task Render_NoNotes_EmptyLine()
    {
        Directory.CreateDirectory(data.Root);

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IElement line = cut.Find("p");
        Assert.Equal("No notes yet", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
    }

    // AC-4: a missing data folder shows the empty states (all panels) and is not created.
    [Fact]
    public async Task Render_MissingFolder_EmptyAndNotCreated()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("No notes yet", cut.Find(".notes p").TextContent);
        Assert.Equal("No todos yet", cut.Find(".todos p").TextContent);
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-4: a listing that throws shows the fixed red line.
    [Fact]
    public async Task Render_ListingThrows_RedLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions only; Windows ACLs would need their own setup.
        }

        Directory.CreateDirectory(data.Root);
        data.WriteNote("Alpha", "a");
        File.SetUnixFileMode(data.Root, UnixFileMode.UserWrite);
        try
        {
            IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

            IElement line = cut.Find("p");
            Assert.Equal("Can't read the notes folder.", line.TextContent);
            Assert.True(line.ClassList.Contains("red"));
        }
        finally
        {
            File.SetUnixFileMode(
                data.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // AC-5: a hostile note name is encoded text with control chars shown as U+FFFD.
    [Fact]
    public async Task Render_HostileName_Encoded()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows file names can't hold < > or control chars.
        }

        data.WriteNote("<script>alert(1)<\\script> <img src=x onerror=alert(1)> \u0007", "x");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<script>alert(1)<\\script> <img src=x onerror=alert(1)> �", cut.Find(".name").TextContent);
    }

    private static string Time(DateTime local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
