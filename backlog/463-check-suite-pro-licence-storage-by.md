# 463 Check Suite Pro licence storage by hand on Windows

As the developer, I want to try Suite Pro activation by hand on Windows, so that the real licence store is known to work.

Category: test

## What

Run the checklist below on Windows with a Release build of the desktop app and tick each step.

## Checklist

- [ ] Start the desktop app on Windows (Setup.exe or Release build from develop).
- [ ] Settings > Suite Pro: activate a valid key. It shows unlocked and the licensee name.
- [ ] Quit and start again: still unlocked, and the key is never shown.
- [ ] Control Panel > Credential Manager > Windows Credentials has `Suite/ProLicence`.
- [ ] Enter a wrong key: it is refused and the credential is unchanged.
- [ ] Remove: a confirm dialog appears; after confirming, the credential is gone and Settings shows locked.
- [ ] Activate in Times, restart Context: it shows unlocked. Remove in Context: Times shows locked after a restart.
- [ ] If any step fails, add a fix item that names the step and what you saw. (`WindowsLicenceStore.cs`)

## Why

The licence stores were written in job 367 and no test touches the real OS store.

## Notes

- From job 367.
- Split from #438 (one item per OS).
