# 484 Word diff in note history

As someone comparing two versions of a note, I want the changed words marked inside a line, so that a small edit in a long paragraph is easy to spot.

Category: ux

## What

The History panel's diff marks the changed words inside each changed line pair (removed red, added accent, also marked by more than colour), on top of today's line diff, with a bound like `MaxChangedLines` after which it falls back to the line diff.

## Why

Context's line diff shows a whole paragraph as changed for one word. assistant has a word diff.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
