# 005 Sensitive notes with certificate unlock

As a Context user, I want to mark single notes as sensitive and have them encrypted with a certificate in my OS store, so that private notes are protected without a passphrase for everything.

## What

In the desktop app, mark a note as sensitive; only those notes are encrypted (AES-256-GCM with a random note key, file format and AAD with the note name as before). The note key is wrapped with the public key of a certificate in the OS store: the macOS login Keychain or the Windows certificate store (CurrentUser\My), with a non-exportable private key (TPM-backed / Windows Hello on Windows where available). Unlocking asks the OS for the private-key operation, so the OS prompts for the keychain password, Touch ID or Windows Hello: the second factor is this machine's protected key plus that prompt. No YubiKey or hardware token. Context creates the certificate on first use (or the user picks one) and stores only its thumbprint and the wrapped keys; recovery is a second certificate exported once as a password-protected `.pfx` that the user keeps offline (every note key is also wrapped for it), and another machine is added by wrapping the keys for its certificate from an unlocked machine. Sensitive notes stay out of `context.db`, search and the tag and link index (only their file names show); the CLI and console list them as locked and never decrypt them; unlocked text lives only in memory and is dropped on lock, after an idle time and when the app closes. Built-in .NET only (`X509Store`, `X509Certificate2`, RSA-OAEP or ECDH), so no new package unless the spec justifies one. Full path, high risk.

## Why

Most notes don't need encryption, but some (HR, personal matters) do. Wrapping each note key with a non-exportable certificate key in the Keychain or Windows certificate store gives strong protection with no password to type or forget.
