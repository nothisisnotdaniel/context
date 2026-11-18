# 304 Same selector for the re-found note button in ReviewPanelTests

As a maintainer of the desktop tests, I want Render_Filled_RowsAndButtons to find the note button the same way both times, so that a reader sees at once that it is the same button.

## What

In tests/Context.Desktop.Tests/ReviewPanelTests.cs, Render_Filled_RowsAndButtons first takes the button with note.QuerySelector("button.note-button") and later finds it again with cut.Find(".review-note button.note-button"). Both pick the same button. Use one selector for both, or add a short comment saying why they differ.

## Why

Code review of job 222 noted the different selectors as a nit: harmless, but a reader may wonder whether two different buttons are meant.

## Notes

Proposed by job 222. The builder left it because both selectors pick the same element.

Proposed by job 222-steady-reviewpanel-test (`factory/jobs/222-steady-reviewpanel-test/`).
