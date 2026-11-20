# 160 Replace the console text-parity rule in CLAUDE.md

As a maintainer, I want CLAUDE.md to say which texts the CLI and desktop app must share, so that builders keep wording consistent now the console is gone.

## What

Job 145 removed the rule "Texts match the console renderer's" from Context/CLAUDE.md without a replacement. Check how Cli and the desktop app share texts today and write a rule that matches.

## Why

Raised by the code review of job 145. Without a rule, CLI and desktop wording can drift apart.

## Notes

Proposed by job 145-remove-interactive-console (`factory/jobs/145-remove-interactive-console/`).
