# 187 Match mention lowercasing between the editor and the index

As a Context user, I want the editor and the index to treat the same names as equal, so that the spelling shown for a person is the same everywhere.

## What

Make the editor de-duplicate mentions with the same invariant casing as C# ToLowerInvariant (for example with a shared fixture covering U+0130 and similar code points), or document the difference. Extend EditorBundleTests.

## Why

JS toLowerCase() and C# ToLowerInvariant() differ for a few code points, so the editor and the index can keep different spellings of one person.

## Notes

Proposed by job 156-people-pages (review-security round 1, nit 3).
