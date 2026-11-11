# 114 Owner-only file mode for all data files

As the user, I want every Context data file created readable only by me, so that other local accounts cannot read my plain notes.

## What

Since encryption is gone, file permissions are the only protection. Make todos.json, notes written by NoteStore Create/Save (import) and any other data file get owner-only mode (0600, folders 0700) on Unix when created or rewritten, like the files that already do. Add tests.

## Why

Raised by the security review of job 132: with protection at rest removed, a default umask can leave data readable by other accounts.

## Notes

Proposed by job 132-remove-folder-encryption (`factory/jobs/132-remove-folder-encryption/`).
