# 457 Clear a stale Times result line when the booking form is cancelled

As a user, I want an old red booking error to go away when I cancel the Times booking form, so that I don't think the last action failed.

Category: ux

## What

In TimesTodayPanel, clear the result line when the booking form is cancelled (button or Esc), with a bUnit test.

## Why

The result line stays until the next action, so a stale error stays visible after cancel.

## Notes

Proposed by job 383-times-desktop-panel (`factory/jobs/383-times-desktop-panel/`).
