namespace Context.Tests;

/// <summary>Job 367 AC-6, AC-7: activating and removing the Suite Pro licence through <see cref="DesktopActions"/>.</summary>
public sealed class ProActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose()
    {
        RestoreWritable();
        data.Dispose();
    }

    private string ActivationPath => Path.Combine(data.ActivationFolder, LicenceActivationFile.FileName);

    private DesktopActions Actions()
    {
        data.CreateState();
        return data.Actions!;
    }

    [Fact]
    public async Task Activate_TrimsVerifiesStoresThenWritesTheFile()
    {
        string token = data.Licences.Token();
        DesktopActions actions = Actions();
        Assert.False(data.State!.Pro.IsUnlocked);

        ActionResult result = await actions.ActivatePro("  " + token + "\n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Suite Pro activated for Ada Lovelace · updates until 2027-12-31.", result.Text);
        Assert.Equal([token], data.LicenceStore.Writes);
        Assert.Equal(token, data.LicenceStore.Stored);
        Assert.Equal(TestLicences.Name, new LicenceActivationFile(data.ActivationFolder).Read()!.Licensee);
        Assert.True(data.State.Pro.IsUnlocked);
        Assert.DoesNotContain(token[LicenceKey.Prefix.Length..][..12], result.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(data.Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Activate_EmptyKey_IsRefused_NothingStored(string key)
    {
        ActionResult result = await Actions().ActivatePro(key);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Paste your licence key first.", result.Text);
        Assert.Empty(data.LicenceStore.Writes);
        Assert.False(File.Exists(ActivationPath));
    }

    [Fact]
    public async Task Activate_InvalidKey_IsRefusedWithTheReason_NeverEchoingIt()
    {
        string other = data.Licences.Token(kid: "other");

        ActionResult unknown = await Actions().ActivatePro(other);
        ActionResult garbage = await Actions().ActivatePro("CTX1-secretgarbage");

        Assert.Equal(ActionKind.Refused, unknown.Kind);
        Assert.Equal("This licence key cannot be used: it was signed with an unknown key. Nothing was stored.", unknown.Text);
        Assert.Equal("This licence key cannot be used: it is not a licence key. Nothing was stored.", garbage.Text);
        Assert.DoesNotContain("secretgarbage", garbage.Text, StringComparison.Ordinal);
        Assert.Empty(data.LicenceStore.Writes);
        Assert.False(File.Exists(ActivationPath));
    }

    [Fact]
    public async Task Activate_UnsupportedStore_IsRefused()
    {
        data.LicenceStore.IsSupported = false;

        ActionResult result = await Actions().ActivatePro(data.Licences.Token());

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Suite Pro needs macOS or Windows.", result.Text);
        Assert.Empty(data.LicenceStore.Writes);
        Assert.False(File.Exists(ActivationPath));
    }

    // The shared dotnet host's refusal (the Mac store's WriteRefusal) is shown as it is and nothing is stored.
    [Fact]
    public async Task Activate_StoreRefusal_IsShown_NothingStored()
    {
        data.LicenceStore.WriteRefusal = "The licence is stored only when Context runs as its own app, not through dotnet.";

        ActionResult result = await Actions().ActivatePro(data.Licences.Token());

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(data.LicenceStore.WriteRefusal, result.Text);
        Assert.Empty(data.LicenceStore.Writes);
        Assert.False(File.Exists(ActivationPath));
    }

    [Fact]
    public async Task Activate_StoreFailure_NothingStored()
    {
        data.LicenceStore.WriteThrows = true;

        ActionResult result = await Actions().ActivatePro(data.Licences.Token());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't save the licence. Nothing was stored.", result.Text);
        Assert.Null(data.LicenceStore.Stored);
        Assert.False(File.Exists(ActivationPath));
        Assert.DoesNotContain(FakeLicenceStore.Failure, result.Text, StringComparison.Ordinal);
    }

    // The folder path is an existing file, so the activation file can't be written; the stored key is deleted again.
    [Fact]
    public async Task Activate_FileWriteFails_DeletesTheStoredKeyAgain()
    {
        File.WriteAllText(data.ActivationFolder, "not a folder");
        DesktopActions actions = Actions();

        ActionResult result = await actions.ActivatePro(data.Licences.Token());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't write the activation file. Nothing was stored.", result.Text);
        Assert.Single(data.LicenceStore.Writes);
        Assert.Equal(1, data.LicenceStore.Deletes);
        Assert.Null(data.LicenceStore.Stored);
        Assert.False(data.State!.Pro.IsUnlocked);
    }

    [Fact]
    public async Task Activate_FileWriteAndDeleteFail_SaysClickRemove()
    {
        File.WriteAllText(data.ActivationFolder, "not a folder");
        data.LicenceStore.DeleteThrows = true;

        ActionResult result = await Actions().ActivatePro(data.Licences.Token());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(
            "Can't write the activation file, and the stored key could not be removed. Click Remove licence.", result.Text);
        Assert.NotNull(data.LicenceStore.Stored);
    }

    [Fact]
    public async Task Remove_WithoutConfirmation_ChangesNothing()
    {
        data.LicenceStore.Stored = data.Licences.Token();

        ActionResult result = await Actions().RemovePro(null);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(0, data.LicenceStore.Deletes);
        Assert.NotNull(data.LicenceStore.Stored);
    }

    [Fact]
    public async Task Remove_DeletesTheKeyThenTheFile()
    {
        DesktopActions actions = Actions();
        await actions.ActivatePro(data.Licences.Token());
        Assert.True(File.Exists(ActivationPath));
        Assert.True(data.State!.Pro.IsUnlocked);

        ActionResult result = await actions.RemovePro(Confirm());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Suite Pro licence removed from this computer.", result.Text);
        Assert.Null(data.LicenceStore.Stored);
        Assert.False(File.Exists(ActivationPath));
        Assert.False(data.State.Pro.IsUnlocked);
    }

    [Fact]
    public async Task Remove_NothingActivated_SaysSo()
    {
        ActionResult result = await Actions().RemovePro(Confirm());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal("No Pro licence was activated.", result.Text);
    }

    [Fact]
    public async Task Remove_StoreFails_NothingChanged()
    {
        DesktopActions actions = Actions();
        await actions.ActivatePro(data.Licences.Token());
        data.LicenceStore.DeleteThrows = true;

        ActionResult result = await actions.RemovePro(Confirm());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't remove the licence. Nothing was changed.", result.Text);
        Assert.True(File.Exists(ActivationPath));
    }

    [Fact]
    public async Task Remove_FileDeleteFails_SaysTheKeyWasRemoved()
    {
        DesktopActions actions = Actions();
        await actions.ActivatePro(data.Licences.Token());
        MakeUndeletable();

        ActionResult result = await actions.RemovePro(Confirm());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("The licence key was removed, but the activation file could not be deleted.", result.Text);
        Assert.Null(data.LicenceStore.Stored);
    }

    [Fact]
    public async Task Remove_UnsupportedStore_IsRefused()
    {
        data.LicenceStore.IsSupported = false;

        ActionResult result = await Actions().RemovePro(Confirm());

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Suite Pro needs macOS or Windows.", result.Text);
        Assert.Equal(0, data.LicenceStore.Deletes);
    }

    private static Confirmation Confirm() => new Confirmation();

    // Windows: a read-only file can't be deleted; elsewhere a read-only folder keeps its entries.
    private void MakeUndeletable()
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(ActivationPath, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(data.ActivationFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    private void RestoreWritable()
    {
        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(ActivationPath))
            {
                File.SetAttributes(ActivationPath, FileAttributes.Normal);
            }
        }
        else if (Directory.Exists(data.ActivationFolder))
        {
            File.SetUnixFileMode(data.ActivationFolder, PrivateFile.FolderBits);
        }
    }
}
