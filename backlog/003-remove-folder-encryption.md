# 003 Remove folder encryption

As the user, I want the whole-folder encryption removed, so that my notes are plain files and the code is simpler.

## What

From now on Context data is plain. Remove the optional whole-folder encryption (`Vault`, `Encryption`, `encryption.json`, `/encrypt`, `/decrypt`, the start-up passphrase prompt and `LockedPanel`, the CLI's `--passphrase-stdin` and `CONTEXT_PASSPHRASE`, the in-memory tag index and every optional `Encryption` constructor argument) and its tests. No migration: there are no encrypted folders or files in use, so nothing is decrypted or converted; `encryption.json` and encrypted files are simply no longer recognised. Update CLAUDE.md. Full path, high risk.

## Why

I decided that Context data is plain from now on, with only sensitive notes encrypted on the desktop. Whole-folder encryption adds a passphrase prompt, a locked panel, CLI options and extra code paths everywhere, and nothing uses it, so no migration is needed.
