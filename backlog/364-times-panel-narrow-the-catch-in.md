# 364 Times panel: narrow the catch in DesktopState.TimesNow

As the Context developer, I want DesktopState.TimesNow to catch only the errors a Times read can really throw, so that a programming error in the Times panel shows up instead of being hidden as an unreadable file.

## What

DesktopState.TimesNow (src/Context.Desktop/DesktopState.cs) wraps TimesNow.Read in a broad catch (Exception) and maps every failure to Unreadable, with no log line. TimesNow.Read already never throws for file problems, so narrow the catch (for example IOException and UnauthorizedAccessException), or log what is caught. Add a test that a non-IO exception is not silently turned into the grey line.

## Why

Security review of job 277 (nit 2): the broad catch fits 'never throws' but hides bugs. The builder left it because narrowing it changes behaviour.

## Notes

Proposed by job 277-times-bookings-current (review-security 2).

Proposed by job 277-times-bookings-current (`factory/jobs/277-times-bookings-current/`).
