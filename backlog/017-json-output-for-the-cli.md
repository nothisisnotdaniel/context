# 017 JSON output for the CLI

As a Context user, I want `--json` on every read command, so that scripts and PowerShell can use Context's data directly.

## What

`--json` on every read command prints one JSON document (System.Text.Json, stable camelCase property names) so PowerShell can use `ConvertFrom-Json`; errors stay on stderr with the exit code. Add PowerShell examples to the README. Builds on the scriptable CLI.

## Why

Plain-text output is for people; scripts need stable JSON. Times' calendar-to-checkpoints feature and other tools build on it.
