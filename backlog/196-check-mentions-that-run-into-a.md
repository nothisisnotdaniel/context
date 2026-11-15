# 196 Check @[...] mentions that run into a code span

As a Context user, I want a person mention whose text runs into a code span to be treated the same way as a link, so that code examples never create person mentions.

## What

Check whether PersonParser reads @[Name] whose text runs into an inline code span (for example @[a `b]) as a mention. If so, skip it the way NoteLinkParser does since job 163, bump the index ParserVersion and add tests.

## Why

Job 163 fixed this for [[...]] links. Mentions use similar bracket parsing and may have the same flaw, which would index code text as a person.

## Notes

Proposed by job 163-tag-link-parser-fixes (spec-writer note).

Proposed by job 163-tag-link-parser-fixes (`factory/jobs/163-tag-link-parser-fixes/`).
