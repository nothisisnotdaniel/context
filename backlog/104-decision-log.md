# 104 Decision log

As a developer, I want to record decisions in my notes and list them later, so that I can tell my team why something was done the way it was.

## What

A `#decision` tag plus a short template (context, decision, consequences) that `context decision new` creates. `context decisions [--item <number>] [--since <date>]` lists them, and `--export <file>` writes a Markdown decision register (escaped, no scripts) that can be shared with the team. Full path.

## Why

Why something was decided is the knowledge that gets lost first. A light decision register is useful for every developer, and the export makes it worth sharing with a team.
