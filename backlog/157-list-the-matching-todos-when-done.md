# 157 List the matching todos when /done is ambiguous

As a Context console user, I want an ambiguous /done to list the todos that match, so that I can see what to type to pick the one I mean.

## What

When `/done <text>` (console) or `context todo done <text>` (CLI) matches more than one todo, print the matching todos (one line each, as the sidebar shows them) under the "N todos match" line. Still remove nothing.

## Why

Raised by the ux review of job 144: "N todos match" never lists the matches, so the user has to look at the sidebar or run `context todos` to work out a unique text.

## Notes

Proposed by job 144-console-todo-picking (`factory/jobs/144-console-todo-picking/`).
