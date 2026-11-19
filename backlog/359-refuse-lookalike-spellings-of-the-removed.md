# 359 Refuse lookalike spellings of the removed secret options

As the user, I want the CLI to also refuse the old passphrase options when they are typed with lookalike characters, so that a pasted or autocorrected secret option never lands in my notes as text.

## What

Cli.cs (src/Context/Cli.cs, around line 142) compares the removed option names with OrdinalIgnoreCase, which folds simple case only. Normalise the argument first (for example NFKC plus mapping Unicode dashes such as U+2010–U+2015 and U+2212 to '-') before the check, and add tests with an en dash and fullwidth letters.

## Why

Security review of job 273 (nit 1): a word processor or chat app can turn -- into an en dash, so the option slips through as text; it is outside job 273's spec, which targets the literal old option.

## Notes

Proposed by job 273-refuse-removed-secret-options (review-security 1).

Proposed by job 273-refuse-removed-secret-options (`factory/jobs/273-refuse-removed-secret-options/`).
