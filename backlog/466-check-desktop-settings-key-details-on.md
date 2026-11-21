# 466 Check desktop settings key details on macOS

As the developer, I want to check the key details in Settings on macOS, so that they match the real key.

Category: test

## What

Run the checklist below on macOS after sensitive notes are set up, and tick each step.

## Checklist

- [ ] Settings shows the key's name, a 64-character fingerprint and `Does not expire`.
- [ ] The fingerprint is the same after a restart.
- [ ] Showing Settings never brings up a Keychain or Touch ID prompt.
- [ ] With no machine key yet (fresh user account), Settings shows no key details and no prompt.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

MachineKeyDetails reads the real key store, which no test touches.

## Notes

- From job 138.
- Split from #146 (one item per OS).
