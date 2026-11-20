# 313 Slim CLAUDE.md into docs

As the user, I want Context's CLAUDE.md to hold only the rules every agent needs, so that factory jobs read less, cost less and are less likely to miss a rule buried in feature detail.

Category: docs

## What

CLAUDE.md is about 162 KB (about 40k tokens), changed in 141 commits, with a 623-line Layout section and per-job history. Rewrite it to about 150-250 lines: what the app is, how it runs, safety and privacy rules, stack, test command, style, git, the cross-cutting invariants and an index of the docs below with when to read each. Move feature detail into `docs/<topic>.md` (for example layout, storage, tags, views, sensitive-notes, note-history, cli, release), keeping each rule's wording. Drop job-number history that git already holds. Write a `docs/claude-md-migration.md` checklist that maps every rule and fact of the old file to its new place, so a reviewer can confirm nothing was lost, only moved. No code changes. README links stay valid.

## Why

Every agent of every Context job reads the whole file, and the builder carries it through every turn: Context builders use a median 2.2M tokens against 0.7M in Times, whose CLAUDE.md is 301 lines. Important rules are also hard to find among single-feature details.
