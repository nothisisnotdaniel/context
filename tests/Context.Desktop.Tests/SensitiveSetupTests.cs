using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 136 AC-4, AC-8: the recovery dialog, with a file dialog that only returns the path the test sets.
public sealed class SensitiveSetupTests : IDisposable
{
    private const string Password = "long enough password";

    private readonly DesktopFixture data = new();
    private readonly List<string> done = [];
    private int cancelled;

    public void Dispose() => data.Dispose();

    // AC-4: the dialog names the loss case, and its password fields never render a value.
    [Fact]
    public async Task Setup_ShowsTheWarning_AndTwoEmptyPasswordFields()
    {
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);

        Assert.Equal("Set up sensitive notes", cut.Find("#sensitive-setup-title").TextContent);
        Assert.Contains("can't be opened again", cut.Find(".setup-warning").TextContent, StringComparison.Ordinal);
        Assert.Contains("red", cut.Find(".setup-warning").ClassList);
        Assert.Contains("at least 12 characters", cut.Find(".setup-text").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("input").Count);
        Assert.All(cut.FindAll("input"), i =>
        {
            Assert.Equal("password", i.GetAttribute("type"));
            Assert.False(i.HasAttribute("value"));
            Assert.False(string.IsNullOrEmpty(i.GetAttribute("aria-label")));
        });
        Assert.Equal("dialog", cut.Find(".sensitive-setup").GetAttribute("role"));
        Assert.Equal("Choose where to save", cut.Find("button.confirm").TextContent);

        await Type(cut, Password, Password);
        Assert.DoesNotContain(Password, cut.Markup, StringComparison.Ordinal);
    }

    // AC-4: two different entries or a short password are refused before any dialog or file.
    [Theory]
    [InlineData("long enough password", "long enough passwore", "The passwords don't match.")]
    [InlineData("elevenchars", "elevenchars", "The password needs at least 12 characters.")]
    [InlineData("", "", "The password needs at least 12 characters.")]
    [InlineData("elevenchare\u0301", "elevenchar\u00E9", "The password needs at least 12 characters.")]
    [InlineData("long enough <lone surrogate> password", "long enough <lone surrogate> password", "That password can't be used.")]
    public async Task Setup_BadPasswords_RefusedBeforeTheFileDialog(string first, string second, string text)
    {
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        // An attribute can't carry a lone surrogate, so the row names it and it is put in here.
        await Type(cut, first.Replace("<lone surrogate>", "\uD800", StringComparison.Ordinal), second.Replace("<lone surrogate>", "\uD800", StringComparison.Ordinal));

        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Equal(text, cut.Find(".setup-status").TextContent);
        Assert.Contains("red", cut.Find(".setup-status").ClassList);
        Assert.Equal(0, data.Dialog.Saves);
        Assert.False(File.Exists(data.RecoveryPath));
        Assert.Equal(0, data.Keys.MachineKeysCreated);
        Assert.Empty(done);
    }

    // AC-4: a cancelled save dialog does nothing.
    [Fact]
    public async Task Setup_CancelledFileDialog_DoesNothing()
    {
        data.Dialog.SavePath = null;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        await Type(cut, Password, Password);

        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, data.Dialog.Saves);
        Assert.Equal(string.Empty, cut.Find(".setup-status").TextContent);
        Assert.Equal(0, data.Keys.MachineKeysCreated);
        Assert.Empty(done);
    }

    // AC-4: with matching passwords and a path the recovery file is written and the dialog reports done.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_WritesTheRecoveryFile(bool byEnter)
    {
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        await Type(cut, Password, Password);

        if (byEnter)
        {
            await cut.Find("input.recovery-password-again").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());
        }

        cut.WaitForAssertion(() => Assert.Equal(["Recovery file saved. Keep it and its password safe."], done));
        Assert.True(File.Exists(data.RecoveryPath));
        Assert.NotNull(data.Keys.RecoveryPublicKey());
        Assert.NotNull(data.Keys.MachinePublicKey());
        Assert.Empty(FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // Round 2 (security 1): the two entries are compared in Unicode form C, so the same visible password typed
    // composed and decomposed is not a mismatch.
    [Fact]
    public async Task Setup_SamePasswordInTwoUnicodeForms_IsNoMismatch()
    {
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        await Type(cut, "caf\u00E9 au lait long", "cafe\u0301 au lait long");

        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(done));
        using System.Security.Cryptography.ECDiffieHellman key = RecoveryFile.Read(File.ReadAllBytes(data.RecoveryPath), "caf\u00E9 au lait long");
        Assert.Equal(data.Keys.RecoveryPublicKey(), key.ExportSubjectPublicKeyInfo());
    }

    // AC-4: a refusal of the action (here: the chosen file exists) is shown in red and the dialog stays.
    [Fact]
    public async Task Setup_ActionRefused_ShowsTheText()
    {
        File.WriteAllText(data.RecoveryPath, "mine");
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        await Type(cut, Password, Password);

        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(DesktopActions.RecoveryFileExistsText, cut.Find(".setup-status").TextContent));
        Assert.Equal("mine", File.ReadAllText(data.RecoveryPath));
        Assert.Empty(done);
    }

    // AC-8: restore has one password field, opens a file and reports how many notes it restored.
    [Fact]
    public async Task Restore_ReadsTheFile_AndReportsTheCount()
    {
        data.WriteNote("Plan", "plan\n");
        await data.RefreshAsync();
        await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);
        await data.Actions.MarkSensitive("Plan", new Confirmation());
        data.Keys.LoseMachineKey();
        data.Dialog.OpenPath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = data.Bunit.Render<SensitiveSetup>(Parameters(restore: true));

        Assert.Equal("Restore from recovery file", cut.Find("#sensitive-setup-title").TextContent);
        Assert.Single(cut.FindAll("input"));
        Assert.Empty(cut.FindAll(".setup-warning"));
        Assert.Equal("Choose file and restore", cut.Find("button.confirm").TextContent);

        await cut.Find("input.recovery-password").InputAsync(new ChangeEventArgs { Value = "wrong long password" });
        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Can't read that recovery file.", cut.Find(".setup-status").TextContent));
        Assert.Empty(done);
        Assert.Null(data.Keys.MachinePublicKey());

        await cut.Find("input.recovery-password").InputAsync(new ChangeEventArgs { Value = Password });
        await cut.Find("button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Restored 1 of 1 notes."], done));
        Assert.Equal(2, data.Dialog.Opens);
        Assert.Equal(0, data.Dialog.Saves);
        Assert.Equal("plan\n", (await data.State!.ReadSensitive("Plan"))!.Text);
    }

    // The Cancel button and Esc close the dialog without doing anything.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_OrEscape_ClosesWithoutActing(bool escape)
    {
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<SensitiveSetup> cut = await Render(restore: false);
        await Type(cut, Password, Password);

        if (escape)
        {
            await cut.Find(".sensitive-setup").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find("button.cancel").ClickAsync(new MouseEventArgs());
        }

        Assert.Equal(1, cancelled);
        Assert.Equal(0, data.Dialog.Saves);
        Assert.False(File.Exists(data.RecoveryPath));
    }

    // AC-4 / AC-8: the notes panel offers setup until recovery exists, and restore always; the result is shown.
    [Fact]
    public async Task NotesPanel_OffersSetupAndRestore()
    {
        data.Dialog.SavePath = data.RecoveryPath;
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        Assert.Equal("Set up sensitive notes", cut.Find("button.sensitive-setup-open").TextContent);
        Assert.Equal("Restore from recovery file", cut.Find("button.sensitive-restore-open").TextContent);
        Assert.Empty(cut.FindAll("[role=dialog]"));

        await cut.Find("button.sensitive-restore-open").ClickAsync(new MouseEventArgs());
        Assert.Equal("Restore from recovery file", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));

        await cut.Find("button.sensitive-setup-open").ClickAsync(new MouseEventArgs());
        await cut.Find("input.recovery-password").InputAsync(new ChangeEventArgs { Value = Password });
        await cut.Find("input.recovery-password-again").InputAsync(new ChangeEventArgs { Value = Password });
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "Recovery file saved. Keep it and its password safe.", cut.Find(".sensitive-result").TextContent));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button.sensitive-setup-open")));
        Assert.Single(cut.FindAll("button.sensitive-restore-open"));
        Assert.DoesNotContain(Password, cut.Markup, StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<SensitiveSetup>> Render(bool restore)
    {
        await data.RefreshAsync();
        return data.Bunit.Render<SensitiveSetup>(Parameters(restore));
    }

    private Action<ComponentParameterCollectionBuilder<SensitiveSetup>> Parameters(bool restore) => p => p
        .Add(s => s.Restore, restore)
        .Add(s => s.OnDone, (string text) => done.Add(text))
        .Add(s => s.OnCancel, () => cancelled++);

    private static async Task Type(IRenderedComponent<SensitiveSetup> cut, string first, string second)
    {
        await cut.Find("input.recovery-password").InputAsync(new ChangeEventArgs { Value = first });
        await cut.Find("input.recovery-password-again").InputAsync(new ChangeEventArgs { Value = second });
    }
}
