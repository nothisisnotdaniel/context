# 327 Person page: mark overdue waiting-for follow-ups

As a Context user, I want overdue waiting-for items on a person's page to stand out, so that I see at a glance whom I should chase now.

Category: feature

## What

The person page's Waiting for rows (added in job 243) show the follow-up date but no overdue styling. Mark rows whose follow-up date is before today the same way the todo panel marks overdue todos (same class or theme variables, not colour alone), in light and dark mode, and announce it to screen readers.

## Why

UX review of job 243: the sidebar and briefing flag due follow-ups, the person page doesn't, so an overdue item looks the same as one due next week.

## Notes

Proposed by job 243-waiting-for-list (review-ux 1).

Proposed by job 243-waiting-for-list (`factory/jobs/243-waiting-for-list/`).
