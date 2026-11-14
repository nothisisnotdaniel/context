using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit.Sdk;

namespace Context.Tests;

/// <summary>
/// Checks the release flow: <c>.github/workflows/release.yml</c> (read only through <see cref="WorkflowYaml"/>), the
/// scripts in <c>.github/scripts/</c> and the Windows pack and upload steps and the publish step (run with
/// <c>bash</c>, no network; the steps against stub <c>vpk</c> and <c>gh</c> commands), the tool packaging in <c>Context.csproj</c>, the Velopack
/// version in <c>Context.Desktop.csproj</c> and the release entry in <c>CLAUDE.md</c>.
/// </summary>
public sealed class ReleaseWorkflowTests
{
    private const string SemVerGrep =
        "LC_ALL=C grep -Eq '^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?$'";

    private const string CheckoutAction = "actions/checkout@11d5960a326750d5838078e36cf38b85af677262";
    private const string SetupDotnetAction = "actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9";
    private const string JobIf =
        "github.event_name == 'push' || (github.event.pull_request.merged == true"
        + " && github.event.pull_request.head.ref == 'develop'"
        + " && github.event.pull_request.head.repo.full_name == github.repository)";

    private const string TestCommand = "dotnet test -c Release";
    private const string PackCommand = "dotnet pack src/Context -c Release -p:Version=\"$VERSION\"";
    private const string ReleaseCommand =
        "gh release create \"$TAG\" \"nupkg/Context.$VERSION.nupkg\" --title \"Context $VERSION\""
        + " --notes-file \"$RUNNER_TEMP/release-notes.md\" --generate-notes --target \"$TARGET\" --draft $PRERELEASE";

    private const string NotesCommand =
        "sh .github/scripts/release-notes.sh \"$MACOS_SIGNING\" > \"$RUNNER_TEMP/release-notes.md\"";

    private const string SigningVariable = "${{ vars.MACOS_SIGNING }}";
    private const string AdHocStep = "Bundle, ad-hoc sign and zip";
    private const string SigningStep = "Bundle, sign, notarize and zip";

    private static readonly string[] SigningSecrets =
    [
        "MACOS_CERTIFICATE_P12", "MACOS_CERTIFICATE_PASSWORD", "NOTARY_KEY_P8", "NOTARY_KEY_ID", "NOTARY_ISSUER_ID",
    ];

    private const string ViewCommand =
        "gh release view \"$TAG\" --json isDraft,targetCommitish,assets"
        + " --jq '.isDraft, .targetCommitish, .assets[].name'";

    private const string ReleaseTarget = "0123456789abcdef0123456789abcdef01234567";

    private const string PublishCommand =
        "dotnet publish src/Context.Desktop -c Release --self-contained -p:Version=\"$VERSION\"";

    private const string VersionOutput = "${{ needs.release.outputs.version }}";
    private const string TagOutput = "${{ needs.release.outputs.tag }}";
    private const string TargetOutput = "${{ needs.release.outputs.target }}";

    private static readonly string[] StepNames =
    [
        "Check out", "Set up .NET", "Version", "Test", "Pack", "Smoke check the packed tool",
        "Create GitHub release",
    ];

    private static readonly string[] PublishStepNames =
    [
        "Check out", "Check version", "Publish GitHub release", "Prune old prereleases",
    ];

    private static readonly string[] MacStepNames =
    [
        "Check out", "Check version", "Set up .NET", "Publish", AdHocStep, SigningStep, "Upload",
    ];

    private static readonly string[] WindowsStepNames =
    [
        "Check out", "Check version", "Set up .NET", "Install vpk", "Publish", "Pack", "Upload",
    ];

    private static readonly string[] JobNames = ["release", "desktop-macos", "desktop-windows", "publish"];

    private static readonly string RepoRoot = FindRepoRoot();

    // AC-1, AC-2, AC-3: triggers, permissions, the job if:, step names and order, pins, env-only PR fields.
    [Fact]
    public void Workflow_HasExpectedStructure()
    {
        AssertWorkflowStructure(ReadWorkflow());
    }

    // AC-3: the pinned uses: lines name their version in a trailing comment, and are the two of ci.yml.
    [Fact]
    public void Workflow_PinsTheSameActionsAsCi_ToCommitShasWithVersionComments()
    {
        string workflow = ReadWorkflow();
        List<YamlScalar> uses = [.. AllSteps(workflow)
            .Where(step => step.ContainsKey("uses"))
            .Select(step => Assert.IsType<YamlScalar>(step["uses"]))];
        string[] lines = workflow.Split('\n');
        string ci = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));

        // The same two actions in each of the first three jobs, only the checkout in publish, and no other.
        Assert.Equal(
            [
                CheckoutAction, SetupDotnetAction, CheckoutAction, SetupDotnetAction, CheckoutAction, SetupDotnetAction,
                CheckoutAction,
            ],
            uses.Select(scalar => scalar.Value));
        Assert.All(uses, scalar => Assert.EndsWith(
            scalar.Value == CheckoutAction ? " # v4.4.0" : " # v4.3.1",
            lines[scalar.Line - 1],
            StringComparison.Ordinal));
        Assert.All(uses, scalar => Assert.Matches("@[0-9a-f]{40}$", scalar.Value));
        Assert.Contains("uses: " + CheckoutAction + " # v4.4.0", ci, StringComparison.Ordinal);
        Assert.Contains("uses: " + SetupDotnetAction + " # v4.3.1", ci, StringComparison.Ordinal);
    }

    // 146 AC-1, AC-2: the exact list of secret-bearing steps, of vars. uses and of GH_TOKEN uses. The five secrets
    // reach only the env: of the signing step (which has no token), the switch only the job-level env: of release
    // and desktop-macos, and github.token the same six steps as before.
    [Fact]
    public void Workflow_ScopesSecretsToTheSigningStep_TheSwitchToTwoJobs_AndTheTokenToSixSteps()
    {
        string workflow = ReadWorkflow();
        YamlNode root = WorkflowYaml.Parse(workflow);
        YamlMapping jobs = Map(Map(root)["jobs"]);

        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token", workflow, StringComparison.Ordinal);

        // Step level: every env: entry that is not a PR field, as job/step: KEY=value.
        List<string> stepEnv = [.. jobs.Keys.SelectMany(job => JobSteps(root, job)
            .Where(step => step.ContainsKey("env"))
            .SelectMany(step => Map(step["env"]).Entries
                .Where(entry => !Scalar(entry.Value).StartsWith("${{ github.event.pull_request.", StringComparison.Ordinal))
                .Select(entry => job + "/" + Scalar(step["name"]) + ": " + entry.Key + "=" + Scalar(entry.Value))))];
        List<string> expected =
        [
            "release/Create GitHub release: GH_TOKEN=${{ github.token }}",
            .. SigningSecrets.Select(name => "desktop-macos/" + SigningStep + ": " + name + "=${{ secrets." + name + " }}"),
            "desktop-macos/Upload: GH_TOKEN=${{ github.token }}",
            "desktop-windows/Pack: GH_TOKEN=${{ github.token }}",
            "desktop-windows/Upload: GH_TOKEN=${{ github.token }}",
            "publish/Publish GitHub release: GH_TOKEN=${{ github.token }}",
            "publish/Prune old prereleases: GH_TOKEN=${{ github.token }}",
        ];
        Assert.Equal(expected, stepEnv);

        // Job level: the switch in release and desktop-macos, nothing but the release outputs elsewhere.
        List<string> jobEnv = [.. jobs.Keys
            .Where(job => Map(jobs[job]).ContainsKey("env"))
            .SelectMany(job => Map(Map(jobs[job])["env"]).Entries
                .Where(entry => !Scalar(entry.Value).StartsWith("${{ needs.release.outputs.", StringComparison.Ordinal))
                .Select(entry => job + ": " + entry.Key + "=" + Scalar(entry.Value)))];
        Assert.Equal(
            ["release: MACOS_SIGNING=" + SigningVariable, "desktop-macos: MACOS_SIGNING=" + SigningVariable], jobEnv);

        // The file text (comments included) names secrets., vars. and the token nowhere else.
        Assert.Equal(5, Regex.Matches(workflow, @"secrets\.").Count);
        Assert.Equal(2, Regex.Matches(workflow, @"vars\.").Count);
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(SigningVariable)).Count);
        Assert.Equal(6, Regex.Matches(workflow, Regex.Escape("${{ github.token }}")).Count);
        Assert.DoesNotContain("GITHUB_TOKEN", workflow, StringComparison.Ordinal);

        // Every step if: reads the switch from the job's env:, and only the two bundle steps have one.
        List<string> conditions = [.. jobs.Keys.SelectMany(job => JobSteps(root, job)
            .Where(step => step.ContainsKey("if"))
            .Select(step => job + "/" + Scalar(step["name"]) + ": " + Scalar(step["if"])))];
        Assert.Equal(
            [
                "desktop-macos/" + AdHocStep + ": env.MACOS_SIGNING != 'on'",
                "desktop-macos/" + SigningStep + ": env.MACOS_SIGNING == 'on'",
            ],
            conditions);

        // No script reads the switch or a secret any other way, and no script outside the macOS job signs.
        Assert.All(AllRunScripts(workflow), script =>
        {
            Assert.DoesNotContain("vars.", script, StringComparison.Ordinal);
            Assert.DoesNotContain("secrets.", script, StringComparison.Ordinal);
            Assert.DoesNotContain("openssl", script, StringComparison.Ordinal);
            Assert.DoesNotContain(".sig", script, StringComparison.Ordinal);
            Assert.DoesNotContain("set -x", script, StringComparison.Ordinal);
        });
        Assert.All(
            JobRunScripts(workflow, "release")
                .Concat(JobRunScripts(workflow, "desktop-windows"))
                .Concat(JobRunScripts(workflow, "publish")),
            script =>
            {
                Assert.DoesNotContain("codesign", script, StringComparison.Ordinal);
                Assert.DoesNotContain("notarytool", script, StringComparison.Ordinal);
                Assert.DoesNotContain("macos-sign", script, StringComparison.Ordinal);
                Assert.All(SigningSecrets, name => Assert.DoesNotContain(name, script, StringComparison.Ordinal));
            });

        // The only codesign calls in the workflow itself are the ad-hoc path's; the signed path goes through the scripts.
        List<string> codesign = [.. AllRunScripts(workflow)
            .SelectMany(script => script.Split('\n'))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("codesign ", StringComparison.Ordinal))];
        Assert.Equal(
            [
                "codesign --force --deep -s - \"app/$RID/Context.app\"",
                "codesign --verify --deep --strict \"app/$RID/Context.app\"",
            ],
            codesign);
    }

    // 146 AC-10: the Windows job is byte-for-byte what it was before signing came to the macOS job, and nothing
    // in the workflow signs a Windows build.
    [Fact]
    public void WindowsJob_IsUnchanged_AndNothingSignsForWindows()
    {
        string workflow = ReadWorkflow();
        int start = workflow.IndexOf("  desktop-windows:\n", StringComparison.Ordinal);
        int end = workflow.IndexOf("\n  # Makes the draft public", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the desktop-windows job is not where it was");
        string job = workflow[start..end];

        Assert.Equal(
            "DF58DE1BFB2167C2B48D34C9B1DEC048D443E5E7DF06BCD62930328E6B9795EF",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(job))));
        Assert.All(AllRunScripts(workflow), script =>
        {
            Assert.DoesNotContain("signtool", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("--azure", script, StringComparison.Ordinal);
            Assert.DoesNotContain("--sign", script, StringComparison.Ordinal);
        });
    }

    // AC-12: the structure check reads only the parsed model, so reformatting the YAML keeps it passing.
    [Theory]
    [InlineData("develop")]
    [InlineData("'develop'")]
    public void StructureCheck_PassesOnReformattedCopy(string develop)
    {
        string reformatted = ReformattedWorkflow(ReadWorkflow(), develop);

        Assert.NotEqual(ReadWorkflow(), reformatted);
        Assert.Contains("\n            -   " + develop + "\n", reformatted, StringComparison.Ordinal);
        AssertWorkflowStructure(reformatted);
    }

    // AC-12: the structure check fails on a wider token, a tag-pinned action, pull_request_target and an
    // expression inside a run: script. Every copy is still valid YAML, so it is the check that fails.
    [Theory]
    [InlineData("write-all")]
    [InlineData("more permissions")]
    [InlineData("tag-pinned checkout")]
    [InlineData("pull_request_target")]
    [InlineData("extra trigger")]
    [InlineData("expression in run")]
    [InlineData("expression in a run block")]
    [InlineData("tag-pinned checkout in a desktop job")]
    [InlineData("third action in a desktop job")]
    [InlineData("expression in a desktop run")]
    [InlineData("credentials kept in a desktop job")]
    [InlineData("desktop job without needs")]
    [InlineData("desktop job with its own permissions")]
    [InlineData("secret in a desktop job")]
    [InlineData("secret at job level in release")]
    [InlineData("secret in a release step")]
    [InlineData("secret in a desktop-windows step")]
    [InlineData("signing step without if")]
    [InlineData("ad-hoc step without if")]
    [InlineData("if that reads vars")]
    [InlineData("signing step with GH_TOKEN")]
    [InlineData("secret in the upload step")]
    [InlineData("switch in desktop-windows")]
    [InlineData("publish without the macOS job")]
    [InlineData("publish without the Windows job")]
    [InlineData("publish with its own permissions")]
    [InlineData("publish with an if")]
    [InlineData("expression in a publish run")]
    [InlineData("credentials kept in publish")]
    [InlineData("release not created as a draft")]
    [InlineData("second script that publishes")]
    [InlineData("prune before publish")]
    public void StructureCheck_FailsOnUnsafeCopy(string change)
    {
        string workflow = ReadWorkflow();
        string copy = change switch
        {
            "write-all" => ReplaceOnce(workflow, "permissions:\n  contents: write\n", "permissions: write-all\n"),
            "more permissions" => ReplaceOnce(
                workflow,
                "permissions:\n  contents: write\n",
                "permissions:\n  contents: write\n  id-token: write\n"),
            "tag-pinned checkout" => ReplaceNth(workflow, CheckoutAction + " # v4.4.0", "actions/checkout@v4", 0),
            "tag-pinned checkout in a desktop job" =>
                ReplaceNth(workflow, CheckoutAction + " # v4.4.0", "actions/checkout@v4", 2),
            "third action in a desktop job" => ReplaceOnce(
                workflow,
                "      - name: Install vpk\n        run: ",
                "      - name: Install vpk\n        uses: actions/cache@0000000000000000000000000000000000000000 # v4.0.0\n"
                    + "      - name: Install vpk again\n        run: "),
            "expression in a desktop run" => ReplaceOnce(
                workflow,
                "gh release upload \"$TAG\" \"$@\" --clobber",
                "gh release upload \"${{ needs.release.outputs.tag }}\" \"$@\" --clobber"),
            "credentials kept in a desktop job" => ReplaceNth(
                workflow, "          persist-credentials: false\n", "          persist-credentials: true\n", 2),
            "desktop job without needs" => ReplaceNth(workflow, "    needs: release\n", string.Empty, 1),
            "desktop job with its own permissions" => ReplaceNth(
                workflow, "    needs: release\n", "    needs: release\n    permissions: write-all\n", 0),
            "secret in a desktop job" => ReplaceNth(
                workflow,
                "      TAG: ${{ needs.release.outputs.tag }}\n",
                "      TAG: ${{ needs.release.outputs.tag }}\n      KEY: ${{ secrets.SIGNING_KEY }}\n",
                0),
            // 146 AC-1, AC-2: a secret only in the signing step's env:, which has its if: and no token; the switch
            // only through the job-level env: of release and desktop-macos.
            "secret at job level in release" => ReplaceNth(
                workflow,
                "      MACOS_SIGNING: " + SigningVariable + "\n",
                "      MACOS_SIGNING: " + SigningVariable + "\n      KEY: ${{ secrets.NOTARY_KEY_P8 }}\n",
                0),
            "secret in a release step" => ReplaceNth(
                workflow,
                "          GH_TOKEN: ${{ github.token }}\n",
                "          GH_TOKEN: ${{ github.token }}\n          KEY: ${{ secrets.NOTARY_KEY_P8 }}\n",
                0),
            "secret in a desktop-windows step" => ReplaceNth(
                workflow,
                "          GH_TOKEN: ${{ github.token }}\n",
                "          GH_TOKEN: ${{ github.token }}\n          KEY: ${{ secrets.NOTARY_KEY_P8 }}\n",
                2),
            "secret in the upload step" => ReplaceNth(
                workflow,
                "          GH_TOKEN: ${{ github.token }}\n",
                "          GH_TOKEN: ${{ github.token }}\n          KEY: ${{ secrets.NOTARY_KEY_P8 }}\n",
                1),
            "signing step without if" => ReplaceOnce(workflow, "        if: env.MACOS_SIGNING == 'on'\n", string.Empty),
            "ad-hoc step without if" => ReplaceOnce(workflow, "        if: env.MACOS_SIGNING != 'on'\n", string.Empty),
            "if that reads vars" => ReplaceOnce(
                workflow, "        if: env.MACOS_SIGNING == 'on'\n", "        if: vars.MACOS_SIGNING == 'on'\n"),
            "signing step with GH_TOKEN" => ReplaceOnce(
                workflow,
                "          NOTARY_ISSUER_ID: ${{ secrets.NOTARY_ISSUER_ID }}\n",
                "          NOTARY_ISSUER_ID: ${{ secrets.NOTARY_ISSUER_ID }}\n          GH_TOKEN: ${{ github.token }}\n"),
            "switch in desktop-windows" => ReplaceNth(
                workflow,
                "      TAG: ${{ needs.release.outputs.tag }}\n",
                "      TAG: ${{ needs.release.outputs.tag }}\n      MACOS_SIGNING: " + SigningVariable + "\n",
                1),
            // 135 AC-9: the publish job must wait for both desktop jobs, keep the workflow's token scope, never run
            // after a failed job, take its values only through env:, keep no credentials, and the release must
            // start as a draft that only the publish step makes public.
            "publish without the macOS job" => ReplaceOnce(workflow, "      - desktop-macos\n", string.Empty),
            "publish without the Windows job" => ReplaceOnce(workflow, "      - desktop-windows\n", string.Empty),
            "publish with its own permissions" => ReplaceOnce(
                workflow, "  publish:\n", "  publish:\n    permissions: write-all\n"),
            "publish with an if" => ReplaceOnce(workflow, "  publish:\n", "  publish:\n    if: always()\n"),
            "expression in a publish run" => ReplaceOnce(
                workflow,
                "gh release edit \"$TAG\" --draft=false $PRE",
                "gh release edit \"${{ needs.release.outputs.tag }}\" --draft=false $PRE"),
            "credentials kept in publish" => ReplaceNth(
                workflow, "          persist-credentials: false\n", "          persist-credentials: true\n", 3),
            "release not created as a draft" => ReplaceOnce(
                workflow, "--target \"$TARGET\" --draft $PRERELEASE", "--target \"$TARGET\" $PRERELEASE"),
            "second script that publishes" => ReplaceOnce(
                workflow,
                "gh release upload \"$TAG\" \"$@\" --clobber",
                "gh release upload \"$TAG\" \"$@\" --clobber\n          gh release edit \"$TAG\" --draft=false"),
            "prune before publish" => ReplaceOnce(
                ReplaceOnce(workflow, "      - name: Publish GitHub release\n", "      - name: Prune old prereleases\n"),
                "      - name: Prune old prereleases\n        continue-on-error: true\n",
                "      - name: Publish GitHub release\n        continue-on-error: true\n"),
            "pull_request_target" => ReplaceOnce(workflow, "\n  pull_request:\n", "\n  pull_request_target:\n"),
            "extra trigger" => ReplaceOnce(workflow, "\non:\n", "\non:\n  workflow_dispatch:\n"),
            "expression in run" => ReplaceOnce(
                workflow,
                "run: " + TestCommand + "\n",
                "run: " + TestCommand + " ${{ github.ref_name }}\n"),
            _ => ReplaceOnce(
                workflow,
                "          PRERELEASE=\"\"\n",
                "          PRERELEASE=\"\"\n          echo \"${{ github.event.pull_request.title }}\"\n"),
        };

        WorkflowYaml.Parse(copy);
        Assert.ThrowsAny<XunitException>(() => AssertWorkflowStructure(copy));
    }

    // AC-13 (and the spec's "backlog-only pushes"): the header comment says what the workflow does.
    [Fact]
    public void Workflow_HeaderCommentDescribesTheFlow()
    {
        string workflow = ReadWorkflow();
        int name = workflow.IndexOf("\nname: Release\n", StringComparison.Ordinal);
        Assert.True(name >= 0, "workflow has no 'name: Release' line");
        string header = workflow[..name];

        Assert.Contains("backlog.md", header, StringComparison.Ordinal);
        Assert.Contains("release vX.Y.Z", header, StringComparison.Ordinal);
        Assert.Contains("X.Y.(Z+1)-develop.<run number>", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Times", workflow, StringComparison.Ordinal);

        // 133 AC-13: what ships, the two channels, the updater and its trust model.
        string flat = Regex.Replace(header.Replace("\n#", " ", StringComparison.Ordinal), @"\s+", " ");
        string[] expected =
        [
            "Context.<version>.nupkg",
            "Context-<version>-osx-arm64.zip",
            "Context-<version>-osx-x64.zip",
            "Setup.exe",
            "portable zip",
            "full .nupkg",
            "delta .nupkg",
            "releases.<channel>.json",
            "--channel develop",
            "default channel (win)",
            "looks at start-up for a newer build",
            // 146 AC-11: the switch, every secret by name, the scripts, the entitlements file and the trust model.
            "the macOS app can be signed and notarized, the Windows build is unsigned, and the updater checks no signature",
            "repository variable MACOS_SIGNING (empty, off or on",
            "MACOS_CERTIFICATE_P12",
            "MACOS_CERTIFICATE_PASSWORD",
            "NOTARY_KEY_P8",
            "NOTARY_KEY_ID",
            "NOTARY_ISSUER_ID",
            ".github/scripts/macos-sign.sh",
            ".github/scripts/macos-notarize.sh",
            ".github/scripts/release-notes.sh",
            ".github/scripts/macos-entitlements.plist",
            "allow-jit only",
            "It fails closed",
            "nothing falls back to the ad-hoc signature",
            "ad-hoc signature",
            "checks no signature",
            "HTTPS from this repository's releases, which only this workflow writes",
            "github.token is the only credential",
            "--clobber",
            // 135 AC-11: draft first, published last, and what a failed desktop job leaves behind.
            "Four jobs",
            "The release is a draft until the publish job runs",
            "A failed desktop job leaves an unpublished draft",
            "re-run the failed job",
            "the publish job deletes every -develop prerelease below X.Y.Z",
            "This is what makes immutable releases work",
        ];
        Assert.All(expected, text => Assert.Contains(text, flat, StringComparison.Ordinal));
        Assert.DoesNotContain("there is no desktop build", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("leaves a release with fewer assets", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing is signed", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-header", workflow, StringComparison.Ordinal);
    }

    // AC-2: the version is set first, the tests run on the normal build, then the pack takes the version.
    [Fact]
    public void Workflow_TestsBeforePack_AndPacksWithTheVersion()
    {
        List<string> scripts = RunScripts(ReadWorkflow());
        string all = string.Join("\n", scripts);

        int export = all.IndexOf("echo \"VERSION=$VERSION\" >> \"$GITHUB_ENV\"", StringComparison.Ordinal);
        int test = all.IndexOf(TestCommand, StringComparison.Ordinal);
        int pack = all.IndexOf(PackCommand, StringComparison.Ordinal);
        Assert.True(export >= 0, "VERSION is not written to $GITHUB_ENV");
        Assert.True(test > export, "dotnet test must run after VERSION is set");
        Assert.True(pack > test, "dotnet pack must run after dotnet test");
        Assert.Equal(TestCommand, Scalar(Step(ReadWorkflow(), "Test")["run"]).Trim());
        Assert.Equal(PackCommand, Scalar(Step(ReadWorkflow(), "Pack")["run"]).Trim());
    }

    // AC-3: no run: script interpolates an expression; refs and PR fields arrive as environment variables.
    [Fact]
    public void Workflow_RunScriptsHaveNoExpressionInterpolation()
    {
        List<string> scripts = RunScripts(ReadWorkflow());

        Assert.Equal(5, scripts.Count);
        Assert.All(scripts, script => Assert.DoesNotContain("${{", script, StringComparison.Ordinal));

        // 133 AC-1, 135 AC-4: the same for every run: script of the two desktop jobs and of publish.
        List<string> all = AllRunScripts(ReadWorkflow());
        Assert.Equal(5 + 5 + 5 + 3, all.Count);
        Assert.All(all, script => Assert.DoesNotContain("${{", script, StringComparison.Ordinal));
        Assert.All(all, script => Assert.DoesNotContain("eval", script, StringComparison.Ordinal));
    }

    // AC-4: pull_request first, then the develop push, then the tag push; one SemVer check after all three.
    [Fact]
    public void VersionStep_HasThreeBranches_ThenOneSemVerCheck()
    {
        string script = VersionScript();

        int pr = script.IndexOf("if [ \"$GITHUB_EVENT_NAME\" = \"pull_request\" ]; then", StringComparison.Ordinal);
        int prTarget = script.IndexOf("TARGET=\"$MERGE_SHA\"", StringComparison.Ordinal);
        int branch = script.IndexOf("elif [ \"$GITHUB_REF_TYPE\" = \"branch\" ]; then", StringComparison.Ordinal);
        int compute = script.IndexOf(
            "VERSION=\"$(git tag --list 'v*' | sh .github/scripts/develop-version.sh \"$GITHUB_RUN_NUMBER\")\"",
            StringComparison.Ordinal);
        int tag = script.IndexOf("TAG=\"v$VERSION\"", branch, StringComparison.Ordinal);
        int developTarget = script.IndexOf("TARGET=\"$GITHUB_SHA\"", branch, StringComparison.Ordinal);
        int otherwise = LineIndex(script, "else", branch);
        int fromRef = script.IndexOf("VERSION=\"${GITHUB_REF_NAME#v}\"", StringComparison.Ordinal);
        int refTag = script.IndexOf("TAG=\"$GITHUB_REF_NAME\"", StringComparison.Ordinal);
        int refTarget = script.IndexOf("TARGET=\"$GITHUB_SHA\"", refTag, StringComparison.Ordinal);
        int fi = LineIndex(script, "fi", refTarget);
        int check = script.IndexOf(SemVerGrep, StringComparison.Ordinal);

        Assert.True(pr >= 0, "no GITHUB_EVENT_NAME pull_request test");
        Assert.True(pr < prTarget && prTarget < branch, "the pull_request branch must come first and set TARGET");
        Assert.True(
            branch < compute && compute < tag && tag < developTarget && developTarget < otherwise,
            "the develop branch must compute VERSION, TAG and TARGET");
        Assert.True(
            otherwise < fromRef && fromRef < refTag && refTag < refTarget && refTarget < fi,
            "a tag push must take VERSION and TAG from the ref and TARGET from GITHUB_SHA");
        Assert.True(fi < check, "the SemVer check must come after all three branches");
    }

    // AC-4: the release PR branch re-checks what the job's if: checked, and the merge commit, before the version.
    [Fact]
    public void VersionStep_PullRequestBranchRechecksPrAndMergeCommit()
    {
        string script = VersionScript();
        int pr = script.IndexOf("if [ \"$GITHUB_EVENT_NAME\" = \"pull_request\" ]; then", StringComparison.Ordinal);
        string[] checks =
        [
            "if [ \"$PR_MERGED\" != \"true\" ]; then",
            "if [ \"$PR_HEAD_REF\" != \"develop\" ]; then",
            "if [ \"$PR_HEAD_REPO\" != \"$GITHUB_REPOSITORY\" ]; then",
            "'' | *[!0-9a-f]*)",
            "if [ \"${#MERGE_SHA}\" -ne 40 ]; then",
            "if [ \"$(git rev-parse HEAD)\" != \"$MERGE_SHA\" ]; then",
            "if ! git merge-base --is-ancestor \"$MERGE_SHA\" origin/main; then",
            "if ! VERSION=\"$(git tag --list 'v*' | sh .github/scripts/release-pr-version.sh \"$PR_TITLE\")\"; then",
        ];
        int at = pr;
        foreach (string check in checks)
        {
            int next = script.IndexOf(check, at, StringComparison.Ordinal);
            Assert.True(next > at, "'" + check + "' missing or out of order");
            int end = script.IndexOf("elif [", next, StringComparison.Ordinal);
            string closer = check.EndsWith(')') ? ";;" : "fi\n";
            string after = script[next..script.IndexOf(closer, next, StringComparison.Ordinal)];

            Assert.True(end > next, "'" + check + "' must be in the pull_request branch");
            Assert.Contains(">&2", after, StringComparison.Ordinal);
            Assert.Contains("exit 1", after, StringComparison.Ordinal);
            at = next;
        }

        int tag = script.IndexOf("TAG=\"v$VERSION\"", at, StringComparison.Ordinal);
        int target = script.IndexOf("TARGET=\"$MERGE_SHA\"", at, StringComparison.Ordinal);
        Assert.True(at < tag && tag < target, "TAG and TARGET must be set after the version");

        string branch = script[pr..script.IndexOf("elif [", pr, StringComparison.Ordinal)];
        Assert.DoesNotContain("eval", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event", branch, StringComparison.Ordinal);

        string all = string.Join("\n", RunScripts(ReadWorkflow()));
        Assert.True(
            all.IndexOf("release-pr-version.sh", StringComparison.Ordinal)
                < all.IndexOf("dotnet test", StringComparison.Ordinal),
            "the PR checks must run before dotnet test");
    }

    // AC-4: a pushed tag is refused when it is a -develop tag or not on main, before its version is taken.
    [Fact]
    public void VersionStep_TagPushRefusesDevelopTagsAndTagsNotOnMain()
    {
        string all = string.Join("\n", RunScripts(ReadWorkflow()));
        string script = VersionScript();

        int otherwise = LineIndex(script, "else", 0);
        int develop = script.IndexOf("*-develop*)", StringComparison.Ordinal);
        int main = script.IndexOf(
            "if ! git merge-base --is-ancestor \"$GITHUB_SHA\" origin/main; then",
            StringComparison.Ordinal);
        int fromRef = script.IndexOf("VERSION=\"${GITHUB_REF_NAME#v}\"", StringComparison.Ordinal);
        Assert.True(otherwise >= 0 && develop > otherwise, "no -develop refusal in the tag branch");
        Assert.True(main > develop, "no main-ancestor check after the -develop refusal");
        Assert.True(fromRef > main, "both checks must run before VERSION is taken from the tag");
        Assert.Contains(">&2", script[develop..main], StringComparison.Ordinal);
        Assert.Contains("exit 1", script[develop..main], StringComparison.Ordinal);
        Assert.Contains(">&2", script[main..fromRef], StringComparison.Ordinal);
        Assert.Contains("exit 1", script[main..fromRef], StringComparison.Ordinal);

        int test = all.IndexOf("dotnet test", StringComparison.Ordinal);
        Assert.True(all.IndexOf("--is-ancestor", StringComparison.Ordinal) < test, "main check before dotnet test");
        Assert.True(all.IndexOf("*-develop*)", StringComparison.Ordinal) < test, "-develop check before dotnet test");
    }

    // AC-4: the version is checked against SemVer under LC_ALL=C where it enters, before it is exported.
    [Fact]
    public void VersionStep_ChecksSemVerBeforeExportingVersionTagAndTarget()
    {
        string script = VersionScript();

        int check = script.IndexOf(SemVerGrep, StringComparison.Ordinal);
        int export = script.IndexOf(">> \"$GITHUB_ENV\"", StringComparison.Ordinal);
        Assert.True(check >= 0, "no SemVer check on the version");
        Assert.True(check < export, "the version must be checked before it is exported");
        Assert.Contains("exit 1", script[check..export], StringComparison.Ordinal);
        Assert.Single(Regex.Matches(script, @"\bgrep\b"));
        Assert.Contains("| " + SemVerGrep + "; then", script, StringComparison.Ordinal);
        Assert.Contains("echo \"VERSION=$VERSION\" >> \"$GITHUB_ENV\"", script[check..], StringComparison.Ordinal);
        Assert.Contains("echo \"TAG=$TAG\" >> \"$GITHUB_ENV\"", script[check..], StringComparison.Ordinal);
        Assert.Contains("echo \"TARGET=$TARGET\" >> \"$GITHUB_ENV\"", script[check..], StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(script, Regex.Escape(">> \"$GITHUB_ENV\"")).Count);

        // 133: the same three values become the job outputs the desktop jobs read, also only after the check.
        Assert.Contains("echo \"version=$VERSION\" >> \"$GITHUB_OUTPUT\"", script[check..], StringComparison.Ordinal);
        Assert.Contains("echo \"tag=$TAG\" >> \"$GITHUB_OUTPUT\"", script[check..], StringComparison.Ordinal);
        Assert.Contains("echo \"target=$TARGET\" >> \"$GITHUB_OUTPUT\"", script[check..], StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(script, Regex.Escape(">> \"$GITHUB_OUTPUT\"")).Count);
    }

    // AC-5
    [Theory]
    [InlineData("v0.1.0-beta\n", "12", "0.1.1-develop.12", true)]
    [InlineData("v0.1.0\n", "12", "0.1.1-develop.12", true)]
    [InlineData("v0.1.0-beta\nv0.1.0\nv0.1.1-develop.5\n", "12", "0.1.1-develop.12", true)]
    [InlineData("v0.2.0\nv0.10.0\nv0.9.3\n", "12", "0.10.1-develop.12", true)]
    [InlineData("v1.2\nvx\n", "12", "0.0.1-develop.12", true)]
    [InlineData("v9.9.9-develop.3\n", "7", "0.0.1-develop.7", true)]
    [InlineData("", "12", "0.0.1-develop.12", true)]
    [InlineData("v0.1.0\n", "0", "", false)]
    [InlineData("v0.1.0\n", "012", "", false)]
    [InlineData("v0.1.0\n", "-1", "", false)]
    [InlineData("v0.1.0\n", "abc", "", false)]
    [InlineData("v0.1.0\n", "12;x", "", false)]
    [InlineData("v0.1.0\n", "$(id)", "", false)]
    [InlineData("v0.1.0\n", "", "", false)]
    // A non-ASCII prerelease is ignored even in a UTF-8 locale.
    [InlineData("v0.1.0\nv9.0.0-bétà\n", "12", "0.1.1-develop.12", true, "en_US.UTF-8")]
    // Tags with a MAJOR, MINOR or PATCH of more than 9 digits are ignored.
    [InlineData("v0.1.0\nv1000000000.0.0\n", "12", "0.1.1-develop.12", true)]
    [InlineData("v0.0.9223372036854775807\n", "12", "0.0.1-develop.12", true)]
    [InlineData("v99999999999999999999.0.0\nv0.2.0\n", "12", "0.2.1-develop.12", true)]
    [InlineData("v999999999.999999999.999999999\n", "12", "999999999.999999999.1000000000-develop.12", true)]
    [InlineData("v0.1000000000.0\nv0.0.1000000000\nv0.0.3\n", "12", "0.0.4-develop.12", true, "en_US.UTF-8")]
    public async Task DevelopVersionScript_PrintsNextPatchDevelopVersion(
        string tags, string build, string expected, bool succeeds, string locale = "")
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ScriptResult result = await RunScript("develop-version.sh", build, tags, locale);

        Assert.Equal(expected, result.Out.Trim());
        if (succeeds)
        {
            Assert.True(result.Code == 0, "develop-version.sh exit " + result.Code + ": " + result.Err);
        }
        else
        {
            Assert.NotEqual(0, result.Code);
            Assert.NotEqual(string.Empty, result.Err);
        }
    }

    // AC-6: only a plain X.Y.Z comes out. (Times' copy of this script also lets a prerelease label through; here
    // the one pattern line is tighter, see CLAUDE.md.)
    [Theory]
    [InlineData("release v1.2.3", "", "1.2.3", true)]
    [InlineData("release v0.1.0", "", "0.1.0", true)]
    [InlineData("release v1.2.3", "v1.2.30\nv1.2.2\nv1.2.3-develop.4\n", "1.2.3", true)]
    [InlineData("release v1.2.3", "v1.2.30\nv1.2.2", "1.2.3", true)]
    [InlineData("release v1.2.3", "", "1.2.3", true, "en_US.UTF-8")]
    [InlineData("release v1.2.3", "v1.2.2\nv1.2.3\n", "", false)]
    [InlineData("release v1.2.3", "v1.2.2\nv1.2.3", "", false)]
    [InlineData("Release v1.2.3", "", "", false)]
    [InlineData("release 1.2.3", "", "", false)]
    [InlineData(" release v1.2.3", "", "", false)]
    [InlineData("release v1.2.3 ", "", "", false)]
    [InlineData("release  v1.2.3", "", "", false)]
    [InlineData("release v1.2.3 now", "", "", false)]
    [InlineData("release v1.2", "", "", false)]
    [InlineData("release v01.2.3", "", "", false)]
    [InlineData("release v1.2.3.4", "", "", false)]
    [InlineData("release v1.2.3+abc", "", "", false)]
    [InlineData("release v1.2.3-develop.4", "", "", false)]
    [InlineData("release v1.2.3-beta.1", "", "", false)]
    [InlineData("release v1.2.3-rc1", "", "", false)]
    [InlineData("release v1.2.3-", "", "", false)]
    [InlineData("release v1.2.3;x", "", "", false)]
    [InlineData("release v1.2.3; touch MARKER", "", "", false)]
    [InlineData("release v1.2.3\nrelease v1.2.4", "", "", false)]
    [InlineData("release v1.2.3\n", "", "", false)]
    [InlineData("release v1.2.3\r", "", "", false)]
    [InlineData("release v$(touch MARKER)", "", "", false)]
    [InlineData("release v`touch MARKER`", "", "", false)]
    [InlineData("release v1.2.3-béta", "", "", false, "en_US.UTF-8")]
    [InlineData("", "", "", false)]
    public async Task ReleasePrVersionScript_AcceptsOnlyExactReleaseTitleForNewTag(
        string title, string tags, string expected, bool succeeds, string locale = "")
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ScriptResult result = await RunScript("release-pr-version.sh", title, tags, locale);

        // Nothing in the title was run: the scripts start in a fresh folder, which stays empty.
        Assert.Empty(result.FilesCreated);
        if (succeeds)
        {
            Assert.Equal(expected + "\n", result.Out);
            Assert.True(result.Code == 0, "release-pr-version.sh exit " + result.Code + ": " + result.Err);
        }
        else
        {
            Assert.Equal(string.Empty, result.Out);
            Assert.NotEqual(0, result.Code);
            Assert.NotEqual(string.Empty, result.Err);
        }
    }

    // AC-6: the title is only compared, never evaluated.
    [Fact]
    public void ReleasePrVersionScript_NeverEvaluatesTheTitle()
    {
        string source = ScriptSource("release-pr-version.sh");
        string code = CodeLines(source);

        Assert.StartsWith("#!/bin/sh\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", code, StringComparison.Ordinal);
        Assert.Contains("title=\"${1-}\"", code, StringComparison.Ordinal);
        Assert.Contains("LC_ALL=C\nexport LC_ALL\n", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[^""{]\$title", code);
        Assert.DoesNotMatch(@"[^""]\$version", code.Replace("v$version", "", StringComparison.Ordinal));
    }

    // AC-7
    [Theory]
    [InlineData(
        "1.2.3",
        "v1.2.3-develop.5\nv1.2.2-develop.1\nv0.9.10-develop.3\nv1.2.4-develop.9\nv1.3.0-develop.2\nv2.0.0-develop.1\n",
        "v1.2.3-develop.5\nv1.2.2-develop.1\nv0.9.10-develop.3",
        true)]
    [InlineData("1.2.3", "v1.2.2-develop.1", "v1.2.2-develop.1", true)]
    [InlineData(
        "1.2.3",
        "v1.2.2\nv1.2.3\nv1.0.0-beta.1\nv1.2.2-develop\nv1.2.2-develop.0\nv1.2.2-develop.01\nv1.2.2-develop.1.x\n"
            + "v01.2.2-develop.1\nx v1.2.2-develop.1\nv1.2.2-develop.1;rm\nv1.2.2-develop.$(touch MARKER)\n"
            + "`touch MARKER`\n\n \n",
        "",
        true)]
    [InlineData("0.10.0", "v0.9.3-develop.1\nv0.10.1-develop.1\n", "v0.9.3-develop.1", true)]
    [InlineData("0.10.0", "v0.9.3-develop.1\nv0.9.3-dévelop.1\n", "v0.9.3-develop.1", true, "en_US.UTF-8")]
    [InlineData("1.2.3", "v1.2.3-develop.5\r\n", "", true)]
    [InlineData("1.2.3", "", "", true)]
    [InlineData("", "v1.2.2-develop.1\n", "", false)]
    [InlineData("1.2", "v1.2.2-develop.1\n", "", false)]
    [InlineData("1.2.3-beta.1", "v1.2.2-develop.1\n", "", false)]
    [InlineData("1.2.3-develop.4", "v1.2.2-develop.1\n", "", false)]
    [InlineData("v1.2.3", "v1.2.2-develop.1\n", "", false)]
    [InlineData("01.2.3", "v1.2.2-develop.1\n", "", false)]
    [InlineData("1.2.3;x", "v1.2.2-develop.1\n", "", false)]
    [InlineData("1.2.3\n", "v1.2.2-develop.1\n", "", false)]
    public async Task OldPrereleasesScript_PrintsOnlyOlderDevelopTags(
        string release, string tags, string expected, bool succeeds, string locale = "")
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ScriptResult result = await RunScript("old-prereleases.sh", release, tags, locale);

        Assert.Empty(result.FilesCreated);
        Assert.Equal(expected, result.Out.TrimEnd('\n'));
        if (succeeds)
        {
            Assert.True(result.Code == 0, "old-prereleases.sh exit " + result.Code + ": " + result.Err);
        }
        else
        {
            Assert.NotEqual(0, result.Code);
        }
    }

    // AC-7: tag names are only compared, never evaluated.
    [Fact]
    public void OldPrereleasesScript_NeverEvaluatesTags()
    {
        string source = ScriptSource("old-prereleases.sh");
        string code = CodeLines(source);

        Assert.StartsWith("#!/bin/sh\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", code, StringComparison.Ordinal);
        Assert.Contains("while IFS= read -r tag", code, StringComparison.Ordinal);
        Assert.Contains("LC_ALL=C\nexport LC_ALL\n", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[^""{]\$tag", code);
    }

    // AC-5: the develop script runs in the C locale before its first grep.
    [Fact]
    public void DevelopVersionScript_SetsCLocaleBeforeFirstGrep()
    {
        string code = CodeLines(ScriptSource("develop-version.sh"));

        int locale = code.IndexOf("\nLC_ALL=C\nexport LC_ALL\n", StringComparison.Ordinal);
        int grep = code.IndexOf("grep", StringComparison.Ordinal);
        Assert.True(locale >= 0, "develop-version.sh must set and export LC_ALL=C on lines of their own");
        Assert.True(grep > locale, "LC_ALL=C must be set before the first grep");
    }

    // AC-8: in $RUNNER_TEMP, with a throwaway nuget.config that knows only the local folder.
    [Fact]
    public void SmokeStep_InstallsFromAnIsolatedNugetConfig()
    {
        string[] lines = [.. SmokeScript().Split('\n').Select(line => line.Trim())];

        int cd = Array.IndexOf(lines, "cd \"$RUNNER_TEMP\"");
        int write = Array.IndexOf(lines, "cat > smoke-nuget.config <<XML");
        int end = Array.IndexOf(lines, "XML");
        int install = Array.FindIndex(lines, line => line.StartsWith("dotnet tool install", StringComparison.Ordinal));
        Assert.True(cd >= 0 && cd < write, "the config must be written in $RUNNER_TEMP");
        Assert.True(write < end && end < install, "the config must be written before the install");

        string[] config = lines[(write + 1)..end];
        string[] sources = [.. config.Where(line => line.StartsWith("<add", StringComparison.Ordinal))];
        Assert.Contains("<clear />", config);
        Assert.Equal(["<add key=\"local\" value=\"$REPO/nupkg\" />"], sources);
        Assert.True(Array.IndexOf(config, "<clear />") < Array.IndexOf(config, sources[0]));
        Assert.Equal(
            "dotnet tool install --global --configfile \"$RUNNER_TEMP/smoke-nuget.config\""
                + " --add-source \"$REPO/nupkg\" --version \"$VERSION\" Context",
            lines[install]);
        Assert.Single(lines, line => line.StartsWith("dotnet ", StringComparison.Ordinal));
        Assert.DoesNotContain("nuget.org", string.Join("\n", lines.Where(line => !line.StartsWith('#'))));
    }

    // AC-8: `context --version` must print exactly "Context $VERSION", then `context tags` must exit 0 against
    // a fresh data folder (run: is bash -e, so a failing last command fails the step).
    [Fact]
    public void SmokeStep_ChecksTheVersionLine_ThenRunsTagsOnAFreshDataFolder()
    {
        string smoke = SmokeScript();
        string[] lines = [.. smoke.Split('\n').Select(line => line.Trim())];

        int install = Array.FindIndex(lines, line => line.StartsWith("dotnet tool install", StringComparison.Ordinal));
        int version = Array.IndexOf(lines, "ACTUAL=\"$(context --version)\"");
        int compare = Array.IndexOf(lines, "if [ \"$ACTUAL\" != \"Context $VERSION\" ]; then");
        int exit = Array.IndexOf(lines, "exit 1", Math.Max(compare, 0));
        int fresh = Array.IndexOf(lines, "SMOKE_CONFIG=\"$(mktemp -d \"$RUNNER_TEMP/smoke-config.XXXXXX\")\"");
        int tags = Array.IndexOf(lines, "XDG_CONFIG_HOME=\"$SMOKE_CONFIG\" context tags");
        Assert.True(install >= 0 && install < version, "the tool must be installed before it is run");
        Assert.True(version < compare && compare < exit, "a wrong version line must fail the step");
        Assert.True(exit < fresh && fresh < tags, "context tags must run after the version check, on a fresh folder");
        Assert.Equal("XDG_CONFIG_HOME=\"$SMOKE_CONFIG\" context tags", lines.Last(line => line.Length > 0));
        // Nothing may swallow a failure of the two tool runs.
        Assert.DoesNotContain("|| true", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("set +e", smoke, StringComparison.Ordinal);
        Assert.False(Step(ReadWorkflow(), "Smoke check the packed tool").ContainsKey("continue-on-error"));

        string all = string.Join("\n", RunScripts(ReadWorkflow()));
        int pack = all.IndexOf("dotnet pack", StringComparison.Ordinal);
        int check = all.IndexOf("context --version", StringComparison.Ordinal);
        int release = all.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(pack < check, "the smoke check must run after dotnet pack");
        Assert.True(check < release, "gh release create must run after the smoke check");
    }

    // AC-9: one .nupkg, the title, generated notes, on $TARGET; --prerelease only for a version with '-'.
    // 135 AC-1: as a draft, and it is the last step of the release job (the prune step moved to publish).
    [Fact]
    public void ReleaseStep_CreatesTheReleaseWithTheNupkg_AndMarksPrereleases()
    {
        List<YamlMapping> steps = Steps(ReadWorkflow());
        Assert.Equal("Create GitHub release", Scalar(steps[^1]["name"]));
        Assert.Contains(" --target \"$TARGET\" --draft $PRERELEASE", ReleaseCommand, StringComparison.Ordinal);
        Assert.DoesNotContain(steps, step => Scalar(step["name"]).Contains("Prune", StringComparison.Ordinal));
        Assert.All(RunScripts(ReadWorkflow()), s => Assert.DoesNotContain("old-prereleases", s, StringComparison.Ordinal));

        string release = Assert.Single(
            RunScripts(ReadWorkflow()),
            script => script.Contains("gh release create", StringComparison.Ordinal));
        string[] lines = [.. release.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

        Assert.Equal(
            ["PRERELEASE=\"\"", "case \"$VERSION\" in", "*-*) PRERELEASE=\"--prerelease\" ;;", "esac", NotesCommand, ReleaseCommand],
            lines.Where(line => !line.StartsWith('#')));
        Assert.DoesNotContain("$GITHUB_SHA", release, StringComparison.Ordinal);
    }

    // AC-9: the prune step skips prereleases, fails on a failed listing and deletes only filtered tags.
    // 135 AC-7: it is the last step of the publish job, right after the publish step, with the same script.
    [Fact]
    public void PruneStep_SkipsPrereleases_AndDeletesOnlyFilteredTags()
    {
        string script = Assert.Single(
            AllRunScripts(ReadWorkflow()),
            s => s.Contains("old-prereleases.sh", StringComparison.Ordinal));
        List<YamlMapping> publish = JobSteps(WorkflowYaml.Parse(ReadWorkflow()), "publish");
        Assert.Equal("Publish GitHub release", Scalar(publish[^2]["name"]));
        Assert.Equal("Prune old prereleases", Scalar(publish[^1]["name"]));
        Assert.Equal(script, Scalar(publish[^1]["run"]));
        Assert.Equal("true", Scalar(publish[^1]["continue-on-error"]));
        Assert.Equal(
            [
                "case \"$VERSION\" in",
                "*-*)",
                "echo \"Version '$VERSION' is a prerelease; nothing to prune\"",
                "exit 0",
                ";;",
                "esac",
                "set -o pipefail",
                "gh release list --limit 1000 --json tagName,isPrerelease --jq '.[] | select(.isPrerelease) | .tagName' |",
                "sh .github/scripts/old-prereleases.sh \"$VERSION\" |",
                "while IFS= read -r tag; do",
                "echo \"Deleting prerelease $tag\"",
                "gh release delete \"$tag\" --cleanup-tag --yes || echo \"::warning::Could not delete prerelease $tag\"",
                "done",
            ],
            Lines(script).Where(line => !line.StartsWith('#')));

        int skip = script.IndexOf("case \"$VERSION\" in", StringComparison.Ordinal);
        int hyphen = script.IndexOf("*-*)", Math.Max(skip, 0), StringComparison.Ordinal);
        int exit = script.IndexOf("exit 0", Math.Max(hyphen, 0), StringComparison.Ordinal);
        int esac = LineIndex(script, "esac", Math.Max(exit, 0));
        int firstGh = script.IndexOf("gh ", StringComparison.Ordinal);
        int pipefail = script.IndexOf("set -o pipefail", StringComparison.Ordinal);
        int list = script.IndexOf(
            "gh release list --limit 1000 --json tagName,isPrerelease --jq '.[] | select(.isPrerelease) | .tagName' |",
            StringComparison.Ordinal);
        int filter = script.IndexOf("sh .github/scripts/old-prereleases.sh \"$VERSION\" |", StringComparison.Ordinal);
        int loop = script.IndexOf("while IFS= read -r tag; do", StringComparison.Ordinal);
        int delete = script.IndexOf("gh release delete \"$tag\" --cleanup-tag --yes || echo", StringComparison.Ordinal);
        int done = LineIndex(script, "done", Math.Max(delete, 0));

        Assert.True(skip >= 0 && skip < hyphen && hyphen < exit && exit < esac, "exit 0 for a $VERSION with '-'");
        Assert.True(esac < firstGh, "the prerelease skip must come before any gh call");
        Assert.True(pipefail >= 0 && pipefail < list, "set -o pipefail before the gh release list pipe");
        Assert.True(firstGh == list, "the first gh call must be the prerelease listing");
        Assert.True(list < filter && filter < loop && loop < delete && delete < done, "list | filter | delete loop");
        Assert.Single(Regex.Matches(script, @"gh release delete"));
        Assert.Equal(2, Regex.Matches(script, @"\bgh ").Count);
    }

    // AC-11: the csproj packs the console app as the tool `context`, into the repo-root nupkg folder.
    [Fact]
    public void ProjectFile_PacksAsToolNamedContext()
    {
        XElement project = XDocument.Load(Path.Combine(RepoRoot, "src", "Context", "Context.csproj")).Root!;

        Assert.Equal("true", SingleProperty(project, "PackAsTool"));
        Assert.Equal("context", SingleProperty(project, "ToolCommandName"));
        Assert.Equal("Context", SingleProperty(project, "PackageId"));
        Assert.Equal("$(MSBuildThisFileDirectory)../../nupkg", SingleProperty(project, "PackageOutputPath"));
        Assert.Equal("0.1.0", SingleProperty(project, "Version"));
        Assert.Equal(AppVersion.Current, SingleProperty(project, "Version"));
        Assert.NotEqual(string.Empty, SingleProperty(project, "Description").Trim());
        Assert.Equal("LICENSE.md", SingleProperty(project, "PackageLicenseFile"));
        Assert.Empty(project.Descendants("PackageVersion"));

        XElement license = Assert.Single(project.Descendants("None"));
        Assert.Equal("../../LICENSE.md", license.Attribute("Include")?.Value);
        Assert.Equal("true", license.Attribute("Pack")?.Value);
        Assert.Equal(string.Empty, license.Attribute("PackagePath")?.Value);
        Assert.True(File.Exists(Path.Combine(RepoRoot, "LICENSE.md")));

        XElement target = Assert.Single(project.Descendants("Target"));
        Assert.Equal("ValidateVersion", target.Attribute("Name")?.Value);
        Assert.Equal("BeforeBuild", target.Attribute("BeforeTargets")?.Value);
    }

    // AC-11: ValidateVersion (which runs before every build and pack) accepts only SemVer and names the version.
    // "1.2.3%3Bx" is how a ';' reaches the property; unescaped, msbuild refuses the switch itself (next test).
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("0.1.0", true)]
    [InlineData("1.2.3-develop.4", true)]
    [InlineData("1.2.3-beta.1", true)]
    [InlineData("v1.2.3", false)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("01.2.3", false)]
    [InlineData("1.2.3+build", false)]
    [InlineData("1.2.3%3Bx", false, "1.2.3;x")]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public async Task ProjectFile_ValidateVersion_AcceptsOnlySemVer(string version, bool valid, string? shown = null)
    {
        (int code, string output) = await ValidateVersion(version);

        if (valid)
        {
            Assert.True(code == 0, "ValidateVersion exit " + code + ": " + output);
        }
        else
        {
            Assert.True(code != 0, "ValidateVersion accepted '" + version + "'");
            Assert.Contains(
                "Version '" + (shown ?? version) + "' is not a valid SemVer version",
                output,
                StringComparison.Ordinal);
        }
    }

    // AC-11: passed unescaped, as the workflow would (-p:Version="$VERSION"), a ';' splits the switch and msbuild
    // itself stops with MSB1006 before any target runs, so nothing is built or packed either way.
    [Fact]
    public async Task ProjectFile_VersionWithUnescapedSemicolon_IsRefusedByMsbuild()
    {
        (int code, string output) = await ValidateVersion("1.2.3;x");

        Assert.NotEqual(0, code);
        Assert.Contains("MSB1006", output, StringComparison.Ordinal);
    }

    // AC-11
    [Theory]
    [InlineData("nupkg/x.nupkg")]
    [InlineData("nupkg/")]
    public void NupkgFolder_IsGitIgnored(string path)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("check-ignore");
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add(path);

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, "git check-ignore exit " + process.ExitCode + ": " + error);
    }

    // AC-13: CLAUDE.md describes the release flow, and no longer calls ci.yml the only workflow.
    [Theory]
    [InlineData("- **Release:**")]
    [InlineData(".github/workflows/release.yml")]
    [InlineData("push to `develop`")]
    [InlineData("`release vX.Y.Z`")]
    [InlineData("`vX.Y.Z` tag")]
    [InlineData("`X.Y.(Z+1)-develop.<run number>`")]
    [InlineData(".github/scripts/develop-version.sh")]
    [InlineData(".github/scripts/release-pr-version.sh")]
    [InlineData(".github/scripts/old-prereleases.sh")]
    [InlineData("merge commit")]
    [InlineData("`backlog.md`")]
    [InlineData("`paths-ignore`")]
    [InlineData("`Prune old prereleases`")]
    [InlineData("`Context.<version>.nupkg`")]
    [InlineData("the macOS app can be signed and notarized, the Windows build is unsigned, and the updater checks no signature")]
    [InlineData("repository variable `MACOS_SIGNING`")]
    [InlineData("`env.MACOS_SIGNING`")]
    [InlineData("`MACOS_CERTIFICATE_P12`")]
    [InlineData("`MACOS_CERTIFICATE_PASSWORD`")]
    [InlineData("`NOTARY_KEY_P8`")]
    [InlineData("`NOTARY_KEY_ID`")]
    [InlineData("`NOTARY_ISSUER_ID`")]
    [InlineData(".github/scripts/macos-sign.sh <Context.app> <identity> <entitlements>")]
    [InlineData(".github/scripts/macos-notarize.sh <Context.app> <zip> <key.p8> <key-id> <issuer-id>")]
    [InlineData(".github/scripts/macos-entitlements.plist")]
    [InlineData("`com.apple.security.cs.allow-jit`")]
    [InlineData("`Bundle, sign, notarize and zip`")]
    [InlineData("fails closed")]
    [InlineData("`desktop-macos` (`macos-latest`)")]
    [InlineData("`desktop-windows` (`windows-latest`, `shell: bash`)")]
    [InlineData("`needs: release`")]
    [InlineData("`Context-<version>-osx-arm64.zip` and `Context-<version>-osx-x64.zip`")]
    [InlineData(".github/scripts/macos-app.sh <publish-dir> <version> <out-dir>")]
    [InlineData("`codesign -s -`")]
    [InlineData("`Setup.exe`, the portable zip, this version's full `.nupkg`, a delta `.nupkg`")]
    [InlineData("`releases.<channel>.json`")]
    [InlineData("never a package of an older version")]
    [InlineData("`--channel develop`")]
    [InlineData("default channel (`win`)")]
    [InlineData("updates itself at start-up")]
    [InlineData("**Trust model:**")]
    [InlineData("no signature check in the updater")]
    [InlineData("the Windows build is unsigned")]
    [InlineData("HTTPS download from this repository's GitHub releases, which only the release workflow writes")]
    [InlineData("No token is embedded")]
    [InlineData("`github.token` is the only credential of every other step")]
    [InlineData(".github/scripts/release-notes.sh <macos>")]
    [InlineData("--clobber`, so re-running a failed desktop job works")]
    [InlineData("commit SHAs")]
    [InlineData("tests/Context.Tests/WorkflowYaml.cs")]
    [InlineData("context --version")]
    [InlineData("Four jobs: `release` (`ubuntu-latest`)")]
    [InlineData("`publish` (`ubuntu-latest`, `needs` all three)")]
    [InlineData("created with `--draft`")]
    [InlineData("The release is a draft until `publish` runs")]
    [InlineData("A failed desktop job leaves an unpublished draft")]
    [InlineData("re-run the failed job")]
    [InlineData("`gh release edit \"$TAG\" --draft=false`")]
    [InlineData("runs in `publish`")]
    [InlineData("This is what makes immutable releases work")]
    public void ClaudeMd_DescribesTheReleaseFlow(string expected)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, "CLAUDE.md")).ReplaceLineEndings("\n");
        int start = text.IndexOf("\n- **Release:**", StringComparison.Ordinal);
        Assert.True(start >= 0, "CLAUDE.md has no '- **Release:**' entry");
        int end = text.IndexOf("\n- **", start + 1, StringComparison.Ordinal);
        string entry = Regex.Replace(text[start..(end < 0 ? text.Length : end)], @"\s+", " ");

        Assert.Contains(expected, entry, StringComparison.Ordinal);
        Assert.DoesNotContain("the only workflow", text, StringComparison.Ordinal);
        Assert.DoesNotContain("there is no desktop build", text, StringComparison.Ordinal);
        Assert.DoesNotContain("console app only", text, StringComparison.Ordinal);
        Assert.DoesNotContain("leaves a release with fewer assets", Regex.Replace(text, @"\s+", " "), StringComparison.Ordinal);
        Assert.DoesNotContain("Three jobs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("release-notes-header", text, StringComparison.Ordinal);
        Assert.DoesNotContain("builds are only ad-hoc signed", Regex.Replace(text, @"\s+", " "), StringComparison.Ordinal);
    }

    // 133 AC-2: both architectures are published self-contained with the version, then bundled, ad-hoc signed,
    // verified and zipped, and both zips are uploaded to the release's tag.
    // 146 AC-1, AC-3: this is the path while signing is not on, and its three scripts are what they were.
    [Fact]
    public void MacJob_PublishesBundlesSignsAdHocZipsAndUploadsBothArchitectures()
    {
        YamlMapping adHoc = Assert.Single(
            JobSteps(WorkflowYaml.Parse(ReadWorkflow()), "desktop-macos"), step => Scalar(step["name"]) == AdHocStep);
        Assert.Equal("env.MACOS_SIGNING != 'on'", Scalar(adHoc["if"]));
        Assert.Equal(
            "for RID in osx-arm64 osx-x64; do\n"
                + "  sh .github/scripts/macos-app.sh \"publish/$RID\" \"$VERSION\" \"app/$RID\"\n"
                + "  codesign --force --deep -s - \"app/$RID/Context.app\"\n"
                + "  codesign --verify --deep --strict \"app/$RID/Context.app\"\n"
                + "  # ditto keeps the modes and the signature's extended attributes; the zip holds Context.app itself.\n"
                + "  ditto -c -k --keepParent \"app/$RID/Context.app\" \"Context-$VERSION-$RID.zip\"\n"
                + "done",
            Scalar(adHoc["run"]).TrimEnd('\n'));

        string[] publish = Lines(JobScript("desktop-macos", "Publish"));
        Assert.Equal(
            ["for RID in osx-arm64 osx-x64; do", PublishCommand + " -r \"$RID\" -o \"publish/$RID\"", "done"],
            publish);

        string[] bundle = Lines(JobScript("desktop-macos", "Bundle, ad-hoc sign and zip"));
        Assert.Equal(
            [
                "for RID in osx-arm64 osx-x64; do",
                "sh .github/scripts/macos-app.sh \"publish/$RID\" \"$VERSION\" \"app/$RID\"",
                "codesign --force --deep -s - \"app/$RID/Context.app\"",
                "codesign --verify --deep --strict \"app/$RID/Context.app\"",
                "ditto -c -k --keepParent \"app/$RID/Context.app\" \"Context-$VERSION-$RID.zip\"",
                "done",
            ],
            bundle);

        Assert.Equal(
            ["gh release upload \"$TAG\" \"Context-$VERSION-osx-arm64.zip\" \"Context-$VERSION-osx-x64.zip\" --clobber"],
            Lines(JobScript("desktop-macos", "Upload")));
    }

    // 133 AC-3: Info.plist with the bundle keys, and every published file under Contents/MacOS.
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.4-develop.7", "1.2.4")]
    [InlineData("0.0.1-develop.12", "0.0.1")]
    [InlineData("10.20.30-rc-1.x", "10.20.30")]
    public async Task MacAppScript_BuildsTheBundle(string version, string core)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WritePublishFolder(folder);

            ScriptResult result = await RunBash(folder, [MacAppScript, "publish", version, "out"]);

            Assert.True(result.Code == 0, "macos-app.sh exit " + result.Code + ": " + result.Err);
            Assert.Equal(["out", "publish"], result.FilesCreated.Order(StringComparer.Ordinal));
            string contents = Path.Combine(folder, "out", "Context.app", "Contents");
            Assert.Equal(["Info.plist", "MacOS"], Entries(contents));

            Dictionary<string, string> plist = ReadPlist(Path.Combine(contents, "Info.plist"));
            Assert.Equal("Context.Desktop", plist["CFBundleExecutable"]);
            Assert.Equal("com.nothisisnotdaniel.context", plist["CFBundleIdentifier"]);
            Assert.Equal(core, plist["CFBundleShortVersionString"]);
            Assert.Equal(core, plist["CFBundleVersion"]);
            Assert.Equal("Context", plist["CFBundleName"]);
            Assert.Equal("APPL", plist["CFBundlePackageType"]);

            Assert.Equal(RelativeFiles(Path.Combine(folder, "publish")), RelativeFiles(Path.Combine(contents, "MacOS")));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(folder, "publish", "wwwroot", "index.html")),
                File.ReadAllBytes(Path.Combine(contents, "MacOS", "wwwroot", "index.html")));
            Assert.True(
                (File.GetUnixFileMode(Path.Combine(contents, "MacOS", "Context.Desktop")) & UnixFileMode.UserExecute) != 0,
                "the executable must stay executable");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-3: a version that is not SemVer is refused before anything is created, and is never evaluated.
    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3+abc")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3 ")]
    [InlineData("1.2.3\n")]
    [InlineData("1.2.3\n1.2.4")]
    [InlineData("1.2.3\r")]
    [InlineData("1.2.3;touch MARKER")]
    [InlineData("1.2.3; touch MARKER")]
    [InlineData("$(touch MARKER)")]
    [InlineData("`touch MARKER`")]
    [InlineData("1.2.3-$(touch MARKER)")]
    [InlineData("1.2.3-béta")]
    [InlineData("1.2.3-béta", "en_US.UTF-8")]
    [InlineData("</string><string>x")]
    public async Task MacAppScript_RefusesAVersionThatIsNotSemVer(string version, string locale = "")
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WritePublishFolder(folder);
            Dictionary<string, string> environment = [];
            if (locale.Length > 0)
            {
                environment["LC_ALL"] = locale;
                environment["LANG"] = locale;
            }

            ScriptResult result = await RunBash(folder, [MacAppScript, "publish", version, "out"], environment);

            Assert.NotEqual(0, result.Code);
            Assert.NotEqual(string.Empty, result.Err);
            Assert.Equal(string.Empty, result.Out);
            // No out folder, and nothing the version could have run left a trace.
            Assert.Equal(["publish"], result.FilesCreated);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-3: a missing or extra argument, an empty one, a publish folder without the executable or an output
    // folder that already holds Context.app exits non-zero and creates nothing.
    [Theory]
    [InlineData("no arguments")]
    [InlineData("one argument")]
    [InlineData("two arguments")]
    [InlineData("four arguments")]
    [InlineData("empty publish folder")]
    [InlineData("empty out folder")]
    [InlineData("no executable")]
    [InlineData("no publish folder")]
    [InlineData("app already there")]
    public async Task MacAppScript_RefusesWrongArguments_AndCreatesNothing(string situation)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WritePublishFolder(folder);
            string[] arguments = situation switch
            {
                "no arguments" => [],
                "one argument" => ["publish"],
                "two arguments" => ["publish", "1.2.3"],
                "four arguments" => ["publish", "1.2.3", "out", "more"],
                "empty publish folder" => ["", "1.2.3", "out"],
                "empty out folder" => ["publish", "1.2.3", ""],
                "no publish folder" => ["missing", "1.2.3", "out"],
                _ => ["publish", "1.2.3", "out"],
            };
            if (situation == "no executable")
            {
                File.Delete(Path.Combine(folder, "publish", "Context.Desktop"));
            }

            if (situation == "app already there")
            {
                Directory.CreateDirectory(Path.Combine(folder, "out", "Context.app"));
            }

            SortedDictionary<string, DateTime> before = Listing(folder);
            ScriptResult result = await RunBash(folder, [MacAppScript, .. arguments]);

            Assert.NotEqual(0, result.Code);
            Assert.NotEqual(string.Empty, result.Err);
            Assert.Equal(before, Listing(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-3: the script only builds the bundle (no signing, no zip), and never evaluates its arguments.
    [Fact]
    public void MacAppScript_NeitherSignsNorZips_AndNeverEvaluatesTheVersion()
    {
        string source = ScriptSource("macos-app.sh");
        string code = CodeLines(source);

        Assert.StartsWith("#!/bin/sh\n", source, StringComparison.Ordinal);
        Assert.Contains("set -eu\n", code, StringComparison.Ordinal);
        Assert.Contains("LC_ALL=C\nexport LC_ALL\n", code, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", code, StringComparison.Ordinal);
        Assert.DoesNotContain("codesign", code, StringComparison.Ordinal);
        Assert.DoesNotContain("zip", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ditto", code, StringComparison.Ordinal);
        Assert.DoesNotContain("curl", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[^""{]\$version", code);
        // The SemVer check comes before the first thing the script creates.
        Assert.True(
            code.IndexOf("grep -Eq \"$semver\"", StringComparison.Ordinal) < code.IndexOf("mkdir", StringComparison.Ordinal),
            "the version must be checked before anything is created");
        Assert.Single(Regex.Matches(code, @"\bmkdir\b"));
    }

    // 133 AC-4: vpk is installed at an exact version, equal to the Velopack package of Context.Desktop, and no other
    // project references Velopack.
    [Fact]
    public void WindowsJob_InstallsVpkAtTheVelopackPackageVersion()
    {
        string install = Assert.Single(Lines(JobScript("desktop-windows", "Install vpk")));
        Match match = Regex.Match(install, @"\Adotnet tool install --global vpk --version (\d+\.\d+\.\d+)\z");
        Assert.True(match.Success, "vpk must be installed at an exact version: " + install);

        List<string> references = [];
        foreach (string folder in new[] { "src", "tests" })
        {
            foreach (string project in Directory.EnumerateFiles(
                Path.Combine(RepoRoot, folder), "*.csproj", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RepoRoot, project).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (XElement reference in XDocument.Load(project).Descendants("PackageReference"))
                {
                    string id = (reference.Attribute("Include") ?? reference.Attribute("Update"))?.Value ?? string.Empty;
                    if (id.Contains("Velopack", StringComparison.OrdinalIgnoreCase)
                        || id.Equals("vpk", StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add(relative + ": " + id + " " + reference.Attribute("Version")?.Value);
                    }
                }
            }
        }

        Assert.Equal(["src/Context.Desktop/Context.Desktop.csproj: Velopack " + match.Groups[1].Value], references);
        Assert.Equal(
            [PublishCommand + " -r win-x64 -o publish/win-x64"],
            Lines(JobScript("desktop-windows", "Publish")));
    }

    // 133 AC-4, AC-5: the Pack step, run with bash against a stub vpk: an earlier release of the same channel is
    // downloaded first (with the token), then the pack runs without the token; --channel develop exactly for a
    // version with a prerelease label.
    [Theory]
    [InlineData("1.2.3", "")]
    [InlineData("1.2.4-develop.7", " --channel develop")]
    [InlineData("0.0.1-develop.1", " --channel develop")]
    [InlineData("2.0.0", "")]
    public async Task WindowsPackStep_UsesTheDevelopChannelOnlyForPrereleases(string version, string channel)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            ScriptResult result = await RunStep(folder, JobScript("desktop-windows", "Pack"), version, downloadExit: 0);

            Assert.True(result.Code == 0, "Pack exit " + result.Code + ": " + result.Err);
            string pre = channel.Length > 0 ? " --pre" : string.Empty;
            Assert.Equal(
                [
                    "vpk [token] download github --repoUrl https://github.com/owner/repo --token stub-token"
                        + " --outputDir Releases" + channel + pre,
                    "vpk [no token] pack --packId ContextDesktop --packVersion " + version
                        + " --packDir publish/win-x64 --mainExe Context.Desktop.exe --packTitle Context"
                        + " --runtime win-x64 --outputDir Releases" + channel,
                ],
                StubLog(folder));
            Assert.DoesNotContain("::warning::", result.Out, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-5: no earlier release (the download fails) only warns: the pack still runs and the step passes. A
    // failed pack fails the step.
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.4-develop.7")]
    public async Task WindowsPackStep_AFailedDownloadOnlyWarns_AFailedPackFails(string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            string script = JobScript("desktop-windows", "Pack");
            ScriptResult result = await RunStep(folder, script, version, downloadExit: 1);

            Assert.True(result.Code == 0, "Pack exit " + result.Code + ": " + result.Err);
            Assert.Contains("::warning::", result.Out, StringComparison.Ordinal);
            List<string> log = StubLog(folder);
            Assert.Equal(2, log.Count);
            Assert.StartsWith("vpk [token] download github ", log[0], StringComparison.Ordinal);
            Assert.StartsWith("vpk [no token] pack ", log[1], StringComparison.Ordinal);

            File.Delete(Path.Combine(folder, "stub.log"));
            ScriptResult failed = await RunStep(folder, script, version, downloadExit: 0, packExit: 1);
            Assert.NotEqual(0, failed.Code);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-6: the Upload step, run with bash against a stub gh: Setup.exe, the portable zip, this version's full
    // package, the feed files and the delta when there is one; never the earlier package that sits next to them.
    [Theory]
    [InlineData("1.2.4-develop.7", "develop", "-develop", true)]
    [InlineData("1.2.4-develop.7", "develop", "-develop", false)]
    [InlineData("1.2.3", "win", "", true)]
    [InlineData("1.2.3", "win", "", false)]
    public async Task WindowsUploadStep_UploadsOnlyThisVersionsFiles(string version, string name, string suffix, bool delta)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            string full = "ContextDesktop-" + version + suffix + "-full.nupkg";
            string deltaName = "ContextDesktop-" + version + suffix + "-delta.nupkg";
            List<string> files =
            [
                "ContextDesktop-" + name + "-Setup.exe", "ContextDesktop-" + name + "-Portable.zip", full,
                "releases." + name + ".json", "assets." + name + ".json", "RELEASES" + suffix,
                // What `vpk download` left there: an earlier version, and the other channel's files.
                "ContextDesktop-1.2.2" + suffix + "-full.nupkg", "ContextDesktop-1.2.2" + suffix + "-delta.nupkg",
                "ContextDesktop-1.2.4-develop.6-develop-full.nupkg", "ContextDesktop-1.0.0-full.nupkg",
            ];
            if (delta)
            {
                files.Add(deltaName);
            }

            WriteReleases(folder, files);

            ScriptResult result = await RunStep(folder, JobScript("desktop-windows", "Upload"), version);

            Assert.True(result.Code == 0, "Upload exit " + result.Code + ": " + result.Err);
            string expected = "gh [token] release upload v" + version
                + " Releases/ContextDesktop-" + name + "-Setup.exe Releases/ContextDesktop-" + name + "-Portable.zip"
                + " Releases/" + full + " Releases/releases." + name + ".json Releases/assets." + name + ".json"
                + " Releases/RELEASES" + suffix + (delta ? " Releases/" + deltaName : string.Empty) + " --clobber";
            Assert.Equal([expected], StubLog(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 133 AC-6: without Setup.exe or this version's full package the step fails before gh is called.
    [Theory]
    [InlineData("1.2.4-develop.7", "ContextDesktop-develop-Setup.exe")]
    [InlineData("1.2.4-develop.7", "ContextDesktop-1.2.4-develop.7-develop-full.nupkg")]
    [InlineData("1.2.3", "ContextDesktop-win-Setup.exe")]
    [InlineData("1.2.3", "ContextDesktop-1.2.3-full.nupkg")]
    public async Task WindowsUploadStep_FailsWhenSetupOrTheFullPackageIsMissing(string version, string missing)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            bool develop = version.Contains('-', StringComparison.Ordinal);
            string name = develop ? "develop" : "win";
            string suffix = develop ? "-develop" : string.Empty;
            List<string> files =
            [
                "ContextDesktop-" + name + "-Setup.exe", "ContextDesktop-" + name + "-Portable.zip",
                "ContextDesktop-" + version + suffix + "-full.nupkg", "releases." + name + ".json",
                "assets." + name + ".json", "RELEASES" + suffix,
                // The other channel's installer and an earlier package must not stand in for the missing file.
                "ContextDesktop-" + (develop ? "win" : "develop") + "-Setup.exe", "ContextDesktop-1.2.2" + suffix + "-full.nupkg",
            ];
            Assert.True(files.Remove(missing));
            WriteReleases(folder, files);

            ScriptResult result = await RunStep(folder, JobScript("desktop-windows", "Upload"), version);

            Assert.NotEqual(0, result.Code);
            Assert.Contains(missing + " is missing", result.Err, StringComparison.Ordinal);
            Assert.Empty(StubLog(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 135 AC-5, AC-6: the publish step, run with bash against a stub gh. With a draft on the release's commit that
    // holds every needed asset it publishes with one `gh release edit`, and adds --prerelease only for a prerelease.
    [Theory]
    [InlineData("1.2.4-develop.7", " --prerelease")]
    [InlineData("0.0.1-develop.9", " --prerelease")]
    [InlineData("1.2.3", "")]
    public async Task PublishStep_PublishesACompleteDraft(string version, string prerelease)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            // More assets than the needed ones (portable zip, delta, the other feed files) are fine.
            List<string> assets = PublishAssets(version);
            assets.AddRange(["ContextDesktop-win-Portable.zip", "RELEASES", "assets.win.json", "extra.txt"]);
            ScriptResult result = await RunStep(
                folder, JobScript("publish", "Publish GitHub release"), version, view: ReleaseView("true", ReleaseTarget, assets));

            Assert.True(result.Code == 0, "Publish exit " + result.Code + ": " + result.Err);
            Assert.Equal(
                [
                    "gh [token] release view v" + version
                        + " --json isDraft,targetCommitish,assets --jq .isDraft, .targetCommitish, .assets[].name",
                    "gh [token] release edit v" + version + " --draft=false" + prerelease,
                ],
                StubLog(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 135 AC-5: anything but this run's complete draft fails the step before `gh release edit` is called: a release
    // that is already public, one on another commit, one that cannot be read, and each needed asset missing (also
    // when only the other channel's or another version's file is there).
    [Theory]
    [InlineData("1.2.4-develop.7", "not a draft")]
    [InlineData("1.2.3", "not a draft")]
    [InlineData("1.2.3", "wrong target")]
    [InlineData("1.2.3", "target is a longer text")]
    [InlineData("1.2.3", "no target")]
    [InlineData("1.2.3", "view fails")]
    [InlineData("1.2.3", "empty view")]
    [InlineData("1.2.3", "no assets")]
    [InlineData("1.2.3", "asset 0")]
    [InlineData("1.2.3", "asset 1")]
    [InlineData("1.2.3", "asset 2")]
    [InlineData("1.2.3", "asset 3")]
    [InlineData("1.2.3", "asset 4")]
    [InlineData("1.2.3", "asset 5")]
    [InlineData("1.2.4-develop.7", "asset 0")]
    [InlineData("1.2.4-develop.7", "asset 1")]
    [InlineData("1.2.4-develop.7", "asset 2")]
    [InlineData("1.2.4-develop.7", "asset 3")]
    [InlineData("1.2.4-develop.7", "asset 4")]
    [InlineData("1.2.4-develop.7", "asset 5")]
    [InlineData("1.2.3", "other channel")]
    [InlineData("1.2.4-develop.7", "other channel")]
    [InlineData("1.2.3", "other version")]
    [InlineData("1.2.3", "longer names only")]
    public async Task PublishStep_RefusesAnythingButACompleteDraft(string version, string situation)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            List<string> assets = PublishAssets(version);
            string draft = "true";
            string target = ReleaseTarget;
            int viewExit = 0;
            string? view = null;
            switch (situation)
            {
                case "not a draft":
                    draft = "false";
                    break;
                case "wrong target":
                    target = "76543210fedcba9876543210fedcba9876543210";
                    break;
                case "target is a longer text":
                    target = ReleaseTarget + "0";
                    break;
                case "no target":
                    view = "true\n" + string.Join('\n', assets) + "\n";
                    break;
                case "view fails":
                    viewExit = 1;
                    break;
                case "empty view":
                    view = string.Empty;
                    break;
                case "no assets":
                    assets.Clear();
                    break;
                case "other channel":
                    assets = PublishAssets(version.Contains('-', StringComparison.Ordinal) ? "1.2.4" : "1.2.3-develop.1");
                    assets[0] = "Context." + version + ".nupkg";
                    assets[1] = "Context-" + version + "-osx-arm64.zip";
                    assets[2] = "Context-" + version + "-osx-x64.zip";
                    break;
                case "other version":
                    assets = PublishAssets("1.2.2");
                    break;
                case "longer names only":
                    assets = [.. assets.Select(name => name + ".sha256").Concat(assets.Select(name => "old-" + name))];
                    break;
                default:
                    assets.RemoveAt(int.Parse(situation["asset ".Length..], System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }

            ScriptResult result = await RunStep(
                folder,
                JobScript("publish", "Publish GitHub release"),
                version,
                view: view ?? ReleaseView(draft, target, assets),
                viewExit: viewExit);

            Assert.NotEqual(0, result.Code);
            List<string> log = StubLog(folder);
            Assert.StartsWith("gh [token] release view v" + version + " ", Assert.Single(log), StringComparison.Ordinal);
            Assert.DoesNotContain(log, line => line.Contains(" edit ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 135 AC-6: a failing `gh release edit` fails the step (it is the script's last command, with no `||`).
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.4-develop.7")]
    public async Task PublishStep_FailsWhenTheEditFails(string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            ScriptResult result = await RunStep(
                folder,
                JobScript("publish", "Publish GitHub release"),
                version,
                view: ReleaseView("true", ReleaseTarget, PublishAssets(version)),
                editExit: 1);

            Assert.NotEqual(0, result.Code);
            Assert.Equal(2, StubLog(folder).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 135 AC-2: both desktop jobs still upload into the release by its tag, replacing their own assets on a re-run.
    [Fact]
    public void DesktopJobs_UploadIntoTheDraftByTag()
    {
        string workflow = ReadWorkflow();

        Assert.Contains(
            "gh release upload \"$TAG\" \"Context-$VERSION-osx-arm64.zip\" \"Context-$VERSION-osx-x64.zip\" --clobber",
            JobScript("desktop-macos", "Upload"),
            StringComparison.Ordinal);
        Assert.Contains(
            "gh release upload \"$TAG\" \"$@\" --clobber", JobScript("desktop-windows", "Upload"), StringComparison.Ordinal);
        Assert.Equal(2, AllRunScripts(workflow).Count(script => script.Contains("gh release upload", StringComparison.Ordinal)));
        Assert.All(
            JobRunScripts(workflow, "release").Concat(JobRunScripts(workflow, "publish")),
            script => Assert.DoesNotContain("gh release upload", script, StringComparison.Ordinal));
    }

    // 146 AC-9: the release notes header comes from release-notes.sh. Off (or empty) is the text the header file
    // had: how to open an unsigned build on macOS and Windows.
    [Theory]
    [InlineData("off")]
    [InlineData("")]
    public async Task ReleaseNotes_Off_SayHowToOpenAnUnsignedBuild(string macos)
    {
        ScriptResult result = await RunScript("release-notes.sh", macos, string.Empty, string.Empty);

        Assert.True(result.Code == 0, "exit " + result.Code + ": " + result.Err);
        Assert.Equal(string.Empty, result.Err);
        Assert.Empty(result.FilesCreated);
        Assert.StartsWith("## Desktop app (unsigned test builds)\n", result.Out, StringComparison.Ordinal);
        string text = Regex.Replace(result.Out, @"\s+", " ");
        Assert.Contains("`Open Anyway`", text, StringComparison.Ordinal);
        Assert.Contains("`xattr -dr com.apple.quarantine Context.app`", text, StringComparison.Ordinal);
        Assert.Contains("The desktop builds are not signed yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("notarized", text, StringComparison.Ordinal);
        AssertSharedReleaseNotes(result.Out);
    }

    // 146 AC-9: with signing on the macOS paragraph says signed and notarized and names no workaround; the Windows,
    // update and console-tool text is the same as with signing off.
    [Fact]
    public async Task ReleaseNotes_On_SayTheMacBuildIsSignedAndNotarized_AndKeepTheRest()
    {
        ScriptResult on = await RunScript("release-notes.sh", "on", string.Empty, string.Empty);
        ScriptResult off = await RunScript("release-notes.sh", "off", string.Empty, string.Empty);

        Assert.True(on.Code == 0, "exit " + on.Code + ": " + on.Err);
        Assert.Equal(string.Empty, on.Err);
        Assert.StartsWith("## Desktop app\n", on.Out, StringComparison.Ordinal);
        string text = Regex.Replace(on.Out, @"\s+", " ");
        Assert.Contains("- **macOS:**", text, StringComparison.Ordinal);
        Assert.Contains("signed and notarized by Apple", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Open Anyway", text, StringComparison.Ordinal);
        Assert.DoesNotContain("xattr", text, StringComparison.Ordinal);
        Assert.DoesNotContain("quarantine", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unsigned test builds", text, StringComparison.Ordinal);
        Assert.Contains("The Windows build is not signed yet", text, StringComparison.Ordinal);
        AssertSharedReleaseNotes(on.Out);

        // From the Windows paragraph on, both texts are the same bytes.
        string marker = "- **Windows:**";
        Assert.Equal(
            off.Out[off.Out.IndexOf(marker, StringComparison.Ordinal)..],
            on.Out[on.Out.IndexOf(marker, StringComparison.Ordinal)..]);
    }

    // 146 AC-9: any other value is refused with nothing on stdout, and is never evaluated.
    [Theory]
    [InlineData("ON")]
    [InlineData("On")]
    [InlineData("true")]
    [InlineData("on ")]
    [InlineData(" off")]
    [InlineData("on\n")]
    [InlineData("on; touch MARKER")]
    [InlineData("$(touch MARKER)")]
    [InlineData("`touch MARKER`")]
    public async Task ReleaseNotes_RefuseAnyOtherValue(string macos)
    {
        ScriptResult result = await RunScript("release-notes.sh", macos, string.Empty, string.Empty);

        Assert.NotEqual(0, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal("usage: release-notes.sh <macos: on, off or empty>\n", result.Err);
        Assert.Empty(result.FilesCreated);
    }

    // 146 AC-9: the release job writes the header to a file under $RUNNER_TEMP and passes it to gh; the script
    // prints plain Markdown from quoted here-documents and never evaluates its argument.
    [Fact]
    public async Task ReleaseNotes_ReachGhThroughANotesFile_AndTheScriptEvaluatesNothing()
    {
        string release = JobScript("release", "Create GitHub release");
        Assert.Contains(NotesCommand + "\n", release, StringComparison.Ordinal);
        Assert.Contains("--notes-file \"$RUNNER_TEMP/release-notes.md\" --generate-notes", release, StringComparison.Ordinal);
        Assert.True(
            release.IndexOf(NotesCommand, StringComparison.Ordinal) < release.IndexOf("gh release create", StringComparison.Ordinal),
            "the notes are written before the release is created");
        Assert.False(File.Exists(Path.Combine(RepoRoot, ".github", "release-notes-header.md")));

        string source = ScriptSource("release-notes.sh");
        string code = CodeLines(source);
        Assert.StartsWith("#!/bin/sh\n", source, StringComparison.Ordinal);
        Assert.Contains("set -eu\n", code, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", code, StringComparison.Ordinal);
        Assert.DoesNotContain("set -x", code, StringComparison.Ordinal);
        Assert.DoesNotContain("${{", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Regex.Matches(code, @"cat <<'NOTES'").Count);
        Assert.Equal(3, Regex.Matches(code, @"<<").Count);

        // No argument at all is refused as well.
        if (!OperatingSystem.IsWindows())
        {
            string folder = NewTempFolder();
            try
            {
                ScriptResult none = await RunBash(folder, [Path.Combine(RepoRoot, ".github", "scripts", "release-notes.sh")]);
                Assert.NotEqual(0, none.Code);
                Assert.Equal(string.Empty, none.Out);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // 146 AC-1: the macOS job's first script fails unless the switch is empty, off or on (exact case).
    [Theory]
    [InlineData("", true)]
    [InlineData("off", true)]
    [InlineData("on", true)]
    [InlineData("ON", false)]
    [InlineData("On", false)]
    [InlineData("true", false)]
    [InlineData("on ", false)]
    [InlineData("onn", false)]
    [InlineData("$(touch MARKER)", false)]
    public async Task MacCheckVersion_AcceptsOnlyEmptyOffOrOn(string value, bool accepted)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            ScriptResult result = await RunMac(
                folder,
                JobScript("desktop-macos", "Check version"),
                new Dictionary<string, string> { ["VERSION"] = "1.2.3", ["TAG"] = "v1.2.3", ["MACOS_SIGNING"] = value });

            Assert.Equal(accepted, result.Code == 0);
            Assert.Equal(accepted ? string.Empty : "MACOS_SIGNING must be empty, 'off' or 'on'\n", result.Err);
            Assert.Equal(string.Empty, result.Out);
            Assert.Empty(StubLog(folder));
            Assert.DoesNotContain("MARKER", result.FilesCreated);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-8: with signing on each architecture goes through macos-app.sh, macos-sign.sh and macos-notarize.sh, in
    // that order, to the same asset name, and the Upload step is the same script as with signing off.
    [Fact]
    public void MacJob_SignedPath_BundlesSignsNotarizesAndUploadsTheSameNames()
    {
        string[] lines = Lines(JobScript("desktop-macos", SigningStep));
        string[] expected =
        [
            "for RID in osx-arm64 osx-x64; do",
            "APP=\"app/$RID/Context.app\"",
            "sh .github/scripts/macos-app.sh \"publish/$RID\" \"$VERSION\" \"app/$RID\" || fail bundle \"$RID\"",
            "sh .github/scripts/macos-sign.sh \"$APP\" \"$IDENTITY\" .github/scripts/macos-entitlements.plist || CODE=\"$?\"",
            "sh .github/scripts/macos-notarize.sh \"$APP\" \"Context-$VERSION-$RID.zip\" \"$P8\" \"$NOTARY_KEY_ID\" \"$NOTARY_ISSUER_ID\" || CODE=\"$?\"",
            "done",
        ];
        int at = -1;
        foreach (string line in expected)
        {
            int next = Array.IndexOf(lines, line, at + 1);
            Assert.True(next > at, "'" + line + "' is missing or out of order");
            at = next;
        }

        Assert.Equal("done", lines[^1]);
        string script = JobScript("desktop-macos", SigningStep);
        Assert.DoesNotContain(lines, line => line.StartsWith("codesign ", StringComparison.Ordinal));
        // No ad-hoc identity ("-s -" followed by a space or the line end; "-s -k" is the keychain call's own option).
        Assert.DoesNotMatch(@"-s -(\s|$)", script);
        Assert.DoesNotContain("--deep", script, StringComparison.Ordinal);
        Assert.DoesNotContain("set -x", script, StringComparison.Ordinal);
        Assert.DoesNotContain("gh ", script, StringComparison.Ordinal);
        Assert.Contains("umask 077\n", script, StringComparison.Ordinal);
        Assert.Contains("trap cleanup EXIT\n", script, StringComparison.Ordinal);
        Assert.Equal(
            ["gh release upload \"$TAG\" \"Context-$VERSION-osx-arm64.zip\" \"Context-$VERSION-osx-x64.zip\" --clobber"],
            Lines(JobScript("desktop-macos", "Upload")));
        Assert.Equal("Upload", MacStepNames[^1]);
    }

    // 146 AC-5: the entitlements file holds exactly one key, allow-jit = true.
    [Fact]
    public void Entitlements_HoldOnlyAllowJit()
    {
        string path = Path.Combine(RepoRoot, ".github", "scripts", "macos-entitlements.plist");
        XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XElement root = XDocument.Load(reader).Root!;

        Assert.Equal("plist", root.Name.LocalName);
        XElement dict = Assert.Single(root.Elements());
        Assert.Equal("dict", dict.Name.LocalName);
        List<XElement> children = [.. dict.Elements()];
        Assert.Equal(["key", "true"], children.Select(child => child.Name.LocalName));
        Assert.Equal("com.apple.security.cs.allow-jit", children[0].Value);
    }

    // 146 AC-4: every file under Contents/MacOS except the main executable, then the main executable, then the
    // bundle, each with the hardened-runtime options and the identity; never --deep or "-" when signing; then verify.
    [Fact]
    public async Task MacSignScript_SignsInsideOut_ThenVerifies()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);

            ScriptResult result = await RunMac(folder, SignCall("out/Context.app", SigningIdentity, "ent.plist"));

            Assert.True(result.Code == 0, "exit " + result.Code + ": " + result.Err);
            string sign = "codesign --force --timestamp --options runtime --entitlements ent.plist -s " + SigningIdentity + " ";
            string macos = "out/Context.app/Contents/MacOS/";
            Assert.Equal(
                [
                    sign + macos + "Context.Desktop.dll " + macos + "wwwroot/editor/editor.js " + macos + "wwwroot/index.html",
                    sign + macos + "Context.Desktop",
                    sign + "out/Context.app",
                    "codesign --verify --deep --strict out/Context.app",
                ],
                MacLog(folder));
            Assert.Equal(string.Empty, result.Err);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-4: a wrong argument count, the ad-hoc identity, a missing bundle or entitlements file: no codesign call.
    [Theory]
    [InlineData("no arguments")]
    [InlineData("two arguments")]
    [InlineData("four arguments")]
    [InlineData("ad-hoc identity")]
    [InlineData("option as identity")]
    [InlineData("empty identity")]
    [InlineData("no bundle")]
    [InlineData("no main executable")]
    [InlineData("no entitlements")]
    public async Task MacSignScript_Refuses_WithoutCallingCodesign(string change)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);
            string app = "out/Context.app";
            if (change == "no bundle")
            {
                Directory.Delete(Path.Combine(folder, "out"), recursive: true);
            }
            else if (change == "no main executable")
            {
                File.Delete(Path.Combine(folder, "out", "Context.app", "Contents", "MacOS", "Context.Desktop"));
            }
            else if (change == "no entitlements")
            {
                File.Delete(Path.Combine(folder, "ent.plist"));
            }

            string[] arguments = change switch
            {
                "no arguments" => [],
                "two arguments" => [app, SigningIdentity],
                "four arguments" => [app, SigningIdentity, "ent.plist", "extra"],
                "ad-hoc identity" => [app, "-", "ent.plist"],
                "option as identity" => [app, "--deep", "ent.plist"],
                "empty identity" => [app, string.Empty, "ent.plist"],
                _ => [app, SigningIdentity, "ent.plist"],
            };
            SortedDictionary<string, DateTime> before = Listing(folder);

            ScriptResult result = await RunMac(folder, [MacScript("macos-sign.sh"), .. arguments]);

            Assert.Equal(1, result.Code);
            Assert.Equal(string.Empty, result.Out);
            Assert.Single(result.Err.TrimEnd('\n').Split('\n'));
            Assert.Empty(MacLog(folder));
            Assert.Equal(before, WithoutStubs(Listing(folder)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-3, AC-4: a failed codesign call stops the script: exit 2 while signing (nothing later is signed), exit 3
    // when the verification fails.
    [Theory]
    [InlineData("codesign --force*editor.js*", 2, 1)]
    [InlineData("codesign --force*/Context.Desktop", 2, 2)]
    [InlineData("codesign --force*/Context.app", 2, 3)]
    [InlineData("codesign --verify*", 3, 4)]
    public async Task MacSignScript_StopsAtTheFirstFailedCall(string fail, int code, int calls)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);

            ScriptResult result = await RunMac(
                folder,
                SignCall("out/Context.app", SigningIdentity, "ent.plist"),
                new Dictionary<string, string> { ["STUB_FAIL"] = fail });

            Assert.Equal(code, result.Code);
            Assert.Equal(calls, MacLog(folder).Count);
            Assert.Contains("macos-sign.sh: ", result.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-7: zip, submit and wait, staple, validate, assess, then the final zip again from the stapled app.
    [Fact]
    public async Task MacNotarizeScript_SubmitsStaplesAssesses_ThenWritesTheZip()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);
            File.WriteAllText(Path.Combine(folder, "key.p8"), NotaryKey);

            ScriptResult result = await RunMac(
                folder, NotarizeCall("out/Context.app", "zips/Context-1.2.3-osx-arm64.zip", "key.p8", NotaryKeyId, NotaryIssuer));

            Assert.True(result.Code == 0, "exit " + result.Code + ": " + result.Err);
            Assert.Equal(
                [
                    "ditto -c -k --keepParent out/Context.app zips/notarize.X/submit.zip",
                    "xcrun notarytool submit zips/notarize.X/submit.zip --key key.p8 --key-id " + NotaryKeyId + " --issuer "
                        + NotaryIssuer + " --wait",
                    "xcrun stapler staple out/Context.app",
                    "xcrun stapler validate out/Context.app",
                    "spctl --assess --type execute out/Context.app",
                    "ditto -c -k --keepParent out/Context.app zips/notarize.X/final.zip",
                ],
                MacLog(folder).Where(line => !line.StartsWith("p8 ", StringComparison.Ordinal)));
            // Only the final zip is left next to it, and the key never reaches the output.
            Assert.Equal(["Context-1.2.3-osx-arm64.zip"], Entries(Path.Combine(folder, "zips")));
            Assert.Contains("status: Accepted", result.Out, StringComparison.Ordinal);
            Assert.DoesNotContain(NotaryKey, result.Out + result.Err, StringComparison.Ordinal);
            Assert.Equal(string.Empty, result.Err);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-3, AC-7: any failed step (also exit code 0 with the status Invalid) exits non-zero with the stage's
    // code, calls nothing after it and leaves no file at the zip's name.
    [Theory]
    [InlineData("ditto*submit.zip", false, 2, 1)]
    [InlineData("xcrun notarytool*", false, 2, 2)]
    [InlineData("", true, 2, 2)]
    [InlineData("xcrun stapler staple*", false, 3, 3)]
    [InlineData("xcrun stapler validate*", false, 3, 4)]
    [InlineData("spctl*", false, 4, 5)]
    [InlineData("ditto*final.zip", false, 5, 6)]
    public async Task MacNotarizeScript_FailedStep_LeavesNoZip(string fail, bool invalid, int code, int calls)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);
            File.WriteAllText(Path.Combine(folder, "key.p8"), NotaryKey);
            Dictionary<string, string> environment = [];
            if (fail.Length > 0)
            {
                environment["STUB_FAIL"] = fail;
            }

            if (invalid)
            {
                environment["STUB_INVALID_AT"] = "0";
            }

            ScriptResult result = await RunMac(
                folder,
                NotarizeCall("out/Context.app", "zips/Context-1.2.3-osx-arm64.zip", "key.p8", NotaryKeyId, NotaryIssuer),
                environment);

            Assert.Equal(code, result.Code);
            Assert.Equal(calls, MacLog(folder).Count(line => !line.StartsWith("p8 ", StringComparison.Ordinal)));
            Assert.Empty(Entries(Path.Combine(folder, "zips")));
            Assert.Contains("macos-notarize.sh: ", result.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(NotaryKey, result.Out + result.Err, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-7: a wrong or missing argument is refused before anything is created or called.
    [Theory]
    [InlineData("no arguments")]
    [InlineData("four arguments")]
    [InlineData("six arguments")]
    [InlineData("empty key id")]
    [InlineData("empty issuer")]
    [InlineData("no bundle")]
    [InlineData("no key file")]
    [InlineData("zip already there")]
    public async Task MacNotarizeScript_Refuses_BeforeAnythingIsCreated(string change)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            WriteApp(folder);
            string app = "out/Context.app";
            string zip = "zips/x.zip";
            if (change != "no key file")
            {
                File.WriteAllText(Path.Combine(folder, "key.p8"), NotaryKey);
            }

            if (change == "no bundle")
            {
                Directory.Delete(Path.Combine(folder, "out"), recursive: true);
            }
            else if (change == "zip already there")
            {
                File.WriteAllText(Path.Combine(folder, "zips", "x.zip"), "old");
            }

            string[] arguments = change switch
            {
                "no arguments" => [],
                "four arguments" => [app, zip, "key.p8", NotaryKeyId],
                "six arguments" => [app, zip, "key.p8", NotaryKeyId, NotaryIssuer, "extra"],
                "empty key id" => [app, zip, "key.p8", string.Empty, NotaryIssuer],
                "empty issuer" => [app, zip, "key.p8", NotaryKeyId, string.Empty],
                _ => [app, zip, "key.p8", NotaryKeyId, NotaryIssuer],
            };
            SortedDictionary<string, DateTime> before = Listing(folder);

            ScriptResult result = await RunMac(folder, [MacScript("macos-notarize.sh"), .. arguments]);

            Assert.Equal(1, result.Code);
            Assert.Equal(string.Empty, result.Out);
            Assert.Single(result.Err.TrimEnd('\n').Split('\n'));
            Assert.Empty(MacLog(folder));
            Assert.Equal(before, WithoutStubs(Listing(folder)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-4, AC-7: both scripts are POSIX sh in the style of macos-app.sh and never evaluate an argument.
    [Theory]
    [InlineData("macos-sign.sh")]
    [InlineData("macos-notarize.sh")]
    public void MacSigningScripts_EvaluateNothing(string script)
    {
        string source = ScriptSource(script);
        string code = CodeLines(source);

        Assert.StartsWith("#!/bin/sh\n", source, StringComparison.Ordinal);
        Assert.Contains("set -eu\n", code, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", code, StringComparison.Ordinal);
        Assert.DoesNotContain("set -x", code, StringComparison.Ordinal);
        Assert.DoesNotContain("curl", code, StringComparison.Ordinal);
        Assert.DoesNotContain("-s -", code, StringComparison.Ordinal);
        Assert.DoesNotContain("gh ", code, StringComparison.Ordinal);
        if (script == "macos-sign.sh")
        {
            // --deep only verifies; the three signing calls never have it.
            Assert.Equal("codesign --verify --deep --strict \"$app\"", Assert.Single(
                Lines(source), line => line.Contains("--deep", StringComparison.Ordinal))
                .Replace("if ! ", string.Empty, StringComparison.Ordinal)
                .Replace("; then", string.Empty, StringComparison.Ordinal));
            Assert.Equal(
                3,
                Regex.Matches(code, Regex.Escape(
                    "codesign --force --timestamp --options runtime --entitlements \"$entitlements\" -s \"$identity\"")).Count);
        }
    }

    // 146 AC-3, AC-6, AC-8: the signing step with every secret set: a new keychain under $RUNNER_TEMP with a random
    // password, the decoded .p12 and the .p8 readable only by the user, the one Developer ID identity by its hash,
    // then per architecture bundle, sign, notarize; both zips at their asset names; keychain and key files removed;
    // no secret value in the output.
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.4-develop.7")]
    public async Task SigningStep_SignsAndNotarizesBothArchitectures_AndCleansUp(string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            ScriptResult result = await RunSigningStep(folder, version);

            Assert.True(result.Code == 0, "exit " + result.Code + ": " + result.Err);
            string keychain = "runner-temp/macos-signing.X/signing.keychain-db";
            List<string> expected =
            [
                "security create-keychain -p <random> " + keychain,
                "security set-keychain-settings -lut 21600 " + keychain,
                "security unlock-keychain -p <random> " + keychain,
                "security import runner-temp/macos-signing.X/certificate.p12 -k " + keychain + " -f pkcs12 -P "
                    + CertificatePassword + " -T /usr/bin/codesign",
                "p12 -rw------- " + CertificateBytes,
                "security set-key-partition-list -S apple-tool:,apple: -s -k <random> " + keychain,
                "security list-keychains -d user -s " + keychain,
                "security find-identity -v -p codesigning " + keychain,
            ];
            foreach (string rid in new[] { "osx-arm64", "osx-x64" })
            {
                string app = "app/" + rid + "/Context.app";
                string macos = app + "/Contents/MacOS/";
                string sign = "codesign --force --timestamp --options runtime --entitlements"
                    + " .github/scripts/macos-entitlements.plist -s " + SigningIdentity + " ";
                expected.AddRange(
                [
                    sign + macos + "Context.Desktop.dll " + macos + "wwwroot/editor/editor.js " + macos + "wwwroot/index.html",
                    sign + macos + "Context.Desktop",
                    sign + app,
                    "codesign --verify --deep --strict " + app,
                    "ditto -c -k --keepParent " + app + " ./notarize.X/submit.zip",
                    "xcrun notarytool submit ./notarize.X/submit.zip --key runner-temp/macos-signing.X/notary-key.p8 --key-id "
                        + NotaryKeyId + " --issuer " + NotaryIssuer + " --wait",
                    "p8 -rw------- " + NotaryKey,
                    "xcrun stapler staple " + app,
                    "xcrun stapler validate " + app,
                    "spctl --assess --type execute " + app,
                    "ditto -c -k --keepParent " + app + " ./notarize.X/final.zip",
                ]);
            }

            expected.Add("security delete-keychain " + keychain);
            Assert.Equal(expected, MacLog(folder));

            // AC-8: the same two asset names as the ad-hoc path, each written from its own stapled app.
            foreach (string rid in new[] { "osx-arm64", "osx-x64" })
            {
                Assert.Equal(
                    "zip of app/" + rid + "/Context.app\n",
                    File.ReadAllText(Path.Combine(folder, "Context-" + version + "-" + rid + ".zip")));
            }

            Assert.DoesNotContain(result.FilesCreated, name => name.StartsWith("notarize.", StringComparison.Ordinal));
            // AC-6: the keychain, the .p12 and the .p8 are gone.
            Assert.Empty(Entries(Path.Combine(folder, "runner-temp")));
            AssertNoSecret(result);
            // The random keychain password is 48 hex characters, new on every run.
            Assert.Matches("^security create-keychain -p [0-9a-f]{48} ", StubLog(folder)[0]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-3: a missing or empty secret fails the step before any tool is called, with one stderr line that names
    // the variable; nothing is bundled, zipped or left under $RUNNER_TEMP.
    [Theory]
    [InlineData("MACOS_CERTIFICATE_P12", false)]
    [InlineData("MACOS_CERTIFICATE_P12", true)]
    [InlineData("MACOS_CERTIFICATE_PASSWORD", false)]
    [InlineData("MACOS_CERTIFICATE_PASSWORD", true)]
    [InlineData("NOTARY_KEY_P8", false)]
    [InlineData("NOTARY_KEY_P8", true)]
    [InlineData("NOTARY_KEY_ID", false)]
    [InlineData("NOTARY_KEY_ID", true)]
    [InlineData("NOTARY_ISSUER_ID", false)]
    [InlineData("NOTARY_ISSUER_ID", true)]
    public async Task SigningStep_MissingSecret_FailsBeforeAnyTool(string name, bool empty)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            ScriptResult result = await RunSigningStep(
                folder, "1.2.3", new Dictionary<string, string?> { [name] = empty ? string.Empty : null });

            Assert.NotEqual(0, result.Code);
            Assert.Equal(name + " is not set; nothing was signed\n", result.Err);
            Assert.Equal(string.Empty, result.Out);
            Assert.Empty(MacLog(folder));
            Assert.Empty(Entries(Path.Combine(folder, "runner-temp")));
            Assert.DoesNotContain("app", result.FilesCreated);
            Assert.DoesNotContain(result.FilesCreated, file => file.EndsWith(".zip", StringComparison.Ordinal));
            AssertNoSecret(result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-3, AC-6: a failed tool, a notarization that is not Accepted or a certificate without exactly one
    // Developer ID Application identity fails the step with one line of its own naming the stage and the
    // architecture; no file at the failing architecture's asset name, the later architecture is not started,
    // nothing falls back to ad-hoc, and the keychain and key files are removed all the same.
    [Theory]
    [InlineData("security create-keychain*", 0, "", "import", "")]
    [InlineData("security import*", 0, "", "import", "")]
    [InlineData("security set-key-partition-list*", 0, "", "import", "")]
    [InlineData("security find-identity*", 0, "", "import", "")]
    [InlineData("", 0, "no identity", "import", "")]
    [InlineData("", 0, "two identities", "import", "")]
    [InlineData("", 0, "other identity", "import", "")]
    [InlineData("codesign --force*", 0, "", "sign", "osx-arm64")]
    [InlineData("codesign --force*", 2, "", "sign", "osx-arm64")]
    [InlineData("codesign --force*", 3, "", "sign", "osx-x64")]
    [InlineData("codesign --verify*", 0, "", "verify", "osx-arm64")]
    [InlineData("codesign --verify*", 1, "", "verify", "osx-x64")]
    [InlineData("xcrun notarytool*", 0, "", "notarize", "osx-arm64")]
    [InlineData("xcrun notarytool*", 1, "", "notarize", "osx-x64")]
    [InlineData("", 0, "invalid 0", "notarize", "osx-arm64")]
    [InlineData("", 0, "invalid 1", "notarize", "osx-x64")]
    [InlineData("xcrun stapler staple*", 0, "", "staple", "osx-arm64")]
    [InlineData("xcrun stapler staple*", 1, "", "staple", "osx-x64")]
    [InlineData("xcrun stapler validate*", 1, "", "staple", "osx-x64")]
    [InlineData("spctl*", 0, "", "assess", "osx-arm64")]
    [InlineData("spctl*", 1, "", "assess", "osx-x64")]
    [InlineData("ditto*final.zip", 1, "", "notarize", "osx-x64")]
    public async Task SigningStep_FailedStage_FailsClosed(string fail, int skip, string answer, string stage, string rid)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = NewTempFolder();
        try
        {
            Dictionary<string, string?> environment = new Dictionary<string, string?>
            {
                ["STUB_FAIL_SKIP"] = skip.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (fail.Length > 0)
            {
                environment["STUB_FAIL"] = fail;
            }

            if (answer.StartsWith("invalid ", StringComparison.Ordinal))
            {
                environment["STUB_INVALID_AT"] = answer["invalid ".Length..];
            }

            string identity = "  1) " + SigningIdentity + " \"Developer ID Application: Example (TEAM123456)\"\n";
            string? identities = answer switch
            {
                "no identity" => "     0 valid identities found\n",
                "two identities" => identity
                    + "  2) 89ABCDEF0123456789ABCDEF0123456789ABCDEF \"Developer ID Application: Other (TEAM654321)\"\n"
                    + "     2 valid identities found\n",
                "other identity" => "  1) " + SigningIdentity + " \"Apple Development: Example (TEAM123456)\"\n"
                    + "     1 valid identities found\n",
                _ => null,
            };

            ScriptResult result = await RunSigningStep(folder, "1.2.3", environment, identities);

            Assert.NotEqual(0, result.Code);
            string where = rid.Length > 0 ? rid : "before any architecture";
            Assert.Equal(
                ["macOS signing failed: " + stage + " (" + where + "); nothing is uploaded"],
                result.Err.Split('\n').Where(line => line.StartsWith("macOS signing failed", StringComparison.Ordinal)));

            List<string> log = MacLog(folder);
            // No file at the failing architecture's asset name, and the later architecture was not started.
            if (rid != "osx-x64")
            {
                Assert.DoesNotContain(result.FilesCreated, file => file.EndsWith(".zip", StringComparison.Ordinal));
                Assert.DoesNotContain(log, line => line.Contains("osx-x64", StringComparison.Ordinal));
                Assert.False(Directory.Exists(Path.Combine(folder, "app", "osx-x64")));
            }
            else
            {
                // The finished arm64 zip may remain; it is never uploaded because the step failed.
                Assert.Equal(
                    ["Context-1.2.3-osx-arm64.zip"],
                    result.FilesCreated.Where(file => file.EndsWith(".zip", StringComparison.Ordinal)));
            }

            if (rid.Length == 0)
            {
                Assert.False(Directory.Exists(Path.Combine(folder, "app")));
                Assert.DoesNotContain(log, line => line.StartsWith("codesign", StringComparison.Ordinal));
            }

            // Nothing falls back to ad-hoc, and signing never uses --deep.
            Assert.All(log.Where(line => line.StartsWith("codesign --force", StringComparison.Ordinal)), line =>
            {
                Assert.DoesNotContain(" -s - ", line, StringComparison.Ordinal);
                Assert.DoesNotContain("--deep", line, StringComparison.Ordinal);
                Assert.Contains(" -s " + SigningIdentity + " ", line, StringComparison.Ordinal);
            });
            Assert.DoesNotContain(result.FilesCreated, name => name.StartsWith("notarize.", StringComparison.Ordinal));
            // The trap ran: the keychain, the .p12 and the .p8 are gone.
            Assert.Equal("security delete-keychain runner-temp/macos-signing.X/signing.keychain-db", log[^1]);
            Assert.Empty(Entries(Path.Combine(folder, "runner-temp")));
            AssertNoSecret(result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 146 AC-3: Upload is the step after the signing step and has no if:, so a failed signing step skips it.
    [Fact]
    public void UploadStep_RunsOnlyAfterTheBundleStepsSucceeded()
    {
        List<YamlMapping> steps = JobSteps(WorkflowYaml.Parse(ReadWorkflow()), "desktop-macos");
        List<string> names = [.. steps.Select(step => Scalar(step["name"]))];

        Assert.Equal(names.IndexOf(SigningStep) + 1, names.IndexOf("Upload"));
        Assert.Equal(names.IndexOf(AdHocStep) + 1, names.IndexOf(SigningStep));
        Assert.All(steps, step => Assert.False(step.ContainsKey("continue-on-error")));
        Assert.False(steps[^1].ContainsKey("if"));
        Assert.DoesNotContain("||", Scalar(steps[^1]["run"]), StringComparison.Ordinal);
    }

    private const string SigningIdentity = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string CertificateBytes = "p12-bytes-CERTSECRET";
    private const string CertificatePassword = "p12-password-SECRET";
    private const string NotaryKey = "P8-KEY-TEXT-SECRET";
    private const string NotaryKeyId = "KEYID-SECRET";
    private const string NotaryIssuer = "ISSUER-SECRET";

    /// <summary>The Windows, update and console-tool text every release notes header has (146 AC-9).</summary>
    private static void AssertSharedReleaseNotes(string notes)
    {
        string text = Regex.Replace(notes, @"\s+", " ");
        Assert.Contains("`More info`", text, StringComparison.Ordinal);
        Assert.Contains("`Run anyway`", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("`More info`", StringComparison.Ordinal) < text.IndexOf("`Run anyway`", StringComparison.Ordinal),
            "More info comes before Run anyway");
        Assert.Contains("not signed", text, StringComparison.Ordinal);
        Assert.Contains("Setup.exe", text, StringComparison.Ordinal);
        Assert.Contains("osx-arm64.zip", text, StringComparison.Ordinal);
        Assert.Contains("osx-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("The installed app keeps itself up to date", text, StringComparison.Ordinal);
        Assert.Contains("The macOS copy does not update itself", text, StringComparison.Ordinal);
        Assert.Contains(
            "- **Console tool:** `Context.<version>.nupkg` is the .NET tool package (command `context`).",
            text,
            StringComparison.Ordinal);
        // Plain Markdown text for `gh release create --notes-file`: nothing a workflow or a shell would expand.
        Assert.DoesNotContain("${{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoSecret(ScriptResult result)
    {
        string output = result.Out + result.Err;
        string[] secrets =
        [
            CertificateBytes, Convert.ToBase64String(Encoding.UTF8.GetBytes(CertificateBytes)), CertificatePassword,
            NotaryKey, NotaryKeyId, NotaryIssuer,
        ];
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, output, StringComparison.Ordinal));
    }

    /// <summary>A listing without the stub tools that <see cref="RunMac(string, string[], Dictionary{string, string}?, string?)"/> adds.</summary>
    private static SortedDictionary<string, DateTime> WithoutStubs(SortedDictionary<string, DateTime> listing)
    {
        SortedDictionary<string, DateTime> rest = new SortedDictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DateTime> entry in listing)
        {
            if (!entry.Key.StartsWith("stub-bin", StringComparison.Ordinal))
            {
                rest[entry.Key] = entry.Value;
            }
        }

        return rest;
    }

    private static string MacScript(string script) => Path.Combine(RepoRoot, ".github", "scripts", script);

    private static string[] SignCall(string app, string identity, string entitlements) =>
        [MacScript("macos-sign.sh"), app, identity, entitlements];

    private static string[] NotarizeCall(string app, string zip, string key, string keyId, string issuer) =>
        [MacScript("macos-notarize.sh"), app, zip, key, keyId, issuer];

    /// <summary>A stand-in Context.app in <c>out/</c>, an entitlements file and an empty <c>zips/</c> folder.</summary>
    private static void WriteApp(string folder)
    {
        WritePublishFolder(folder);
        string contents = Path.Combine(folder, "out", "Context.app", "Contents");
        Directory.CreateDirectory(contents);
        Directory.Move(Path.Combine(folder, "publish"), Path.Combine(contents, "MacOS"));
        File.WriteAllText(Path.Combine(contents, "Info.plist"), "<plist/>\n");
        File.WriteAllText(Path.Combine(folder, "ent.plist"), "<plist/>\n");
        Directory.CreateDirectory(Path.Combine(folder, "zips"));
    }

    /// <summary>
    /// The stub log without the folder's own path and the random parts (the <c>mktemp</c> suffixes and the keychain
    /// password), and with the files of a several-file <c>codesign</c> call sorted (find gives them in any order).
    /// </summary>
    private static List<string> MacLog(string folder)
    {
        List<string> lines = [];
        foreach (string raw in StubLog(folder))
        {
            string line = raw.Replace(folder + "/", string.Empty, StringComparison.Ordinal);
            line = Regex.Replace(line, @"macos-signing\.[A-Za-z0-9]+", "macos-signing.X");
            line = Regex.Replace(line, @"notarize\.[A-Za-z0-9]+/", "notarize.X/");
            line = Regex.Replace(line, @" -(p|k) [0-9a-f]{48} ", " -$1 <random> ");
            Match sign = Regex.Match(line, @"^(codesign --force .* -s \S+) (\S+( \S+)+)$");
            if (sign.Success)
            {
                line = sign.Groups[1].Value + " "
                    + string.Join(' ', sign.Groups[2].Value.Split(' ').Order(StringComparer.Ordinal));
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Runs the signing step of the macOS job in <paramref name="folder"/> (a copy of <c>.github/scripts</c>, a publish
    /// folder per architecture, <c>runner-temp/</c> as <c>$RUNNER_TEMP</c>) with every secret set; a <c>null</c> value
    /// in <paramref name="environment"/> removes a variable.
    /// </summary>
    private static async Task<ScriptResult> RunSigningStep(
        string folder, string version, Dictionary<string, string?>? environment = null, string? identities = null)
    {
        string scripts = Path.Combine(folder, ".github", "scripts");
        Directory.CreateDirectory(scripts);
        foreach (string script in Directory.GetFiles(Path.Combine(RepoRoot, ".github", "scripts")))
        {
            File.Copy(script, Path.Combine(scripts, Path.GetFileName(script)));
        }

        foreach (string rid in new[] { "osx-arm64", "osx-x64" })
        {
            string scratch = Path.Combine(folder, "scratch");
            WritePublishFolder(scratch);
            Directory.CreateDirectory(Path.Combine(folder, "publish"));
            Directory.Move(Path.Combine(scratch, "publish"), Path.Combine(folder, "publish", rid));
            Directory.Delete(scratch);
        }

        Directory.CreateDirectory(Path.Combine(folder, "runner-temp"));
        Dictionary<string, string?> all = new Dictionary<string, string?>
        {
            ["VERSION"] = version,
            ["TAG"] = "v" + version,
            ["MACOS_SIGNING"] = "on",
            ["RUNNER_TEMP"] = Path.Combine(folder, "runner-temp"),
            ["MACOS_CERTIFICATE_P12"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(CertificateBytes)),
            ["MACOS_CERTIFICATE_PASSWORD"] = CertificatePassword,
            ["NOTARY_KEY_P8"] = NotaryKey,
            ["NOTARY_KEY_ID"] = NotaryKeyId,
            ["NOTARY_ISSUER_ID"] = NotaryIssuer,
        };
        foreach (KeyValuePair<string, string?> variable in environment ?? [])
        {
            all[variable.Key] = variable.Value;
        }

        Dictionary<string, string> set = [];
        foreach (KeyValuePair<string, string?> variable in all)
        {
            if (variable.Value is not null)
            {
                set[variable.Key] = variable.Value;
            }
        }

        return await RunMac(folder, JobScript("desktop-macos", SigningStep), set, identities);
    }

    /// <summary>Runs a step's script as the macOS runner does (<c>bash -e</c>) against the stub tools.</summary>
    private static async Task<ScriptResult> RunMac(
        string folder, string script, Dictionary<string, string>? environment = null, string? identities = null)
    {
        string file = Path.Combine(folder, "step.sh");
        File.WriteAllText(file, script);
        return await RunMac(folder, ["--noprofile", "--norc", "-e", file], environment, identities);
    }

    /// <summary>
    /// Runs <c>bash</c> in <paramref name="folder"/> with stub <c>codesign</c>, <c>security</c>, <c>xcrun</c>,
    /// <c>spctl</c> and <c>ditto</c> first on <c>PATH</c>: no real keychain, certificate, Apple account or network. Each
    /// stub logs its name and arguments to <c>stub.log</c>. A call that matches the glob <c>STUB_FAIL</c> exits 1
    /// (after <c>STUB_FAIL_SKIP</c> matching calls passed); the <c>STUB_INVALID_AT</c>-th <c>notarytool</c> call (from
    /// 0) answers <c>Invalid</c> with exit code 0. <c>security import</c> and <c>notarytool</c> also log the mode and
    /// the text of the key file they were given, <c>find-identity</c> prints <paramref name="identities"/>, and
    /// <c>ditto</c> writes its zip.
    /// </summary>
    private static async Task<ScriptResult> RunMac(
        string folder, string[] arguments, Dictionary<string, string>? environment = null, string? identities = null)
    {
        string bin = Path.Combine(folder, "stub-bin");
        Directory.CreateDirectory(bin);
        string stub = "#!/bin/sh\n"
            + "name=\"$(basename \"$0\")\"\n"
            + "line=\"$name $*\"\n"
            + "calls=0\n"
            + "if [ -f \"$STUB_LOG\" ]; then calls=\"$(grep -c '^xcrun notarytool ' \"$STUB_LOG\" || true)\"; fi\n"
            + "printf '%s\\n' \"$line\" >> \"$STUB_LOG\"\n"
            + "case \"$line\" in\n"
            + "  $STUB_FAIL)\n"
            + "    seen=0\n"
            + "    if [ -f \"$STUB_HITS\" ]; then seen=\"$(grep -c '' \"$STUB_HITS\" || true)\"; fi\n"
            + "    echo hit >> \"$STUB_HITS\"\n"
            + "    if [ \"$seen\" -ge \"$STUB_FAIL_SKIP\" ]; then echo \"$name: stub failure\" >&2; exit 1; fi\n"
            + "    ;;\n"
            + "esac\n"
            + "case \"$line\" in\n"
            + "  'security create-keychain '*) : > \"$4\" ;;\n"
            + "  'security delete-keychain '*) rm -f \"$2\" ;;\n"
            + "  'security import '*) printf 'p12 %s %s\\n' \"$(ls -ln \"$2\" | cut -c1-10)\" \"$(cat \"$2\")\" >> \"$STUB_LOG\" ;;\n"
            + "  'security find-identity '*) cat \"$STUB_IDENTITIES\" ;;\n"
            + "  'xcrun notarytool submit '*)\n"
            + "    printf 'p8 %s %s\\n' \"$(ls -ln \"$5\" | cut -c1-10)\" \"$(cat \"$5\")\" >> \"$STUB_LOG\"\n"
            + "    status=Accepted\n"
            + "    if [ \"$calls\" -eq \"$STUB_INVALID_AT\" ]; then status=Invalid; fi\n"
            + "    printf 'Current status: In Progress...\\nCurrent status: %s...\\nProcessing complete\\n  id: 0000-1111\\n  status: %s\\n' \"$status\" \"$status\"\n"
            + "    ;;\n"
            + "  'ditto -c -k --keepParent '*)\n"
            + "    if [ ! -d \"$4\" ]; then exit 1; fi\n"
            + "    printf 'zip of %s\\n' \"$4\" > \"$5\"\n"
            + "    ;;\n"
            + "esac\n"
            + "exit 0\n";
        foreach (string tool in new[] { "codesign", "security", "xcrun", "spctl", "ditto" })
        {
            string path = Path.Combine(bin, tool);
            File.WriteAllText(path, stub);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        string identityFile = Path.Combine(bin, "identities.txt");
        File.WriteAllText(
            identityFile,
            identities ?? "  1) " + SigningIdentity + " \"Developer ID Application: Example (TEAM123456)\"\n"
                + "     1 valid identities found\n");
        Dictionary<string, string> all = new Dictionary<string, string>
        {
            ["HOME"] = folder,
            ["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["STUB_LOG"] = Path.Combine(folder, "stub.log"),
            ["STUB_HITS"] = Path.Combine(bin, "hits.txt"),
            ["STUB_FAIL"] = "no such call",
            ["STUB_FAIL_SKIP"] = "0",
            ["STUB_INVALID_AT"] = "-1",
            ["STUB_IDENTITIES"] = identityFile,
        };
        foreach (KeyValuePair<string, string> variable in environment ?? [])
        {
            all[variable.Key] = variable.Value;
        }

        return await RunBash(folder, arguments, all);
    }

    /// <summary>
    /// Every structural check on the release workflow, on the parsed model only (no raw lines or indents): triggers,
    /// permissions, the job if:, step names and order, the pinned actions, the checkout with:, the Version env:, the
    /// release and prune steps' settings, and no expression in any run: script. Throws on a mismatch.
    /// </summary>
    private static void AssertWorkflowStructure(string yaml)
    {
        YamlMapping root = Map(WorkflowYaml.Parse(yaml));

        // Exact key sets: no workflow- or job-level env:, defaults:, container: or services: next to the write token.
        Assert.Equal(["jobs", "name", "on", "permissions"], Sorted(root.Keys));

        YamlMapping on = Map(root["on"]);
        Assert.Equal(["pull_request", "push"], Sorted(on.Keys));
        YamlMapping push = Map(on["push"]);
        Assert.Equal(["branches", "paths-ignore", "tags"], Sorted(push.Keys));
        Assert.Equal(["develop"], Strings(push["branches"]));
        Assert.Equal(["v*"], Strings(push["tags"]));
        Assert.Equal(["backlog.md"], Strings(push["paths-ignore"]));
        YamlMapping pullRequest = Map(on["pull_request"]);
        Assert.Equal(["branches", "types"], Sorted(pullRequest.Keys));
        Assert.Equal(["closed"], Strings(pullRequest["types"]));
        Assert.Equal(["main"], Strings(pullRequest["branches"]));

        Assert.Equal(["contents=write"], Pairs(root["permissions"]));

        YamlMapping jobs = Map(root["jobs"]);
        Assert.Equal(JobNames, jobs.Keys);
        YamlMapping job = Map(jobs["release"]);
        Assert.Equal(["env", "if", "outputs", "runs-on", "steps"], Sorted(job.Keys));
        // 146 AC-1: the signing switch is the only job-level variable of the release job (no secret).
        Assert.Equal(["MACOS_SIGNING=" + SigningVariable], Pairs(job["env"]));
        Assert.Equal(JobIf, Regex.Replace(Scalar(job["if"]), @"\s+", " ").Trim());
        Assert.Equal("ubuntu-latest", Scalar(job["runs-on"]));
        Assert.Equal(
            [
                "tag=${{ steps.version.outputs.tag }}",
                "target=${{ steps.version.outputs.target }}",
                "version=${{ steps.version.outputs.version }}",
            ],
            Pairs(job["outputs"]));

        List<YamlMapping> steps = Steps(root);
        Assert.Equal(StepNames, steps.Select(step => Scalar(step["name"])));
        Assert.All(steps, step => Assert.False(step.ContainsKey("permissions") || step.ContainsKey("shell")));
        Assert.All(
            steps,
            step => Assert.True(step.ContainsKey("uses") != step.ContainsKey("run"), "each step has uses: or run:"));

        List<YamlScalar> uses = [.. steps
            .Where(step => step.ContainsKey("uses"))
            .Select(step => Assert.IsType<YamlScalar>(step["uses"]))];
        Assert.All(uses, scalar => Assert.Matches("^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$", scalar.Value));
        Assert.All(uses, scalar => Assert.Matches(@"^v\d+\.\d+\.\d+$", scalar.Comment ?? string.Empty));
        Assert.Equal([CheckoutAction, SetupDotnetAction], uses.Select(scalar => scalar.Value));
        Assert.Equal(["v4.4.0", "v4.3.1"], uses.Select(scalar => scalar.Comment));
        Assert.Equal(CheckoutAction, Scalar(steps[0]["uses"]));
        Assert.Equal(SetupDotnetAction, Scalar(steps[1]["uses"]));

        Assert.Equal(
            ["fetch-depth=0", "persist-credentials=false", "ref=${{ github.event.pull_request.merge_commit_sha }}"],
            Pairs(steps[0]["with"]));
        Assert.Equal(["dotnet-version=10.0.x"], Pairs(steps[1]["with"]));

        YamlMapping version = steps[2];
        Assert.Equal(["env", "id", "name", "run"], Sorted(version.Keys));
        Assert.Equal("version", Scalar(version["id"]));
        Assert.Equal(
            [
                "MERGE_SHA=${{ github.event.pull_request.merge_commit_sha }}",
                "PR_HEAD_REF=${{ github.event.pull_request.head.ref }}",
                "PR_HEAD_REPO=${{ github.event.pull_request.head.repo.full_name }}",
                "PR_MERGED=${{ github.event.pull_request.merged }}",
                "PR_TITLE=${{ github.event.pull_request.title }}",
            ],
            Pairs(version["env"]));

        Assert.Equal(["name", "run"], Sorted(steps[3].Keys));
        Assert.Equal(TestCommand, Scalar(steps[3]["run"]).Trim());
        Assert.Equal(["name", "run"], Sorted(steps[4].Keys));
        Assert.Equal(PackCommand, Scalar(steps[4]["run"]).Trim());
        Assert.Equal(["name", "run"], Sorted(steps[5].Keys));

        // 135 AC-1: the release is created as a draft, by the last step of this job.
        YamlMapping release = steps[6];
        Assert.Equal(7, steps.Count);
        Assert.Equal(["env", "name", "run"], Sorted(release.Keys));
        Assert.Equal(["GH_TOKEN=${{ github.token }}"], Pairs(release["env"]));
        Assert.Equal(
            ["PRERELEASE=\"\"", "case \"$VERSION\" in", "*-*) PRERELEASE=\"--prerelease\" ;;", "esac", NotesCommand, ReleaseCommand],
            Lines(Scalar(release["run"])).Where(line => !line.StartsWith('#')));

        // Event fields, refs and the token reach a script only as environment variables.
        Assert.All(
            steps.Where(step => step.ContainsKey("run")),
            step => Assert.DoesNotContain("${{", Scalar(step["run"]), StringComparison.Ordinal));

        AssertDesktopJob(root, "desktop-macos", "macos-latest", MacStepNames, ["Upload"]);
        AssertDesktopJob(root, "desktop-windows", "windows-latest", WindowsStepNames, ["Pack", "Upload"]);

        // Only the Windows job sets a shell, and only as its job default.
        Assert.False(Map(jobs["desktop-macos"]).ContainsKey("defaults"));
        YamlMapping defaults = Map(Map(jobs["desktop-windows"])["defaults"]);
        Assert.Equal(["run"], defaults.Keys);
        Assert.Equal(["shell=bash"], Pairs(defaults["run"]));

        AssertPublishJob(root);

        // 135 AC-8: one script creates the release, one publishes it, and nothing outside publish ends the draft.
        List<string> all = [.. jobs.Keys.SelectMany(name => RunScriptsOf(root, name))];
        Assert.Single(all, script => script.Contains("gh release create", StringComparison.Ordinal));
        Assert.Single(all, script => script.Contains("gh release edit", StringComparison.Ordinal));
        Assert.All(
            jobs.Keys.Where(name => name != "publish").SelectMany(name => RunScriptsOf(root, name)),
            script => Assert.DoesNotContain("--draft=false", script, StringComparison.Ordinal));
        Assert.All(
            jobs.Keys.Where(name => name != "publish").SelectMany(name => RunScriptsOf(root, name)),
            script => Assert.DoesNotContain("gh release edit", script, StringComparison.Ordinal));
        Assert.All(
            RunScriptsOf(root, "desktop-macos").Concat(RunScriptsOf(root, "desktop-windows")),
            script => Assert.DoesNotContain("gh release create", script, StringComparison.Ordinal));
    }

    /// <summary>
    /// 135 AC-3, AC-4, AC-7: the publish job needs all three other jobs and has no if:, permissions: or outputs:
    /// (so a failed or skipped needed job skips it), takes version, tag and target only through its env:, checks
    /// out the release's commit without keeping credentials, uses no other action, re-checks the version first,
    /// and gives the token only to the publish and prune steps, in that order.
    /// </summary>
    private static void AssertPublishJob(YamlNode root)
    {
        YamlMapping job = Map(Map(Map(root)["jobs"])["publish"]);
        Assert.Equal(["env", "needs", "runs-on", "steps"], Sorted(job.Keys));
        Assert.Equal(["release", "desktop-macos", "desktop-windows"], Strings(job["needs"]));
        Assert.Equal("ubuntu-latest", Scalar(job["runs-on"]));
        Assert.Equal(["TAG=" + TagOutput, "TARGET=" + TargetOutput, "VERSION=" + VersionOutput], Pairs(job["env"]));

        List<YamlMapping> steps = JobSteps(root, "publish");
        Assert.Equal(PublishStepNames, steps.Select(step => Scalar(step["name"])));
        Assert.All(steps, step => Assert.False(
            step.ContainsKey("permissions") || step.ContainsKey("shell") || step.ContainsKey("if")));
        Assert.All(
            steps,
            step => Assert.True(step.ContainsKey("uses") != step.ContainsKey("run"), "each step has uses: or run:"));

        YamlScalar uses = Assert.IsType<YamlScalar>(Assert.Single(steps, step => step.ContainsKey("uses"))["uses"]);
        Assert.Equal(CheckoutAction, uses.Value);
        Assert.Equal("v4.4.0", uses.Comment);
        Assert.Equal(["name", "uses", "with"], Sorted(steps[0].Keys));
        Assert.Equal(["persist-credentials=false", "ref=" + TargetOutput], Pairs(steps[0]["with"]));

        AssertVersionCheck(steps[1]);

        YamlMapping publish = steps[2];
        Assert.Equal(["env", "name", "run"], Sorted(publish.Keys));
        Assert.Equal(["GH_TOKEN=${{ github.token }}"], Pairs(publish["env"]));
        YamlMapping prune = steps[3];
        Assert.Equal(["continue-on-error", "env", "name", "run"], Sorted(prune.Keys));
        Assert.Equal("true", Scalar(prune["continue-on-error"]));
        Assert.Equal(["GH_TOKEN=${{ github.token }}"], Pairs(prune["env"]));

        // Read first, publish last: one view, then one edit as the script's last command, and nothing after a
        // failed check (every check ends in exit 1, no `||` that could swallow a failure).
        string script = Scalar(publish["run"]);
        List<string> lines = [.. Lines(script).Where(line => !line.StartsWith('#'))];
        Assert.Equal("gh release edit \"$TAG\" --draft=false $PRE", lines[^1]);
        Assert.Single(lines, line => line.Contains("gh release edit", StringComparison.Ordinal));
        Assert.Equal("INFO=\"$(" + ViewCommand + ")\"", Assert.Single(lines, line => line.Contains("gh release view", StringComparison.Ordinal)));
        Assert.Equal(2, Regex.Matches(script, @"\bgh ").Count);
        Assert.DoesNotContain("||", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--draft=false", Scalar(prune["run"]), StringComparison.Ordinal);
        Assert.Contains("old-prereleases.sh", Scalar(prune["run"]), StringComparison.Ordinal);

        // The needs.* values reach a script only through env:, never inside a run: script.
        Assert.All(
            steps.Where(step => step.ContainsKey("run")),
            step => Assert.DoesNotContain("${{", Scalar(step["run"]), StringComparison.Ordinal));
    }

    /// <summary>The SemVer and tag re-check that every job after <c>release</c> runs as its first script.</summary>
    private static void AssertVersionCheck(YamlMapping step, int exits = 2)
    {
        Assert.Equal(["name", "run"], Sorted(step.Keys));
        string check = Scalar(step["run"]);
        Assert.Contains("if ! printf '%s' \"$VERSION\" | " + SemVerGrep + "; then", check, StringComparison.Ordinal);
        Assert.Contains("if [ \"$TAG\" != \"v$VERSION\" ]; then", check, StringComparison.Ordinal);
        Assert.Equal(exits, Regex.Matches(check, @"(?m)^\s*exit 1\s*$").Count);
        Assert.DoesNotContain("dotnet", check, StringComparison.Ordinal);
        Assert.DoesNotContain("gh ", check, StringComparison.Ordinal);
        Assert.DoesNotContain("||", check, StringComparison.Ordinal);
    }

    /// <summary>
    /// 133 AC-1: a desktop job needs the release job, has no if:, permissions: or outputs: of its own, takes version
    /// and tag only through its env:, checks out the release's commit without keeping credentials, uses only the two
    /// pinned actions, re-checks the version before any dotnet call, and gives the token only to the named steps.
    /// </summary>
    private static void AssertDesktopJob(
        YamlNode root, string name, string runner, string[] stepNames, string[] tokenSteps)
    {
        YamlMapping job = Map(Map(Map(root)["jobs"])[name]);
        List<string> keys = Sorted(job.Keys);
        keys.Remove("defaults");
        Assert.Equal(["env", "needs", "runs-on", "steps"], keys);
        Assert.Equal("release", Scalar(job["needs"]));
        Assert.Equal(runner, Scalar(job["runs-on"]));
        bool mac = name == "desktop-macos";
        Assert.Equal(
            mac
                ? ["MACOS_SIGNING=" + SigningVariable, "TAG=" + TagOutput, "VERSION=" + VersionOutput]
                : ["TAG=" + TagOutput, "VERSION=" + VersionOutput],
            Pairs(job["env"]));

        List<YamlMapping> steps = JobSteps(root, name);
        Assert.Equal(stepNames, steps.Select(step => Scalar(step["name"])));
        Assert.All(steps, step => Assert.False(
            step.ContainsKey("permissions") || step.ContainsKey("shell") || step.ContainsKey("continue-on-error")));
        // Only the two bundle steps of the macOS job have an if: (checked below); a failed step still stops the job.
        Assert.All(steps, step => Assert.True(
            !step.ContainsKey("if") || (mac && (Scalar(step["name"]) == AdHocStep || Scalar(step["name"]) == SigningStep))));
        Assert.All(
            steps,
            step => Assert.True(step.ContainsKey("uses") != step.ContainsKey("run"), "each step has uses: or run:"));

        List<YamlScalar> uses = [.. steps
            .Where(step => step.ContainsKey("uses"))
            .Select(step => Assert.IsType<YamlScalar>(step["uses"]))];
        Assert.Equal([CheckoutAction, SetupDotnetAction], uses.Select(scalar => scalar.Value));
        Assert.Equal(["v4.4.0", "v4.3.1"], uses.Select(scalar => scalar.Comment));
        Assert.Equal(["name", "uses", "with"], Sorted(steps[0].Keys));
        Assert.Equal(["persist-credentials=false", "ref=" + TargetOutput], Pairs(steps[0]["with"]));
        Assert.Equal(["name", "uses", "with"], Sorted(steps[2].Keys));
        Assert.Equal(["dotnet-version=10.0.x"], Pairs(steps[2]["with"]));

        // The SemVer re-check is the first script, so it runs before any dotnet call (and before setup-dotnet).
        AssertVersionCheck(steps[1], mac ? 3 : 2);
        if (mac)
        {
            // 146 AC-1: the switch is checked in the first script: only empty, off or on.
            Assert.Equal(
                ["case \"$MACOS_SIGNING\" in", "'' | off | on) ;;", "*)", "echo \"MACOS_SIGNING must be empty, 'off' or 'on'\" >&2", "exit 1", ";;", "esac"],
                Lines(Scalar(steps[1]["run"])).SkipWhile(line => !line.StartsWith("case ", StringComparison.Ordinal)));
        }

        foreach (YamlMapping step in steps.Skip(3))
        {
            if (mac && Scalar(step["name"]) == AdHocStep)
            {
                // 146 AC-3: the ad-hoc path runs only while signing is not on.
                Assert.Equal(["if", "name", "run"], Sorted(step.Keys));
                Assert.Equal("env.MACOS_SIGNING != 'on'", Scalar(step["if"]));
            }
            else if (mac && Scalar(step["name"]) == SigningStep)
            {
                // 146 AC-2: the one step with secrets: its if:, the five secrets and no token.
                Assert.Equal(["env", "if", "name", "run"], Sorted(step.Keys));
                Assert.Equal("env.MACOS_SIGNING == 'on'", Scalar(step["if"]));
                Assert.Equal(
                    Sorted(SigningSecrets.Select(secret => secret + "=${{ secrets." + secret + " }}")), Pairs(step["env"]));
            }
            else if (tokenSteps.Contains(Scalar(step["name"])))
            {
                Assert.Equal(["env", "name", "run"], Sorted(step.Keys));
                Assert.Equal(["GH_TOKEN=${{ github.token }}"], Pairs(step["env"]));
            }
            else
            {
                Assert.Equal(["name", "run"], Sorted(step.Keys));
            }
        }

        // The needs.* values reach a script only through env:, never inside a run: script.
        Assert.All(
            steps.Where(step => step.ContainsKey("run")),
            step => Assert.DoesNotContain("${{", Scalar(step["run"]), StringComparison.Ordinal));
    }

    /// <summary>
    /// The real workflow with 4-space indents, other quoting, extra comments and blank lines, a folded if: and
    /// reordered step keys.
    /// </summary>
    private static string ReformattedWorkflow(string workflow, string develop)
    {
        string text = workflow;
        text = ReplaceOnce(text, "      - \"develop\"\n", "      - " + develop + "\n");
        text = ReplaceOnce(text, "      - \"main\"\n", "      - main\n");
        text = ReplaceNth(text, "dotnet-version: \"10.0.x\"\n", "dotnet-version: '10.0.x'   # any 10.0 patch\n", 0);
        text = ReplaceNth(text, "dotnet-version: \"10.0.x\"\n", "dotnet-version: 10.0.x\n", 1);
        text = ReplaceNth(text, "    needs: release\n", "    needs: 'release' # the version comes from there\n", 0);
        text = ReplaceOnce(
            text,
            "    defaults:\n      run:\n        shell: bash\n",
            "    defaults:\n\n      # Git Bash on the Windows runner\n      run:\n        shell: \"bash\"\n");
        text = ReplaceOnce(
            text,
            "    needs:\n      - release\n      - desktop-macos\n",
            "    needs: # every other job\n\n      - \"release\"\n      - 'desktop-macos'\n");
        for (int i = 0; i < 4; i++)
        {
            text = ReplaceNth(
                text,
                "      - name: Check out\n        uses: " + CheckoutAction + " # v4.4.0\n",
                "      - uses: " + CheckoutAction + "    # v4.4.0\n"
                    + "        # name after uses\n        name: \"Check out\"\n",
                0);
        }

        text = ReplaceOnce(
            text,
            "        continue-on-error: true\n        env:\n          GH_TOKEN: ${{ github.token }}\n",
            "        env:\n\n          GH_TOKEN: ${{ github.token }}\n        continue-on-error: 'true'\n");
        text = ReplaceOnce(
            text,
            "    if: github.event_name == 'push' || (",
            "    if: >\n      github.event_name == 'push' ||\n        (");
        text = ReplaceOnce(text, "\non:\n", "\non: # triggers\n\n  # every push\n");
        text = ReplaceOnce(
            text,
            "permissions:\n  contents: write\n",
            "permissions:   # token scope\n\n  contents: write # nothing else\n");
        text = ReplaceOnce(text, "      - name: Test\n", "\n      # an extra comment\n\n      - name: Test\n");

        return string.Join('\n', text.Split('\n').Select(line =>
        {
            int indent = line.Length - line.TrimStart(' ').Length;
            string rest = line[indent..];
            if (rest.StartsWith("- ", StringComparison.Ordinal))
            {
                rest = "-   " + rest[2..];
            }

            return new string(' ', indent * 2) + rest;
        }));
    }

    private sealed class ScriptResult
    {
        public ScriptResult(int code, string output, string error, string[] filesCreated)
        {
            Code = code;
            Out = output;
            Err = error;
            FilesCreated = filesCreated;
        }

        public int Code { get; }

        public string Out { get; }

        public string Err { get; }

        /// <summary>What the script left in its (fresh, empty) working folder.</summary>
        public string[] FilesCreated { get; }
    }

    /// <summary>
    /// Runs <c>.github/scripts/&lt;script&gt;</c> with <c>bash</c>, one argument and <paramref name="stdin"/>, in a
    /// fresh empty folder (so anything a hostile argument managed to run would leave a trace there).
    /// </summary>
    private static async Task<ScriptResult> RunScript(string script, string argument, string stdin, string locale)
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("bash")
            {
                WorkingDirectory = folder,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.Combine(RepoRoot, ".github", "scripts", script));
            startInfo.ArgumentList.Add(argument);
            if (locale.Length > 0)
            {
                startInfo.Environment["LC_ALL"] = locale;
                startInfo.Environment["LANG"] = locale;
            }

            using Process process = Process.Start(startInfo)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            try
            {
                Stream input = process.StandardInput.BaseStream;
                await input.WriteAsync(Encoding.UTF8.GetBytes(stdin));
                input.Close();
            }
            catch (IOException)
            {
                // The script refuses a bad argument before it reads stdin, so the pipe can already be closed.
            }

            string stdout = await output;
            string stderr = await error;
            await process.WaitForExitAsync();
            string[] files = [.. Directory.GetFileSystemEntries(folder).Select(path => Path.GetFileName(path) ?? "")];
            return new ScriptResult(process.ExitCode, stdout, stderr, files);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static readonly string MacAppScript = Path.Combine(FindRepoRoot(), ".github", "scripts", "macos-app.sh");

    /// <summary>The trimmed, non-empty, non-comment lines of a script.</summary>
    private static string[] Lines(string script) =>
        [.. script.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    private static string NewTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A stand-in for a <c>dotnet publish</c> output: the executable, a library and a nested file.</summary>
    private static void WritePublishFolder(string folder)
    {
        string publish = Path.Combine(folder, "publish");
        Directory.CreateDirectory(Path.Combine(publish, "wwwroot", "editor"));
        File.WriteAllText(Path.Combine(publish, "Context.Desktop"), "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(publish, "Context.Desktop"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(Path.Combine(publish, "Context.Desktop.dll"), "dll");
        File.WriteAllText(Path.Combine(publish, "wwwroot", "index.html"), "<!doctype html>\n");
        File.WriteAllText(Path.Combine(publish, "wwwroot", "editor", "editor.js"), "// editor\n");
    }

    private static void WriteReleases(string folder, IEnumerable<string> files)
    {
        string releases = Path.Combine(folder, "Releases");
        Directory.CreateDirectory(releases);
        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(releases, file), file);
        }
    }

    private static List<string> Entries(string folder) =>
        [.. Directory.GetFileSystemEntries(folder).Select(path => Path.GetFileName(path) ?? "").Order(StringComparer.Ordinal)];

    private static List<string> RelativeFiles(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path))
            .Order(StringComparer.Ordinal)];

    /// <summary>Every file and folder under <paramref name="folder"/> with its last-write time.</summary>
    private static SortedDictionary<string, DateTime> Listing(string folder)
    {
        SortedDictionary<string, DateTime> entries = new SortedDictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            entries[Path.GetRelativePath(folder, path)] = File.GetLastWriteTimeUtc(path);
        }

        return entries;
    }

    /// <summary>The <c>key</c>/<c>string</c> pairs of an Info.plist (the DTD is not fetched).</summary>
    private static Dictionary<string, string> ReadPlist(string path)
    {
        XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XElement dict = Assert.Single(XDocument.Load(reader).Root!.Elements("dict"));
        Dictionary<string, string> values = [];
        List<XElement> children = [.. dict.Elements()];
        for (int i = 0; i + 1 < children.Count; i += 2)
        {
            Assert.Equal("key", children[i].Name.LocalName);
            values.Add(children[i].Value, children[i + 1].Value);
        }

        return values;
    }

    /// <summary>The lines the stub commands logged: <c>&lt;command&gt; [token|no token] &lt;arguments&gt;</c>.</summary>
    private static List<string> StubLog(string folder)
    {
        string path = Path.Combine(folder, "stub.log");
        return File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
    }

    /// <summary>The six assets a release must hold before it is published (135 AC-5), in the spec's order.</summary>
    private static List<string> PublishAssets(string version)
    {
        bool prerelease = version.Contains('-', StringComparison.Ordinal);
        string name = prerelease ? "develop" : "win";
        string suffix = prerelease ? "-develop" : string.Empty;
        return
        [
            "Context." + version + ".nupkg",
            "Context-" + version + "-osx-arm64.zip",
            "Context-" + version + "-osx-x64.zip",
            "ContextDesktop-" + name + "-Setup.exe",
            "ContextDesktop-" + version + suffix + "-full.nupkg",
            "releases." + name + ".json",
        ];
    }

    /// <summary>What <c>gh release view</c> prints for the publish step's <c>--jq</c>: draft flag, commit, asset names.</summary>
    private static string ReleaseView(string draft, string target, IEnumerable<string> assets) =>
        string.Concat(new[] { draft, target }.Concat(assets).Select(line => line + "\n"));

    /// <summary>
    /// Runs a workflow step's script the way the runner does (<c>bash --noprofile --norc -eo pipefail</c>) in
    /// <paramref name="folder"/>, with <c>$HOME</c> there and stub <c>vpk</c> (in <c>$HOME/.dotnet/tools</c>) and
    /// <c>gh</c> commands that only log their arguments and whether <c>GH_TOKEN</c> was set (<c>gh release view</c>
    /// also prints <paramref name="view"/>, what the real one prints after its <c>--jq</c>). No network, no real tool.
    /// </summary>
    private static async Task<ScriptResult> RunStep(
        string folder, string script, string version, int downloadExit = 0, int packExit = 0, string view = "",
        int viewExit = 0, int editExit = 0)
    {
        string tools = Path.Combine(folder, ".dotnet", "tools");
        string bin = Path.Combine(folder, "stub-bin");
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(bin);
        string stub = "#!/bin/sh\n"
            + "token='no token'\n"
            + "if [ -n \"${GH_TOKEN-}\" ]; then token='token'; fi\n"
            + "printf '%s [%s] %s\\n' \"$(basename \"$0\")\" \"$token\" \"$*\" >> \"$STUB_LOG\"\n"
            + "case \"$1\" in\n"
            + "  download) exit \"$STUB_DOWNLOAD_EXIT\" ;;\n"
            + "  pack) exit \"$STUB_PACK_EXIT\" ;;\n"
            + "esac\n"
            + "case \"$2\" in\n"
            + "  view) cat \"$STUB_VIEW\"; exit \"$STUB_VIEW_EXIT\" ;;\n"
            + "  edit) exit \"$STUB_EDIT_EXIT\" ;;\n"
            + "esac\n";
        foreach (string path in new[] { Path.Combine(tools, "vpk"), Path.Combine(bin, "gh") })
        {
            File.WriteAllText(path, stub);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        string file = Path.Combine(folder, "step.sh");
        File.WriteAllText(file, script);
        string viewFile = Path.Combine(folder, "stub-view.txt");
        File.WriteAllText(viewFile, view);
        Dictionary<string, string> environment = new Dictionary<string, string>
        {
            ["HOME"] = folder,
            ["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["VERSION"] = version,
            ["TAG"] = "v" + version,
            ["TARGET"] = ReleaseTarget,
            ["GH_TOKEN"] = "stub-token",
            ["GITHUB_REPOSITORY"] = "owner/repo",
            ["STUB_LOG"] = Path.Combine(folder, "stub.log"),
            ["STUB_DOWNLOAD_EXIT"] = downloadExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["STUB_PACK_EXIT"] = packExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["STUB_VIEW"] = viewFile,
            ["STUB_VIEW_EXIT"] = viewExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["STUB_EDIT_EXIT"] = editExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return await RunBash(folder, ["--noprofile", "--norc", "-eo", "pipefail", file], environment);
    }

    /// <summary>Runs <c>bash</c> with the arguments in <paramref name="folder"/>; reports what is in the folder after.</summary>
    private static async Task<ScriptResult> RunBash(
        string folder, IEnumerable<string> arguments, IDictionary<string, string>? environment = null)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using Process process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await error;
        await process.WaitForExitAsync();
        string[] files = [.. Directory.GetFileSystemEntries(folder).Select(path => Path.GetFileName(path) ?? "")];
        return new ScriptResult(process.ExitCode, stdout, stderr, files);
    }

    /// <summary>Runs only the <c>ValidateVersion</c> target of <c>Context.csproj</c> (no build).</summary>
    private static async Task<(int Code, string Output)> ValidateVersion(string version)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(Path.Combine("src", "Context", "Context.csproj"));
        startInfo.ArgumentList.Add("-t:ValidateVersion");
        startInfo.ArgumentList.Add("-p:Version=" + version);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-nodeReuse:false");

        using Process process = Process.Start(startInfo)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = await process.StandardOutput.ReadToEndAsync() + await error;
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    private static string ReadWorkflow() =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml")).ReplaceLineEndings("\n");

    private static string ScriptSource(string script) =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "scripts", script)).ReplaceLineEndings("\n");

    /// <summary>The script without its comment lines.</summary>
    private static string CodeLines(string source) =>
        string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static string VersionScript() => Scalar(Step(ReadWorkflow(), "Version")["run"]);

    private static string SmokeScript() => Scalar(Step(ReadWorkflow(), "Smoke check the packed tool")["run"]);

    /// <summary>
    /// The index of the first line at or after <paramref name="start"/> whose trimmed text is
    /// <paramref name="text"/>, or -1.
    /// </summary>
    private static int LineIndex(string script, string text, int start)
    {
        if (start < 0)
        {
            return -1;
        }

        Match match = Regex.Match(script[start..], @"(?m)^[ \t]*" + Regex.Escape(text) + @"[ \t]*$");
        return match.Success ? start + match.Index : -1;
    }

    private static string ReplaceOnce(string text, string old, string replacement)
    {
        Assert.Single(Regex.Matches(text, Regex.Escape(old)));
        return text.Replace(old, replacement, StringComparison.Ordinal);
    }

    /// <summary>Replaces only the occurrence with the 0-based <paramref name="index"/>; it must exist.</summary>
    private static string ReplaceNth(string text, string old, string replacement, int index)
    {
        MatchCollection matches = Regex.Matches(text, Regex.Escape(old));
        Assert.True(matches.Count > index, "'" + old + "' occurs " + matches.Count + " time(s)");
        int at = matches[index].Index;
        return text[..at] + replacement + text[(at + old.Length)..];
    }

    private static YamlMapping Map(YamlNode node) => Assert.IsType<YamlMapping>(node);

    private static string Scalar(YamlNode node) => Assert.IsType<YamlScalar>(node).Value;

    private static List<string> Strings(YamlNode node) =>
        [.. Assert.IsType<YamlSequence>(node).Items.Select(Scalar)];

    private static List<string> Sorted(IEnumerable<string> keys) => [.. keys.Order(StringComparer.Ordinal)];

    /// <summary>A mapping of scalars as sorted <c>key=value</c> strings, so key order does not matter.</summary>
    private static List<string> Pairs(YamlNode node) =>
        Sorted(Map(node).Entries.Select(entry => entry.Key + "=" + Scalar(entry.Value)));

    private static List<YamlMapping> Steps(string yaml) => Steps(WorkflowYaml.Parse(yaml));

    private static List<YamlMapping> Steps(YamlNode root) => JobSteps(root, "release");

    private static List<YamlMapping> JobSteps(YamlNode root, string job)
    {
        YamlNode steps = Map(Map(Map(root)["jobs"])[job])["steps"];
        return [.. Assert.IsType<YamlSequence>(steps).Items.Select(Map)];
    }

    /// <summary>The steps of every job, in file order.</summary>
    private static List<YamlMapping> AllSteps(string yaml)
    {
        YamlNode root = WorkflowYaml.Parse(yaml);
        return [.. Map(Map(root)["jobs"]).Keys.SelectMany(job => JobSteps(root, job))];
    }

    private static List<string> AllRunScripts(string yaml) =>
        [.. AllSteps(yaml).Where(step => step.ContainsKey("run")).Select(step => Scalar(step["run"]))];

    private static List<string> JobRunScripts(string yaml, string job) =>
        [.. JobSteps(WorkflowYaml.Parse(yaml), job)
            .Where(step => step.ContainsKey("run"))
            .Select(step => Scalar(step["run"]))];

    private static List<string> RunScriptsOf(YamlNode root, string job) =>
        [.. JobSteps(root, job).Where(step => step.ContainsKey("run")).Select(step => Scalar(step["run"]))];

    private static string JobScript(string job, string step) =>
        Scalar(Assert.Single(JobSteps(WorkflowYaml.Parse(ReadWorkflow()), job), s => Scalar(s["name"]) == step)["run"]);

    private static YamlMapping Step(string yaml, string name) =>
        Assert.Single(Steps(yaml), step => Scalar(step["name"]) == name);

    private static List<string> RunScripts(string yaml) =>
        [.. Steps(yaml).Where(step => step.ContainsKey("run")).Select(step => Scalar(step["run"]))];

    private static string SingleProperty(XElement project, string name) =>
        Assert.Single(project.Descendants(name)).Value;

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("No Context.slnx found above " + AppContext.BaseDirectory);
    }
}
