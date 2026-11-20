# 146 Check desktop settings key details on Windows and macOS

As the owner, I want the settings page key details checked on real machines, so that the untested Windows code is known to work.

## What

On Windows, the settings thumbprint and expiry must equal what certmgr.msc shows for Context sensitive notes. On macOS, the section must show the name, a 64-character fingerprint and Does not expire.

## Why

Approver (job 138): WindowsKeyStore.MachineKeyDetails has never run; tests use FakeKeyStore only.
