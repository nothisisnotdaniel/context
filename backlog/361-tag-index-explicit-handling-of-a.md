# 361 Tag index: explicit handling of a symlinked context.db

As the user, I want Context to handle a context.db that is a symlink in a clear, deliberate way, so that tightening file modes never silently changes a file somewhere else or fails with an unclear error.

Category: security

## What

TagIndex.PreparePrivateFile (src/Context.Core/TagIndex.cs) calls File.Exists, PrivateFile.OpenNew and Tighten, which all follow a symlinked context.db: the link's target is chmodded to 0600, and a dangling link becomes a TagIndexException. Add a LinkTarget check and decide the behaviour explicitly (for example refuse with a clear message, or leave the link's target alone), with tests for a live and a dangling link.

## Why

Security review of job 274 (nit 2): only someone who can already write the user's data folder can set this up, so there is no new exposure, but the current behaviour is accidental and the dangling-link error is unclear.

## Notes

Proposed by job 274-owner-only-file-mode (review-security 2).

Proposed by job 274-owner-only-file-mode (`factory/jobs/274-owner-only-file-mode/`).
