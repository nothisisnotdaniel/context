# 015 Signed desktop builds

As a Context user, I want notarized macOS and signed Windows builds of the desktop app, so that I can install it without security warnings.

## What

Publish scripts for a notarized macOS `.app` (arm64 + x64) and a signed Windows build (zip or Velopack), documented in CLAUDE.md; the console app keeps shipping as is.

## Why

Unsigned apps are blocked or flagged by macOS Gatekeeper and Windows SmartScreen. Signing is what makes the desktop app installable for normal use.
