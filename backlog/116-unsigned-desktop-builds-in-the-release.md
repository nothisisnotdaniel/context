# 116 Unsigned desktop builds and Windows auto-update

As a Context user, I want the desktop app as a download on every GitHub release, with a Windows installer that keeps itself up to date from the develop prereleases, so that I can install the UI and test the latest develop build on Windows before signed builds exist.

## What

Add desktop jobs to `.github/workflows/release.yml` that run after the existing checks and attach to the same release (for all three triggers: develop prereleases, release PRs and tags). No certificates, no secrets, no notarization.

- **macOS:** on a `macos-latest` runner, `dotnet publish src/Context.Desktop -c Release --self-contained -p:Version=$VERSION` for `osx-arm64` and `osx-x64`, each wrapped in a `Context.app` with an `Info.plist`, ad-hoc signed (`codesign --force --deep -s -`, needed for arm64 to run at all) and zipped.
- **Windows, with Velopack:** on a `windows-latest` runner, publish `win-x64` self-contained and pack it with Velopack's `vpk pack` (pinned version of the `vpk` .NET tool), with a package id distinct from the console tool's `Context` (for example `ContextDesktop`). Attach `Setup.exe`, the portable zip, the full (and delta, when there is one) `.nupkg` and the release feed files vpk writes. `Setup.exe` installs per user, with no admin prompt, a Start menu entry and an uninstaller.
- **Channels:** develop prereleases are packed on a `develop` channel, proper releases on the default (stable) channel, so a develop install only updates to newer develop prereleases and a stable install only to proper releases.
- **Auto-update:** add the `Velopack` NuGet package to `Context.Desktop` only (pinned, same version as `vpk`). `Program.cs` calls `VelopackApp.Build().Run()` first. On start, off the UI thread, the app checks this repo's GitHub releases (`GithubSource`, prereleases included only on the `develop` channel), downloads a newer version on its own channel and applies it when the app exits, so the next start runs it. Only an installed copy checks (not `dotnet run`, not the portable zip, not tests). The check never blocks start-up, never shows an error dialog and never writes to the data folder; a failure is silent and retried on the next start. No network in tests: the update code sits behind a small interface with a fake.
- **Docs:** CLAUDE.md (what ships, the channels, the updater and its trust model) and the release notes say how to open an unsigned build: macOS System Settings > Privacy & Security > Open Anyway (or `xattr -dr com.apple.quarantine Context.app`), Windows SmartScreen > More info > Run anyway (once, for `Setup.exe`).
- **Tests:** extend `ReleaseWorkflowTests` for the new jobs (pinned actions and tool versions, channel per trigger, no `${{ }}` in `run:` scripts), and test the start-up update flow with the fake.

## Why

Today the release ships only the console tool, so the desktop app can only be run from source. Testing on Windows means building it there by hand after every change. A `Setup.exe` that updates itself from the develop prereleases lets the user install once and always get the latest develop build. Signing (#15) needs an Apple Developer account and a Windows certificate; unsigned builds let the UI ship now, and #15 then only adds signing steps to these jobs.

## Notes

Full path, high risk: a new dependency (`Velopack`), network access and a self-updater. Without signing, an update is trusted only because it comes over HTTPS from this repo's GitHub releases, which only the release workflow writes; record that in CLAUDE.md. Pruning stays as it is: after a proper release the old develop prereleases are deleted, and a develop install keeps updating from the newest one left. Signed and notarized builds, and signature checks in the updater, stay in #15, which waits for this item.
