# 530 Check long note names in person-page note todos by eye

As a user, I want a long note name next to long todo text on a person page to be cut cleanly, so that the row stays one tidy line.

Category: ui

## What

Open a person page in the running app with a note todo whose note name and text are both long, and check that both are cut with an ellipsis like in the todo panel; fix the CSS if not.

## Why

review-ui 1 on job 487: the reviewer did not render the new row; the shared .cut rules should apply but were not seen.

## Notes

Proposed by job 487-person-note-todos.67b86604ce9643509d12bed49684c969 (`factory/jobs/487-person-note-todos.67b86604ce9643509d12bed49684c969/`).
