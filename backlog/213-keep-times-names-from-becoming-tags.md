# 213 Keep Times names from becoming tags or links in generated notes

As a Context user, I want checkpoint names copied from Times into my briefing and day end notes to stay plain text, so that a name like "#x" or a wiki-link doesn't add a tag or link to my index.

## What

In Briefing and DayEnd, escape or neutralise tag and link syntax (#tag, [[link]], and any other syntax the index parses) in Times checkpoint names and todo texts before writing them into a stored note. Keep the text readable. Check that search and tag lists no longer pick them up, and add tests.

## Why

Names are cleaned to one line but otherwise written verbatim, so Times data can create real tags and links in Context's index.

## Notes

Proposed by job 172-morning-briefing (review-security round 1, nit 2).

Proposed by job 172-morning-briefing (`factory/jobs/172-morning-briefing/`).
