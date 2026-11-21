# 438 Check Suite Pro licence storage by hand on macOS and Windows

As the owner of Context, I want to try the Suite Pro licence on a real Mac and a real Windows machine, so that the Keychain and Credential Manager code, which no test runs, is known to work.

Category: test

## What

On the develop build, on macOS and on Windows: open Settings > Suite Pro, activate a valid key (state shows unlocked, licensee shown), restart the app (still unlocked, key not shown), check the Keychain item 'Suite Pro licence' / Credential Manager 'Suite/ProLicence' exists, try a wrong key (refused, nothing stored), remove the licence (confirm, item gone), and check that a key activated in Times is seen by Context and the other way round.

## Why

Job 367 was escalated by the approver because the new native key stores run only on a real OS; it was merged on the user's approval so it can be tested on the develop build. If it is not checked, a broken store would only show up for users.

## Notes

From job 367-pro-licence-context (approved by human for testing on develop).
