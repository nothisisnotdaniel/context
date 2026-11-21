# 436 Don't follow a SuitePro symlink when tightening the activation folder

As a user, I want Context to refuse a symlinked SuitePro folder, so that a planted link can't make it change the mode of another folder.

Category: security

## What

Before SetUnixFileMode on <ApplicationData>/SuitePro (and activation.json), check that the entry is not a link or reparse point and refuse the write if it is. Times does the same today, so change both apps together or file the matching Times item.

## Why

Raised by the security review of job 367: SetUnixFileMode follows a symlink, so someone who can write the app data folder could redirect the chmod. It matches Times' current behaviour, so it was left.

## Notes

Proposed by job 367-pro-licence-context.

Proposed by job 367-pro-licence-context (`factory/jobs/367-pro-licence-context/`).
