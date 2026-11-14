# 165 Show --json as an option in the CLI help

## What

The help Commands table lists `<command> --json` as if it were a command. Move it under an Options heading (or reword it) and update the help-text test.

## Why

Review-ux round 1 of job 148: a flag listed as a command reads oddly in context help.

## Notes

Proposed by job 148-cli-json-output (`factory/jobs/148-cli-json-output/`).
