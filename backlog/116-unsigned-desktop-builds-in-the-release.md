# 116 Unsigned desktop builds in the release

As a Context user, I want the desktop app as a download on every GitHub release, so that I can install the UI before signed builds exist.

## What

Add a desktop job to .github/workflows/release.yml that runs after the existing checks and attaches to the same release: `dotnet publish src/Context.Desktop -c Release --self-contained -p:Version=$VERSION` for osx-arm64 and osx-x64 on a macos-latest runner, each wrapped in a Context.app with an Info.plist, ad-hoc signed (`codesign --force --deep -s -`, needed for arm64 to run at all) and zipped; and win-x64, zipped. No certificates, no secrets, no notarization. Extend ReleaseWorkflowTests for the new job. Document in CLAUDE.md and the release notes how to open an unsigned build: macOS System Settings > Privacy & Security > Open Anyway (or `xattr -dr com.apple.quarantine Context.app`), Windows SmartScreen > More info > Run anyway.

## Why

Today the release ships only the console tool, so the desktop app can only be run from source. Signing (#15) needs an Apple Developer account and a Windows certificate; unsigned builds let the UI ship now, and #15 then only adds signing steps to this job.

## Notes

Signed and notarized builds stay in #15, which waits for this item.
