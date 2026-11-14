# 184 Skip ~~~ code fences when reading tags and mentions

As a Context user, I want text inside ~~~ code blocks ignored, so that code examples never create tags or person mentions.

## What

Make the shared TagParser (used for tags and @[person] mentions) skip ~~~ fenced blocks the way it already skips ``` fences. Bump the index ParserVersion and add tests for tags and mentions.

## Why

Markdown allows ~~~ fences, and today a #tag or @[Name] inside one is indexed as if it were real.

## Notes

Proposed by job 156-people-pages (review-code round 1, nit 3).
