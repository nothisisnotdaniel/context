# 455 Strip leading Unicode dashes and non-breaking spaces from Times booking names

As a user booking time from Context, I want a booking name that starts with a Unicode dash or a non-breaking space cleaned like an ASCII dash, so that Times can never read it as an option.

Category: security

## What

Extend TimesBooking.CleanName (src/Context.Core/TimesBooking.cs) to also drop leading U+2013, U+2014 and other dash punctuation plus non-breaking and other Unicode spaces, with tests for each.

## Why

CleanName strips only ASCII #, - and space; it is safe only while Times reads nothing but an ASCII - as an option.

## Notes

Proposed by job 383-times-desktop-panel (`factory/jobs/383-times-desktop-panel/`).
