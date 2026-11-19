# 330 Smart paste: say why a page title wasn't used

As a Context user, I want a short hint when a pasted link's title was skipped as unsafe, so that I know why the link stayed bare.

## What

When PageTitleFetcher discards a title (unsafe characters, too long after cleaning, non-HTML), show a brief, dismissable status hint near the editor (screen-reader announced, light and dark mode) instead of failing silently. No hint on timeouts or when network access is off.

## Why

UX review of job 244: a link that sometimes gets a title and sometimes doesn't looks random without feedback; the reviewer called silence acceptable for now.

## Notes

Proposed by job 244-smart-paste (review-ux 2).

Proposed by job 244-smart-paste (`factory/jobs/244-smart-paste/`).
