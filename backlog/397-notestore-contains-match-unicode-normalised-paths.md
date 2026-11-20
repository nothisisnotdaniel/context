# 397 NoteStore.Contains: match Unicode-normalised paths

As the user, I want Context to treat two Unicode spellings of the same folder name as the same path, so that a data folder with accented names is never wrongly counted as outside (or inside) the root.

Category: bug

## What

NoteStore.Contains (src/Context.Core/NoteStore.cs) compares the root and the candidate path without Unicode normalisation. On APFS, which ignores normalisation, an NFD spelling of a non-ASCII segment (for example Café as e + combining accent) names the same folder as the NFC spelling but does not match. Normalise both paths (for example to NFC) before comparing where the file system ignores normalisation, keep the case rule job 298 added, and add tests for an NFC root with an NFD candidate and the reverse. Check that the inside guards in NoteAttachments and NoteHistory and the outside guard for export and plain copy still behave as their comments say.

## Why

Security review of job 298 (nit 3): this was already the case before job 298 and is out of its scope; a mismatch can refuse a valid path or, for the outside guard, accept one that should be refused.

## Notes

Proposed by job 298-note-store-cleanup (review-security 3).

Proposed by job 298-note-store-cleanup (`factory/jobs/298-note-store-cleanup/`).
