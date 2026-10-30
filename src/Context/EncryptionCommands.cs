using Spectre.Console;

namespace Context;

/// <summary>
/// The <c>/encrypt</c> and <c>/decrypt</c> commands over the session's <see cref="Encryption"/>. Passphrases are
/// read masked through <see cref="PassphrasePrompt"/>; they never appear in a hint, a message or an exception.
/// </summary>
internal static class EncryptionCommands
{
    /// <summary>The shortest passphrase <c>/encrypt</c> accepts.</summary>
    public const int MinPassphraseLength = 12;

    private const string NewLabel = "New passphrase: ";
    private const string RepeatLabel = "Repeat passphrase: ";

    /// <summary>Adds <c>/encrypt</c> and <c>/decrypt</c> to <paramref name="registry"/>.</summary>
    /// <param name="iterations">The PBKDF2 iteration count for a new vault; tests pass a low one.</param>
    public static void Register(
        CommandRegistry registry,
        Encryption encryption,
        PassphrasePrompt prompt,
        int iterations = Vault.DefaultIterations)
    {
        registry.Register("/encrypt", "Encrypt all data with a passphrase", _ => Encrypt(encryption, prompt, iterations));
        registry.Register("/decrypt", "Turn encryption off (writes plain files)", _ => Decrypt(encryption, prompt));
    }

    private static CommandResult Encrypt(Encryption encryption, PassphrasePrompt prompt, int iterations)
    {
        if (encryption.IsOn)
        {
            // Already on: encrypt what is still plain (an interrupted run, newly dropped files). No passphrase.
            return Encrypted(encryption.Migrate());
        }

        if (encryption.IsConfigured)
        {
            return CommandResult.Error($"{Vault.FileName} already exists. Restart Context to unlock it.");
        }

        string? passphrase = prompt.Ask(
            NewLabel,
            new Markup(
                "[grey]If you forget the passphrase, your data is lost: it can't be recovered. "
                + $"At least {MinPassphraseLength} characters. Esc cancels.[/]"));
        if (passphrase is null)
        {
            return Cancelled("Encryption");
        }

        if (passphrase.Length < MinPassphraseLength)
        {
            return CommandResult.Error(
                $"A passphrase needs at least {MinPassphraseLength} characters. Nothing changed.");
        }

        string? repeat = prompt.Ask(RepeatLabel, new Markup("[grey]Type the same passphrase again. Esc cancels.[/]"));
        if (repeat is null)
        {
            return Cancelled("Encryption");
        }

        if (!string.Equals(passphrase, repeat, StringComparison.Ordinal))
        {
            return CommandResult.Error("The passphrases don't match. Nothing changed.");
        }

        Vault vault;
        try
        {
            vault = Vault.Create(encryption.Root, passphrase, iterations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error($"Can't write {Vault.FileName}. Nothing changed.");
        }

        encryption.Use(vault);
        return Encrypted(encryption.Migrate());
    }

    private static CommandResult Decrypt(Encryption encryption, PassphrasePrompt prompt)
    {
        if (!encryption.IsOn)
        {
            return CommandResult.Error("Encryption is off. Type [bold]/encrypt[/] to turn it on.");
        }

        string? passphrase = prompt.Ask(
            PassphrasePrompt.Label,
            new Markup("[grey]Decrypting writes every file in plain text again. Esc cancels.[/]"));
        if (passphrase is null)
        {
            return Cancelled("Decryption");
        }

        try
        {
            // Only a check: the session's vault does the work.
            using Vault check = Vault.Unlock(encryption.Root, passphrase);
        }
        catch (WrongPassphraseException)
        {
            return CommandResult.Error("Wrong passphrase. Nothing changed.");
        }
        catch (VaultException)
        {
            return CommandResult.Error($"Can't read {Vault.FileName}. Nothing changed.");
        }

        MigrationResult result = encryption.DecryptAll();
        if (result.Failed.Count > 0)
        {
            return CommandResult.Error(
                $"Decrypted {Files(result.Converted)}. Couldn't decrypt {Names(result.Failed)}. "
                + "Encryption stays on; type [bold]/decrypt[/] to try again.");
        }

        return Grey($"Decrypted {Files(result.Converted)}. Encryption is off.");
    }

    private static CommandResult Encrypted(MigrationResult result)
    {
        if (result.Failed.Count > 0)
        {
            return CommandResult.Error(
                $"Encrypted {Files(result.Converted)}. Couldn't encrypt {Names(result.Failed)}; "
                + "they stay plain. Type [bold]/encrypt[/] to try again.");
        }

        return Grey(
            $"Encrypted {Files(result.Converted)}. Old plain copies may still exist on SSDs and in backups.");
    }

    private static CommandResult Cancelled(string what) => Grey($"{what} cancelled. Nothing changed.");

    private static CommandResult Grey(string markup) => new(hint: new Markup($"[grey]{markup}[/]"));

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string Names(IReadOnlyList<string> names) =>
        string.Join(", ", names.Select(n => Markup.Escape(SafeText.Clean(n))));
}
