namespace Context.Tests;

// 353: NoteTitleLine.Rewrite rewrites a first-line "# <old name>" title to the new name, by the editor's titleSplit
// rule; pure strings, no files.
public sealed class NoteTitleLineTests
{
    // AC-1: the line is rewritten; spaces around the old name go, the CRLF stays, a leading BOM is skipped and kept.
    [Theory]
    [InlineData("# Plan\n\nbody\n", "# Project\n\nbody\n")]
    [InlineData("#  plan  \r\nx", "# Project\r\nx")]
    [InlineData("﻿# Plan", "﻿# Project")]
    [InlineData("# PLAN", "# Project")]
    [InlineData("# Plan\n# Plan\n", "# Project\n# Plan\n")]
    public void Rewrite_MatchingTitle_BecomesTheNewName(string text, string expected)
    {
        Assert.Equal(expected, NoteTitleLine.Rewrite(text, "Plan", "Project"));
    }

    // AC-2: anything but a matching "# " first line, and a case-only rename, gives the same instance.
    [Theory]
    [InlineData("## Plan\n", "Project")]
    [InlineData(" # Plan\n", "Project")]
    [InlineData("#Plan\n", "Project")]
    [InlineData("# Plan notes\n", "Project")]
    [InlineData("# \nbody\n", "Project")]
    [InlineData("#   \r\nbody\n", "Project")]
    [InlineData("", "Project")]
    [InlineData("x\n# Plan\n", "Project")]
    [InlineData("# Plan\n", "plan")]
    [InlineData("# Plan\n", "PLAN")]
    public void Rewrite_NoMatchOrCaseOnly_SameInstance(string text, string newName)
    {
        string copy = new string(text.AsSpan());

        Assert.Same(copy, NoteTitleLine.Rewrite(copy, "Plan", newName));
    }

    // AC-2: a title already spelled as the new name is left as it is.
    [Fact]
    public void Rewrite_AlreadyTheNewName_SameInstance()
    {
        string text = new string("# Project\n".AsSpan());

        Assert.Same(text, NoteTitleLine.Rewrite(text, "Project", "Project"));
    }

    // AC-3: for a subpage the last segment becomes the new last segment, the full name the new full name.
    [Theory]
    [InlineData("# Meeting 1\n", "# Kickoff\n")]
    [InlineData("# meetings/meeting 1\n", "# Meetings/Kickoff\n")]
    [InlineData("# Meetings\n", "# Meetings\n")]
    public void Rewrite_Subpage_SegmentOrFullName(string text, string expected)
    {
        Assert.Equal(expected, NoteTitleLine.Rewrite(text, "Meetings/Meeting 1", "Meetings/Kickoff"));
    }
}
