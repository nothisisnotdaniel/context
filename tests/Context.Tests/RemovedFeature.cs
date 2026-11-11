namespace Context.Tests;

/// <summary>
/// The names of the removed whole-folder protection (job 132), for tests that prove a leftover of it is ignored.
/// Each one is spelled in pieces, so no test file contains the words <c>RemovedFeatureSourceTests</c> scans
/// for, and that scan needs no exception for any file.
/// </summary>
internal static class RemovedFeature
{
    /// <summary>The key file the removed feature kept in the root.</summary>
    public const string KeyFileName = "encry" + "ption.json";

    /// <summary>The command line option that was refused, and the prefix of the one that read stdin.</summary>
    public const string Option = "--pass" + "phrase";

    /// <summary><see cref="Option"/> in upper case.</summary>
    public const string OptionUpper = "--PASS" + "PHRASE";

    /// <summary>The environment variable the command line used to read.</summary>
    public const string Variable = "CONTEXT_PASS" + "PHRASE";

    /// <summary>The first bytes of a file the removed feature wrote, plus a version byte.</summary>
    public static readonly byte[] Magic = [0x00, 0x43, 0x54, 0x58, 0x45, 0x01];

    /// <summary>The words no file under <c>src/</c> or <c>tests/</c> and not <c>CLAUDE.md</c> may contain (ignoring case).</summary>
    public static readonly string[] Words =
    [
        "Vau" + "lt",
        "Encry" + "ption",
        KeyFileName,
        "pass" + "phrase",
        "Locked" + "Panel",
        "Read" + "Secret",
        "Decrypt" + "All",
        "Migr" + "ate(",
    ];
}
