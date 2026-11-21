# 467 Check desktop settings key details on Windows

As the developer, I want to check the key details in Settings on Windows, so that they match the real key.

Category: test

## What

Run the checklist below on Windows after sensitive notes are set up, and tick each step.

## Checklist

- [ ] Settings shows the name `Context sensitive notes`.
- [ ] The thumbprint equals the one certmgr.msc shows under Personal > Certificates for `Context sensitive notes`.
- [ ] The expiry equals that certificate's Expiration Date.
- [ ] Showing Settings never brings up a Windows Hello or key prompt.
- [ ] With no machine key yet (fresh user account), Settings shows no key details and no prompt.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

MachineKeyDetails reads the real key store, which no test touches.

## Notes

- From job 138.
- Split from #146 (one item per OS).
