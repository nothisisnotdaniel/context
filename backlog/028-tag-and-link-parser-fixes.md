# 028 Tag and link parser fixes

As a Context user, I want the tag and link parsers to stay fast and correct on odd input, so that large or unusual notes can't slow Context down or produce wrong links.

## What

Make `TagParser.SkipCodeSpan` linear (it rescans the rest of the line for every unmatched backtick run, so many unmatched backticks take O(n²) time), before email or calendar text gets indexed. Respect code spans inside `[[...]]` in `NoteLinkParser.ParseLine` (its cached next `]` and `[` can point into a later backtick span, so a link target can run into code), keeping the scan linear. Keep one whitespace-collapsing loop instead of two (`NormalizeBracket` and `CollapseWhitespace`). Bump `TagIndex.ParserVersion` if results change.

## Why

Many unmatched backticks make tag parsing quadratic, and a link target can run into a code span. This matters more once mail and calendar text get indexed.
