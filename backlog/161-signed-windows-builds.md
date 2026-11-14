# 161 Signed Windows builds

As a Windows user of Context, I want a signed desktop build, so that SmartScreen does not warn me when I install it.

## What

Add a signing path for the Windows desktop build (desktop-windows job in release.yml), switched off by default behind a repository variable like MACOS_SIGNING. Pick the signing service first (Azure Trusted Signing via vpk, or a certificate); confirm the vpk option exists before building. The unsigned path stays as it is.

## Why

Job 146 covered macOS only, by the owner's choice. Without signing, SmartScreen flags or blocks the Windows build.

## Notes

Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
