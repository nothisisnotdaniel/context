# 235 Signing hardening and Windows signing

As the Context developer, I want the signing secrets and tools locked down and a Windows signing path ready, so that signed builds are safe and Windows users are not warned off.

Category: security

## What

- Signed Windows builds (was #161): Add a signing path for the Windows desktop build (desktop-windows job in release.yml), switched off by default behind a repository variable like MACOS_SIGNING. Pick the signing service first (Azure Trusted Signing via vpk, or a certificate); confirm the vpk option exists before building. The unsigned path stays as it is.
- Move the signing secrets into a GitHub environment (was #162): Move MACOS_CERTIFICATE_P12, MACOS_CERTIFICATE_PASSWORD, NOTARY_KEY_ID, NOTARY_ISSUER_ID and NOTARY_KEY_P8 from repository secrets into a GitHub environment used only by the macOS signing job, and document the setup in CLAUDE.md.
- Call signing tools by absolute path (was #163): Use /usr/bin paths for the signing tools in the macOS signing script, with a test seam that still lets the tests stub them.

## Why

- #161: Job 146 covered macOS only, by the owner's choice. Without signing, SmartScreen flags or blocks the Windows build.
- #162: Raised by the security review of job 146. Repository-level secrets are readable by any workflow in the repo.
- #163: Raised by the security review of job 146. A PATH lookup lets an earlier step shadow the tools that handle the certificate.

## Notes

- Merges #161, #162, #163 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode, full path, high risk (secrets and signing). Confirm the vpk signing option exists before building the Windows part.
- #161: Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
- #162: Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
- #163: Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
