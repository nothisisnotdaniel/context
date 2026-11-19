# 108 Pro licence in Context

As a Suite Pro buyer, I want Context to use the licence I activated in Times, so that I activate once for the whole suite.

## What

Context reads the shared activation file and keychain item from Times#39 and checks the key the same way (same key format, public keys and revocation list, offline, updates-until on or after the build date). The desktop app's Settings show the Pro state (licensee, updates-until) and can activate or remove a key the same way when Times isn't installed. One `Pro` check gates the Pro features that already exist by then (Dossier, meeting flow, handover pack, MCP server); each shows a short, friendly locked hint instead of failing. Everything built before this item stays as it is apart from that one check. Full path, high risk: crypto, secrets and a shared data format.

## Why

The Pro features are built first, ungated, and move behind the licence here. One shared format keeps the apps in step and the buyer's experience simple.

## Notes

Updated on 2026-11-19: no Lemon Squeezy, no online re-check; keys are issued by hand (Times#39) until a provider is chosen (#399).
