# 312 Ask before a link with # creates a note

As a desktop user, I want to be asked before clicking [[C# notes]] creates a note named 'C', so that a # in a note name doesn't silently make the wrong page.

## What

Clicking a link to a missing note whose text contains '#' (for example [[C# notes]]) now creates the note named after the part before '#' ('C'), as the Links panel shows. Add a confirm step (or another clear choice) for this case, naming the note that will be created, so the user can cancel. Links without '#' and [[Note#Heading]] to an existing note are unchanged.

## Why

Job 233 UX review: a missing [[C# notes]] creates the note 'C'; a confirm step would be safer.

## Notes

Changes click behaviour from job 233 (#296).

Proposed by job 233-link-creates-missing-note (`factory/jobs/233-link-creates-missing-note/`).
