using Bunit;

namespace Context.Tests;

// 244 AC-5, AC-6 (.NET part): a link pasted into the editor (OnPasteLink) is looked up only for a plain note with
// fetchTitles on; a title goes to setLinkTitle, and a reload or a closed note cancels the lookup. FakePageTitles only.
public sealed class NoteEditorPasteTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private const string Url = "https://e.x/a";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-5: with fetchTitles missing, false or unreadable, nothing is asked and the URL stays.
    [Theory]
    [InlineData(null)]
    [InlineData("{\"paste\":{\"fetchTitles\":false}}")]
    [InlineData("not json")]
    public async Task SettingOff_NoRequest(string? settings)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: false);

        await Paste(cut, js, Url);

        Assert.Equal(0, data.PageTitles.Calls);
        Assert.Empty(js.LinkTitles());
    }

    // AC-5: a sensitive note never asks, even with the setting on.
    [Fact]
    public async Task Sensitive_NoRequest()
    {
        TurnOn();
        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: true);

        await Paste(cut, js, Url);

        Assert.Equal(0, data.PageTitles.Calls);
        Assert.Empty(js.LinkTitles());
    }

    // AC-6: on and plain: the source is asked once for the URL, and its title goes to setLinkTitle.
    [Fact]
    public async Task On_AsksOnce_SetsTheTitle()
    {
        TurnOn();
        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: false);

        await Paste(cut, js, Url);

        Assert.Equal([Url], data.PageTitles.Urls);
        Assert.Equal([(Url, "Example page")], js.LinkTitles());
        Assert.Single(js.Calls("ContextEditor.setLinkTitle"));
    }

    // AC-6: a null title leaves the text alone.
    [Fact]
    public async Task NullTitle_NoSetLinkTitle()
    {
        TurnOn();
        data.PageTitles.Title = null;
        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: false);

        await Paste(cut, js, Url);

        Assert.Equal(1, data.PageTitles.Calls);
        Assert.Empty(js.LinkTitles());
    }

    // AC-6: a reload while the title is on its way cancels the lookup; a late answer changes nothing.
    [Fact]
    public async Task Reload_CancelsTheLookup()
    {
        TurnOn();
        data.PageTitles.Hold = true;
        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: false);
        await cut.InvokeAsync(() => js.Page().OnPasteLink(Url));
        await data.PageTitles.Asked.WaitAsync(Wait);

        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());

        await data.PageTitles.SawCancel.WaitAsync(Wait);
        data.PageTitles.Answer("Late");
        await cut.Instance.LastTitleFetch.WaitAsync(Wait);
        Assert.Empty(js.LinkTitles());
    }

    // AC-6: closing the note cancels the lookup; a late answer changes nothing.
    [Fact]
    public async Task Close_CancelsTheLookup()
    {
        TurnOn();
        data.PageTitles.Hold = true;
        (IRenderedComponent<NoteEditor> cut, EditorJs js) = await RenderPlan(sensitive: false);
        await cut.InvokeAsync(() => js.Page().OnPasteLink(Url));
        await data.PageTitles.Asked.WaitAsync(Wait);
        Task fetch = cut.Instance.LastTitleFetch;

        await data.Bunit.DisposeComponentsAsync();

        await data.PageTitles.SawCancel.WaitAsync(Wait);
        data.PageTitles.Answer("Late");
        await fetch.WaitAsync(Wait);
        Assert.Empty(js.LinkTitles());
    }

    private void TurnOn() => data.WriteFile(SettingsStore.FileName, "{\"paste\":{\"fetchTitles\":true}}");

    // The page's paste report, then the lookup it started (if any) finished.
    private static async Task Paste(IRenderedComponent<NoteEditor> cut, EditorJs js, string url)
    {
        await cut.InvokeAsync(() => js.Page().OnPasteLink(url));
        await cut.Instance.LastTitleFetch.WaitAsync(Wait);
    }

    private async Task<(IRenderedComponent<NoteEditor> Cut, EditorJs Js)> RenderPlan(bool sensitive)
    {
        EditorJs js = new EditorJs(data.Bunit);
        if (sensitive)
        {
            data.Keys = FakeKeyStore.Ready();
            data.WriteSensitive("Plan", "# Plan\n");
        }
        else
        {
            data.WriteNote("Plan", "# Plan\n");
        }

        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Sensitive, sensitive));
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        return (cut, js);
    }
}
