# 532 Test control characters in note names on the person page

As a maintainer, I want note names with control characters to be tested on the person page, so that rows always show one line.

Category: test

## What

Exercise AC-10 with a note name holding control characters (through an index row, not a real file) and check the todo-note button shows it on one line via DesktopText.OneLine.

## Why

review-code 2 on job 487: only the todo text was tested, since such a file name cannot exist on every OS.

## Notes

Proposed by job 487-person-note-todos.67b86604ce9643509d12bed49684c969 (`factory/jobs/487-person-note-todos.67b86604ce9643509d12bed49684c969/`).
