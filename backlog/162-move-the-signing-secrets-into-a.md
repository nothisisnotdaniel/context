# 162 Move the signing secrets into a GitHub environment

As the maintainer, I want the Apple signing secrets scoped to a protected GitHub environment, so that only the release job can read them.

## What

Move MACOS_CERTIFICATE_P12, MACOS_CERTIFICATE_PASSWORD, NOTARY_KEY_ID, NOTARY_ISSUER_ID and NOTARY_KEY_P8 from repository secrets into a GitHub environment used only by the macOS signing job, and document the setup in CLAUDE.md.

## Why

Raised by the security review of job 146. Repository-level secrets are readable by any workflow in the repo.

## Notes

Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
