# 113 Refuse removed secret options after a command

As the user, I want the CLI to refuse the old passphrase options anywhere on the line, so that a mistyped secret never ends up as text in my notes.

## What

Since job 132 removed the argument pre-scan, `--passphrase-stdin` and similar removed options are refused only in command position. After a valid command they are ordinary text, so `context capture --passphrase=x` appends `--passphrase=x` to inbox.md. Refuse these removed option names in any position with the usual usage error, and add a test.

## Why

Raised by the code and security reviews of job 132: a user with an old script or habit could write a secret into plain notes without noticing.

## Notes

Proposed by job 132-remove-folder-encryption (`factory/jobs/132-remove-folder-encryption/`).
