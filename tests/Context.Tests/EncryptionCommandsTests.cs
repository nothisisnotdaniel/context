using Spectre.Console.Testing;

namespace Context.Tests;

/// <summary>Every store and command wired over one root and one <see cref="Encryption"/>, like <c>Program.cs</c>.</summary>
internal sealed class Session : IDisposable
{
    public const int Width = 100;
    public const int Height = 24;

    public static readonly DateOnly Today = new(2026, 10, 29);
    public static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly TagIndex index;
    private readonly CommandRegistry registry;
    private readonly TodoSidebar sidebar;

    /// <param name="minIterations">The lowest key-file iteration count unlocking accepts; tests use low ones.</param>
    public Session(string root, int minIterations = VaultTests.TestIterations)
    {
        Encryption = new Encryption(root, minIterations);
        Notes = new NoteStore(root, Encryption);
        index = new TagIndex(root, Encryption);
        TodoStore todos = new TodoStore(root, () => Now, Encryption);
        CalendarStore calendar = new CalendarStore(root, TimeZoneInfo.Utc, Encryption);
        CalendarLink link = new CalendarLink(root, Encryption);
        CalendarFeed feed = new CalendarFeed(
            root, link, FakeHttpHandler.Ok(CalendarFeedTests.Ics), () => Now, TimeSpan.FromSeconds(5), Encryption);
        registry = CommandRegistry.CreateDefault();
        NoteCommands.Register(registry, Notes, Encryption);
        TagCommands.Register(registry, index, Notes);
        TodoCommands.Register(registry, todos, () => Today);
        CalendarLinkCommands.Register(registry, link, feed, TimeZoneInfo.Utc);
        CurrentCommands.Register(registry, todos, index, Notes, calendar, () => Today, feed);
        Console = new TestConsole().Interactive().EmitAnsiSequences();
        Console.Profile.Width = Width;
        Console.Profile.Height = Height;
        EncryptionCommands.Register(registry, Encryption, new PassphrasePrompt(Console), VaultTests.TestIterations);
        sidebar = new TodoSidebar(todos, () => Today);
    }

    public Encryption Encryption { get; }

    public NoteStore Notes { get; }

    public TestConsole Console { get; }

    /// <summary>Queues every line (each with Enter), then <c>/quit</c>, runs the app and returns its exit code.</summary>
    public int Run(params string[] lines)
    {
        foreach (string line in lines)
        {
            Console.Input.PushTextWithEnter(line);
        }

        Console.Input.PushTextWithEnter("/quit");
        return RunQueued();
    }

    /// <summary>Runs the app on whatever is queued on <see cref="Console"/>.</summary>
    public int RunQueued() => new App(Console, registry, sidebar, Encryption).Run();

    public void Dispose()
    {
        index.Dispose();
        Encryption.Use(null);
    }
}

public sealed class EncryptionCommandsTests : IDisposable
{
    private const string Pass = VaultTests.Passphrase;

    private readonly string parent;
    private readonly string root;

    public EncryptionCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-enccmds-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static string Plain(string output) => AnsiWrites.Strip(output);

    // AC-3
    [Fact]
    public void Encrypt_AsksTwiceMasked_EncryptsEverything_DeletesDb()
    {
        SampleData.Write(root);
        File.WriteAllText(Path.Combine(root, "context.db"), "x");
        using Session session = new Session(root);

        int exitCode = session.Run("/encrypt", Pass, Pass);

        Assert.Equal(0, exitCode);
        string output = Plain(session.Console.Output);
        Assert.Contains("New passphrase:", output);
        Assert.Contains("Repeat passphrase:", output);
        Assert.Contains("your data is lost", output);
        Assert.Contains("Encrypted 6 files. Old plain copies may still exist on SSDs and in backups.", output);
        Assert.True(session.Encryption.IsOn);
        Assert.True(Vault.Exists(root));
        Assert.False(File.Exists(Path.Combine(root, "context.db")));
        foreach (string name in SampleData.DataFiles)
        {
            Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, name))), name);
        }

        SampleData.AssertNoMarkers(root);
    }

    // AC-3
    [Theory]
    [InlineData("short pass", "short pass", "at least 12 characters")]
    [InlineData(Pass, "something else!!", "don't match")]
    public void Encrypt_ShortOrMismatched_ChangesNoFile(string first, string second, string message)
    {
        SampleData.Write(root);
        Dictionary<string, string> before = SampleData.Hashes(root);
        using Session session = new Session(root);

        session.Run("/encrypt", first, second);

        Assert.Contains(message, Plain(session.Console.Output));
        Assert.Contains("Nothing changed.", Plain(session.Console.Output));
        Assert.Equal(before, SampleData.Hashes(root));
        Assert.False(session.Encryption.IsOn);
    }

    // AC-3 (UX): Esc cancels at either prompt.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encrypt_Escape_CancelsWithNoChange(bool atRepeat)
    {
        SampleData.Write(root);
        Dictionary<string, string> before = SampleData.Hashes(root);
        using Session session = new Session(root);
        session.Console.Input.PushTextWithEnter("/encrypt");
        if (atRepeat)
        {
            session.Console.Input.PushTextWithEnter(Pass);
        }

        session.Console.Input.PushText("abc");
        session.Console.Input.PushKey(ConsoleKey.Escape);
        session.Console.Input.PushTextWithEnter("/quit");

        Assert.Equal(0, session.RunQueued());
        Assert.Contains("Encryption cancelled. Nothing changed.", Plain(session.Console.Output));
        Assert.Equal(before, SampleData.Hashes(root));
    }

    // AC-5
    [Fact]
    public void Encrypt_WhenOn_EncryptsOnlyPlainFiles_WithoutAskingAPassphrase()
    {
        SampleData.Write(root);
        using Session session = new Session(root);
        session.Run("/encrypt", Pass, Pass);
        Dictionary<string, string> encrypted = SampleData.Hashes(root);
        File.WriteAllText(Path.Combine(root, "calendar", "new.ics"), SampleData.Ics("dropped"));
        using Session again = new Session(root);
        again.Encryption.Unlock(Pass);

        again.Run("/encrypt");

        string output = Plain(again.Console.Output);
        Assert.DoesNotContain("passphrase:", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encrypted 1 file.", output);
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "calendar", "new.ics"))));
        Dictionary<string, string> after = SampleData.Hashes(root);
        Assert.All(encrypted, pair => Assert.Equal(pair.Value, after[pair.Key]));
    }

    // AC-3: a file that can't be encrypted is named and stays plain.
    [Fact]
    public void Encrypt_FailedFile_IsNamedInRedAndStaysPlain()
    {
        SampleData.Write(root);
        byte[] todos = File.ReadAllBytes(Path.Combine(root, "todos.json"));
        using Session session = new Session(root);
        session.Encryption.BeforeReplace = name =>
        {
            if (name == "todos.json")
            {
                throw new IOException("injected");
            }
        };

        session.Run("/encrypt", Pass, Pass);

        Assert.Contains("Couldn't encrypt todos.json; they stay plain.", Plain(session.Console.Output));
        Assert.Equal(todos, File.ReadAllBytes(Path.Combine(root, "todos.json")));
    }

    // AC-4: after /encrypt every command shows the same as on the plain folder.
    [Fact]
    public void AfterEncrypt_CommandsBehaveAsBefore()
    {
        string[] commands =
        [
            "/notes", "/open Plan", "/tags", "/tags work", "/links Plan", "/current", "/calendar-link",
            "/todo 2026-10-30 Next #home", "/done 1", "/current",
        ];
        string plainRoot = Path.Combine(parent, "Plain");
        Directory.CreateDirectory(plainRoot);
        SampleData.Write(plainRoot);
        SampleData.Write(root);
        using Session plain = new Session(plainRoot);
        plain.Run(commands);
        using (Session encrypting = new Session(root))
        {
            encrypting.Run("/encrypt", Pass, Pass);
        }

        using Session encrypted = new Session(root);
        encrypted.Console.Input.PushTextWithEnter(Pass);
        encrypted.Run(commands);

        string expected = plain.Console.Output.Replace(plainRoot, root, StringComparison.Ordinal);
        Assert.EndsWith(expected, encrypted.Console.Output, StringComparison.Ordinal);
        Assert.Contains(SampleData.NoteMarker, expected);
        Assert.Contains("Next", expected);
        SampleData.AssertNoMarkers(root);
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "todos.json"))));
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "calendar-link.ics"))));
        Assert.Empty(Directory.GetFiles(root, "context.db*"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    // AC-4: new writes are encrypted, and context.db is never created.
    [Fact]
    public void AfterEncrypt_NewNoteTodoAndLinkAreEncrypted()
    {
        using Session session = new Session(root);

        session.Run(
            "/encrypt", Pass, Pass, "/new Fresh idea", "/todo today secretTodo42", "/calendar-link " + FakeHttpHandler.Link,
            "/current", "/tags", "/open Fresh idea");

        string output = Plain(session.Console.Output);
        Assert.Contains("Created Fresh idea", output);
        Assert.Contains("secretTodo42", output);
        Assert.Contains("# Fresh idea", output);
        foreach (string name in new[] { "Fresh idea.md", "todos.json", "calendar-link.txt", "calendar-link.ics" })
        {
            Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, name))), name);
        }

        Assert.Empty(Directory.GetFiles(root, "context.db*"));
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain("secretTodo42", text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Standup", text, StringComparison.Ordinal);
        }
    }

    // AC-7: a tampered note shows the same red hint as an unreadable file and is never overwritten.
    [Fact]
    public void TamperedNote_ShowsCantRead_AndIsNotOverwritten()
    {
        SampleData.Write(root);
        using Session session = new Session(root);
        session.Run("/encrypt", Pass, Pass);
        byte[] tampered = SampleData.Tamper(Path.Combine(root, "Plan.md"));
        using Session again = new Session(root);
        again.Encryption.Unlock(Pass);

        again.Run("/open Plan", "/tags", "/links Other");

        Assert.Contains("Can't read Plan.", Plain(again.Console.Output));
        Assert.Equal(tampered, File.ReadAllBytes(Path.Combine(root, "Plan.md")));
    }

    // AC-7: a tampered todos.json shows Can't read todos.json and /todo doesn't overwrite it.
    [Fact]
    public void TamperedTodos_ShowsCantRead_AndIsNotOverwritten()
    {
        SampleData.Write(root);
        using Session session = new Session(root);
        session.Run("/encrypt", Pass, Pass);
        byte[] tampered = SampleData.Tamper(Path.Combine(root, "todos.json"));
        using Session again = new Session(root);
        again.Encryption.Unlock(Pass);

        again.Run("/todo today more", "/done 1");

        Assert.Contains("Can't read todos.json", Plain(again.Console.Output));
        Assert.Equal(tampered, File.ReadAllBytes(Path.Combine(root, "todos.json")));
    }

    // AC-8
    [Fact]
    public void Decrypt_CorrectPassphrase_RestoresOriginalBytes_AndTurnsOff()
    {
        SampleData.Write(root);
        Dictionary<string, string> original = SampleData.Hashes(root);
        using Session session = new Session(root);

        session.Run("/encrypt", Pass, Pass, "/decrypt", Pass);

        string output = Plain(session.Console.Output);
        Assert.Contains("Decrypting writes every file in plain text again.", output);
        Assert.Contains("Decrypted 6 files. Encryption is off.", output);
        Assert.False(session.Encryption.IsOn);
        Assert.False(Vault.Exists(root));
        Assert.Equal(original, SampleData.Hashes(root));
    }

    // AC-8
    [Fact]
    public void Decrypt_WrongPassphraseOrEscape_ChangesNothing()
    {
        SampleData.Write(root);
        using Session session = new Session(root);
        session.Run("/encrypt", Pass, Pass);
        Dictionary<string, string> encrypted = SampleData.Hashes(root);
        using Session again = new Session(root);
        again.Encryption.Unlock(Pass);
        again.Console.Input.PushTextWithEnter("/decrypt");
        again.Console.Input.PushTextWithEnter("not the passphrase");
        again.Console.Input.PushTextWithEnter("/decrypt");
        again.Console.Input.PushKey(ConsoleKey.Escape);

        again.Run();

        string output = Plain(again.Console.Output);
        Assert.Contains("Wrong passphrase. Nothing changed.", output);
        Assert.Contains("Decryption cancelled. Nothing changed.", output);
        Assert.True(again.Encryption.IsOn);
        Assert.Equal(encrypted, SampleData.Hashes(root));
    }

    [Fact]
    public void Decrypt_WhenOff_SaysSo()
    {
        using Session session = new Session(root);

        session.Run("/decrypt");

        Assert.Contains("Encryption is off.", Plain(session.Console.Output));
    }

    // AC-11
    [Fact]
    public void Passphrase_NeverShownOnScreen_OnlyBullets()
    {
        const string Secret = "ÆØÅæøåÆØÅæøå";
        SampleData.Write(root);
        using Session session = new Session(root);

        session.Run("/encrypt", Secret, Secret, "/decrypt", Secret);

        string output = session.Console.Output;
        Assert.DoesNotContain(output, c => "ÆØÅæøå".Contains(c));
        Assert.Contains(new string('•', Secret.Length), output);
        Assert.False(Vault.Exists(root));
        foreach (string path in Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(Secret, File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    // AC-11
    [Fact]
    public void Help_ListsTheNewCommands()
    {
        using Session session = new Session(root);

        session.Run("/help");

        string output = Plain(session.Console.Output);
        foreach (string name in new[] { "/encrypt", "/decrypt", "/export", "/import" })
        {
            Assert.Contains(name, output);
        }
    }

    // Feedback 3: /encrypt refuses to make a new key while encrypted files have lost theirs.
    [Fact]
    public void Encrypt_WithEncryptedFilesButNoKeyFile_IsRefused()
    {
        SampleData.Write(root);
        using (Session first = new Session(root))
        {
            first.Run("/encrypt", Pass, Pass);
        }

        File.Delete(Vault.KeyFilePath(root));
        File.WriteAllText(Path.Combine(root, "New.md"), "# New\n");
        Dictionary<string, string> before = SampleData.Hashes(root);
        using Session session = new Session(root);

        session.Run("/encrypt", Pass, Pass);

        Assert.Contains("Some files are encrypted but encryption.json is missing.", Plain(session.Console.Output));
        Assert.Equal(before, SampleData.Hashes(root));
        Assert.False(Vault.Exists(root));
    }

    // Feedback 1: the repeat may be typed in another Unicode form.
    [Fact]
    public void Encrypt_RepeatInOtherNormalizationForm_Matches_AndUnlocksLater()
    {
        string composed = "Ångström café passphrase".Normalize(System.Text.NormalizationForm.FormC);
        string decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        using Session session = new Session(root);
        session.Console.Input.PushTextWithEnter("/encrypt");
        foreach (string typed in new[] { composed, decomposed })
        {
            // TestConsole's PushText only takes key codes up to 255; combining marks need their own key info.
            foreach (char c in typed)
            {
                session.Console.Input.PushKey(new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false));
            }

            session.Console.Input.PushKey(ConsoleKey.Enter);
        }

        session.Run();

        Assert.True(session.Encryption.IsOn);
        new Encryption(root, VaultTests.TestIterations).CheckPassphrase(decomposed);
    }

    // Feedback 2: /decrypt with a weakened key file changes nothing.
    [Fact]
    public void Decrypt_KeyFileBelowMinimumIterations_ChangesNothing()
    {
        SampleData.Write(root);
        using (Session first = new Session(root))
        {
            first.Run("/encrypt", Pass, Pass);
        }

        Dictionary<string, string> before = SampleData.Hashes(root);
        using Session session = new Session(root, Vault.MinUnlockIterations);
        // The session is on (as if unlocked earlier); only the /decrypt check sees the weak key file.
        session.Encryption.Use(Vault.Unlock(root, Pass, VaultTests.TestIterations));

        session.Run("/decrypt", Pass);

        Assert.Contains("too few key iterations", Plain(session.Console.Output));
        Assert.Contains("Nothing changed.", Plain(session.Console.Output));
        Assert.Equal(before, SampleData.Hashes(root));
    }
}
