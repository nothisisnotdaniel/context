# 266 Tag filters: work item numbers like #1234

As a Context user, I want a filter like `#1234 and not #done` to find the notes about work item 1234 that aren't tagged done, so that I can filter by ticket numbers the way the saved searches story shows it.

Category: feature

## What

- In a tag filter (`TagFilter.TryParse`), a term may be a work item reference `#<n>`, written as in notes (`WorkItemParser` rules: 1 to 9 ASCII digits, the first 1-9). It matches the notes that reference that item (`note_items`, as `TagIndex.NotesWithItem(n)`), and `not #<n>` excludes them. It can be combined with tag terms through `and`.
- Tags keep their rule: a tag starts with a letter. `#1234` stays a work item reference and never becomes a tag, so nothing changes in notes and no migration is needed.
- `#1234 and not #done`, `#done and #1234` and `not #1234 and #work` work. A filter with only `not` terms still fails. A malformed number such as `#0123` or a 10-digit `#1234567890` shows the red usage line, as an invalid tag does today.
- `TagFilter.IsTagFilter` still recognises a filter that starts with `#1234`.
- The usage line and help text say that a term is a tag or a work item number. Saved searches (job 207) and anything else built on tag filters take the new term without changes of their own. Tests cover work item terms, their mix with tag terms, and the refusals.

## Why

Raised by the ux and code reviews of job 207: the saved searches story uses `#1234 and not #done` as its example, but it shows the red usage line, because a tag filter accepts only tags and tags must start with a letter. Since job 280, `#1234` is already the work item syntax, and the user wants work items to look different from tags. So the filter should read `#1234` as a work item, not turn numbers into tags.

## Notes

Proposed by job 207-saved-searches (`factory/jobs/207-saved-searches/`).

- 2026-11-20: The user suggested `#?1234` for work items, so they look different from tags. They already do: tags start with a letter, and `#1234` has been the work item reference since job 280. The user chose to keep that syntax and extend tag filters instead.
- Code: `TagFilter.cs` and `TagFilterView.cs` (job 207), `WorkItemParser.cs` and `note_items` (job 280).
