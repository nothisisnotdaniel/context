using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 178 AC-10: timed todos through the palette's /todo <date> [HH:mm] <text>. The day is DesktopFixture.Today
// (Thursday 2026-10-29).
public sealed class TimedTodoTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // A time word after the date is the todo's time; the rest is its text.
    [Theory]
    [InlineData("/todo 2026-11-16 14:00 Call Anna", "2026-11-16", 14, 0)]
    [InlineData("/todo tomorrow 9:05   Call Anna  ", "2026-10-30", 9, 5)]
    [InlineData("/todo today 00:00 Call Anna", "2026-10-29", 0, 0)]
    public async Task Palette_TodoWithTime_AddsTimedTodo(string line, string due, int hour, int minute)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Call Anna", added.Text);
        Assert.Equal(DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), added.Due);
        Assert.Equal(new TimeOnly(hour, minute), added.Time);
        Assert.Null(added.Notified);
        Assert.Null(added.Repeat);
        IElement status = cut.Find(".main-status");
        Assert.Equal("Added: Call Anna", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
    }

    // Without a time word the todo has no time (text that only starts like a time stays text).
    [Theory]
    [InlineData("/todo today Call Anna", "Call Anna")]
    [InlineData("/todo today 14h Call Anna", "14h Call Anna")]
    public async Task Palette_TodoWithoutTime_AddsUntimed(string line, string text)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(text, added.Text);
        Assert.Null(added.Time);
    }

    // A bad time, or a time with no text, is the red timed hint and writes nothing; a bad date keeps its old hint.
    [Theory]
    [InlineData("/todo today 25:00 x", "Type /todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.")]
    [InlineData("/todo today 12:60 x", "Type /todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.")]
    [InlineData("/todo today 14:00", "Type /todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.")]
    [InlineData("/todo today 14:00   ", "Type /todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.")]
    [InlineData("/todo someday 14:00 x", "Type /todo <yyyy-MM-dd|today|tomorrow> <text>.")]
    public async Task Palette_TodoBadTime_RedHint_WritesNothing(string line, string hint)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        IElement status = cut.Find(".main-status");
        Assert.Equal(hint, status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }
}
