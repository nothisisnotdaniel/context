# 464 Run the sensitive-notes manual checks on macOS

As the developer, I want to run the sensitive-notes checks by hand on macOS, so that the real key store is known to work.

Category: test

## What

Run the checklist below on macOS with a throw-away note and a throw-away recovery password, and tick each step.

## Checklist

- [ ] Setup: Notes > Set up sensitive notes, a password of 12+ characters twice, save the `.pfx` outside the data folder. The file exists.
- [ ] Mark: More actions > Mark sensitive on the note and confirm. The `.md` is gone, `<name>.ctxs` exists, the list shows `locked`.
- [ ] Open: open the note. macOS asks (Touch ID or password) once and the text shows.
- [ ] Lock: press Lock. The editor closes and reopening asks again. Leave it 5 minutes idle: it locks by itself.
- [ ] Restart: quit and start. The note is still listed as locked and opens after the prompt.
- [ ] Restore: on another user account (or after deleting the key), Restore from recovery file with the `.pfx` and password. It says `Restored 1 of 1 notes.` and the note opens.
- [ ] Console: `context notes` lists `<name>` as `locked`; `context note show <name>` answers `<name> is sensitive. Open it in the desktop app.`
- [ ] Keychain Access: the private key can't be exported. Note whether it is in the Secure Enclave or the login Keychain.
- [ ] If any step fails, add a fix item that names the step and what you saw. (`MacKeyStore.cs`)

## Why

The real key stores (MacKeyStore, WindowsKeyStore) have never run; tests use FakeKeyStore only.

## Notes

- From job 136 (factory/jobs/136-sensitive-notes-cert/build.md).
- Split from #133 (one item per OS).
