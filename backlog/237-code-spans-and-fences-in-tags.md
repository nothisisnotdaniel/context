# 237 Code spans and fences in tags, links and mentions

As a Context user, I want tags, links and mentions inside code to be ignored the same way in the editor and the index, so that what looks linked really is linked.

Category: bug

## What

- Skip ~~~ code fences when reading tags and mentions (was #184): Make the shared TagParser (used for tags and @[person] mentions) skip ~~~ fenced blocks the way it already skips ``` fences. Bump the index ParserVersion and add tests for tags and mentions.
- Match mention lowercasing between the editor and the index (was #187): Make the editor de-duplicate mentions with the same invariant casing as C# ToLowerInvariant (for example with a shared fixture covering U+0130 and similar code points), or document the difference. Extend EditorBundleTests.
- Editor: don't show a link that runs into a code span (was #195): Make the editor's noteLinkRule in src/Context.Desktop/editor/src/markdown.js skip a [[...]] whose text runs into an inline code span (for example [[a `b]]), the same way NoteLinkParser does since job 163. Extend EditorBundleTests with that case.
- Check @[...] mentions that run into a code span (was #196): Check whether PersonParser reads @[Name] whose text runs into an inline code span (for example @[a `b]) as a mention. If so, skip it the way NoteLinkParser does since job 163, bump the index ParserVersion and add tests.

## Why

- #184: Markdown allows ~~~ fences, and today a #tag or @[Name] inside one is indexed as if it were real.
- #187: JS toLowerCase() and C# ToLowerInvariant() differ for a few code points, so the editor and the index can keep different spellings of one person.
- #195: Since job 163 the index no longer counts [[a `b]] as a link, but the editor still highlights it as one, so a note looks linked when it isn't.
- #196: Job 163 fixed this for [[...]] links. Mentions use similar bracket parsing and may have the same flaw, which would index code text as a person.

## Notes

- Merges #184, #187, #195, #196 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode. Bump ParserVersion once for all parser changes.
- #184: Proposed by job 156-people-pages (review-code round 1, nit 3).
- #187: Proposed by job 156-people-pages (review-security round 1, nit 3).
- #195: Proposed by job 163-tag-link-parser-fixes (review-code round 1, nit 2). Proposed by job 163-tag-link-parser-fixes (`factory/jobs/163-tag-link-parser-fixes/`).
- #196: Proposed by job 163-tag-link-parser-fixes (spec-writer note). Proposed by job 163-tag-link-parser-fixes (`factory/jobs/163-tag-link-parser-fixes/`).
