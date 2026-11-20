# 129 Pin the Windows certificate by thumbprint

As a Windows user, I want the sensitive-notes certificate pinned by thumbprint, so that a second certificate with the same name cannot be picked by mistake.

## What

Pin the Windows machine certificate by thumbprint, or refuse when two certificates share the name.

## Why

Raised by the security review of job 136 (nit 4); a design change in WindowsKeyStore, which has never run on a real machine.

## Notes

Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
