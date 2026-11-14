# 182 Refuse invisible characters in person names and note titles

As a Context user, I want names with zero-width or text-direction characters refused, so that two pages that look identical can never both exist.

## What

Make PersonParser.Normalize and NoteStore.ValidateTitle refuse Unicode Format characters (UnicodeCategory.Format, for example U+200B and U+202E), not only control characters. Check that OneLine and SafeText.Clean strip them wherever titles are shown. Bump the index ParserVersion if mention parsing changes.

## Why

Today @[Anna​Schmidt] creates a second person page that looks the same as Anna Schmidt, and U+202E can reorder a title on screen. That is confusing and can be used to disguise a page.

## Notes

Proposed by job 156-people-pages (review-security round 1, nit 1).
