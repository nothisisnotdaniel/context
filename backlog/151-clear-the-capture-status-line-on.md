# 151 Clear the capture status line on new input

As a Context user, I want the capture box status line to clear when I start typing again or do something else, so that an old Captured. or refusal is not mistaken for the latest result.

## What

In CaptureBox.razor, clear the status line (Captured. or the red refusal) when the text changes or another action runs, with tests.

## Why

Review-ux (job 140): the status line keeps Captured. or a red refusal until the next capture, even after other actions.

## Notes

Proposed by job 140-desktop-quick-capture (`factory/jobs/140-desktop-quick-capture/`).
