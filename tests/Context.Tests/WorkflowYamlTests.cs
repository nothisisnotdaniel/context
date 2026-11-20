namespace Context.Tests;

// AC-12: the minimal YAML reader the workflow tests use.
public sealed class WorkflowYamlTests
{
    [Fact]
    public void Parse_ReadsNestedMappingsAndSequences()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("""
            name: Release
            on:
              push:
                branches:
                  - develop
                  - main
              pull_request:
                types:
                - closed
            jobs:
              build:
                steps:
                  - name: One
                    uses: a/b@c
                  - run: echo hi
            """));

        Assert.Equal(["name", "on", "jobs"], root.Keys);
        Assert.Equal("Release", Scalar(root["name"]));
        YamlMapping on = Map(root["on"]);
        Assert.Equal(["push", "pull_request"], on.Keys);
        Assert.Equal(["develop", "main"], Strings(Map(on["push"])["branches"]));
        Assert.Equal(["closed"], Strings(Map(on["pull_request"])["types"]));

        YamlSequence steps = Assert.IsType<YamlSequence>(Map(Map(root["jobs"])["build"])["steps"]);
        Assert.Equal(2, steps.Items.Count);
        YamlMapping first = Map(steps.Items[0]);
        Assert.Equal(["name", "uses"], first.Keys);
        Assert.Equal("a/b@c", Scalar(first["uses"]));
        Assert.Equal("echo hi", Scalar(Map(steps.Items[1])["run"]));
    }

    [Fact]
    public void Parse_ReadsPlainSingleAndDoubleQuotedScalars()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("""
            plain: a b ${{ c }} == 'd'
            single: 'it''s # not a comment'
            double: "tab\there \"q\" # not a comment"
            empty:
            list:
              - "v*"
              - 'x'
              - y
            """));

        Assert.Equal("a b ${{ c }} == 'd'", Scalar(root["plain"]));
        Assert.Equal("it's # not a comment", Scalar(root["single"]));
        Assert.Equal("tab\there \"q\" # not a comment", Scalar(root["double"]));
        Assert.Equal(string.Empty, Scalar(root["empty"]));
        Assert.Equal(["v*", "x", "y"], Strings(root["list"]));
    }

    [Fact]
    public void Parse_LiteralBlockKeepsRelativeIndentation_AndCommentLinesInside()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("""
            run: |
              if true; then
                # still script
                echo "a: b"

                echo done
              fi


            next: x
            """));

        Assert.Equal("if true; then\n  # still script\n  echo \"a: b\"\n\n  echo done\nfi\n", Scalar(root["run"]));
        Assert.Equal("x", Scalar(root["next"]));
    }

    [Fact]
    public void Parse_FoldedBlockJoinsLines()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("""
            if: >
              a ==
              b

              c
            """));

        Assert.Equal("a == b\nc\n", Scalar(root["if"]));
    }

    [Fact]
    public void Parse_SkipsCommentsAndBlankLines_AndKeepsTrailingComments()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("""
            # header

            a: 1 # one
              # indented comment line

            b: 'x'   # quoted
            c:   # nested below
              - d#e # dee
            url: http://x#y
            """));

        Assert.Equal(["a", "b", "c", "url"], root.Keys);
        YamlScalar a = Assert.IsType<YamlScalar>(root["a"]);
        Assert.Equal("1", a.Value);
        Assert.Equal("one", a.Comment);
        Assert.Equal(3, a.Line);
        YamlScalar b = Assert.IsType<YamlScalar>(root["b"]);
        Assert.Equal("x", b.Value);
        Assert.Equal("quoted", b.Comment);
        YamlScalar d = Assert.IsType<YamlScalar>(Assert.Single(Assert.IsType<YamlSequence>(root["c"]).Items));
        Assert.Equal("d#e", d.Value);
        Assert.Equal("dee", d.Comment);
        Assert.Equal("http://x#y", Scalar(root["url"]));
        Assert.Null(Assert.IsType<YamlScalar>(root["url"]).Comment);
    }

    [Theory]
    [InlineData("a:\n\tb: 1\n")]
    [InlineData("a:\n  \tb: 1\n")]
    [InlineData("\ta: 1\n")]
    public void Parse_ThrowsOnTabIndent(string yaml)
    {
        Assert.Throws<FormatException>(() => WorkflowYaml.Parse(yaml));
    }

    [Theory]
    [InlineData("a: {b: 1}\n")]
    [InlineData("a: [b, c]\n")]
    [InlineData("a:\n  - [b]\n")]
    [InlineData("a:\n  - {b: 1}\n")]
    public void Parse_ThrowsOnFlowCollection(string yaml)
    {
        Assert.Throws<FormatException>(() => WorkflowYaml.Parse(yaml));
    }

    [Theory]
    [InlineData("a: &x 1\n")]
    [InlineData("a: &x\n  b: 1\n")]
    [InlineData("a:\n  - &x b\n")]
    public void Parse_ThrowsOnAnchor(string yaml)
    {
        Assert.Throws<FormatException>(() => WorkflowYaml.Parse(yaml));
    }

    [Theory]
    [InlineData("a: 1\nb: *x\n")]
    [InlineData("a: !!str 1\n")]
    [InlineData("a: |-\n  x\n")]
    [InlineData("---\na: 1\n")]
    [InlineData("a: b: c\n")]
    [InlineData("a: 'open\n")]
    [InlineData("a: \"open\n  more\"\n")]
    [InlineData("a: one\n  two\n")]
    [InlineData("a: 1\n b: 2\n")]
    [InlineData("? a\n: 1\n")]
    [InlineData("")]
    public void Parse_ThrowsOnAliasTagAndOtherUnsupportedSyntax(string yaml)
    {
        Assert.Throws<FormatException>(() => WorkflowYaml.Parse(yaml));
    }

    [Theory]
    [InlineData("a: 1\na: 2\n")]
    [InlineData("a:\n  b: 1\n  c: 2\n  b: 3\n")]
    [InlineData("s:\n  - name: x\n    name: y\n")]
    public void Parse_ThrowsOnDuplicateKey(string yaml)
    {
        FormatException error = Assert.Throws<FormatException>(() => WorkflowYaml.Parse(yaml));

        Assert.Contains("duplicate key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_MissingKeyThrows()
    {
        YamlMapping root = Map(WorkflowYaml.Parse("a: 1\n"));

        Assert.False(root.ContainsKey("b"));
        Assert.Throws<KeyNotFoundException>(() => root["b"]);
    }

    private static YamlMapping Map(YamlNode node) => Assert.IsType<YamlMapping>(node);

    private static string Scalar(YamlNode node) => Assert.IsType<YamlScalar>(node).Value;

    private static List<string> Strings(YamlNode node)
    {
        return Assert.IsType<YamlSequence>(node).Items.Select(Scalar).ToList();
    }
}
