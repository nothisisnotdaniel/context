# 158 Same /done usage hint in the console and the CLI

As a Context user, I want the console and the CLI to word the todo usage hint the same way, so that the two feel like one tool.

## What

Make the hint shown for `/done` with no text in the console and for `context todo done` with no text in the CLI use the same wording (adjusting only the command name), and update the goldens.

## Why

Raised by the code review of job 144: the two hints are worded slightly differently after the switch to picking todos by text.

## Notes

Proposed by job 144-console-todo-picking (`factory/jobs/144-console-todo-picking/`).
