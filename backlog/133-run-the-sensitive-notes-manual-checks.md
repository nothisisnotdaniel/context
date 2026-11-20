# 133 Run the sensitive-notes manual checks on macOS and Windows

As the maintainer, I want the seven manual checks of the sensitive-notes feature run on real macOS and Windows machines, so that I know the key stores work before a release.

## What

Follow the seven manual checks in factory/jobs/136-sensitive-notes-cert/build.md (setup, mark, open, lock, restart, restore from the .pfx, console) with a throw-away note, and confirm the private key cannot be exported (Keychain Access on macOS, certutil -user -store My on Windows).

## Why

MacKeyStore and WindowsKeyStore never ran in job 136; a bug can lock users out of their notes or leave the key exportable.
