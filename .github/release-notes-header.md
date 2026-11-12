## Desktop app (unsigned test builds)

The desktop builds are not signed yet, so the operating system warns before the first start.

- **macOS:** download `Context-<version>-osx-arm64.zip` (Apple silicon) or `Context-<version>-osx-x64.zip` (Intel),
  unzip it and open `Context.app`. macOS blocks it the first time: open System Settings > Privacy & Security and
  click `Open Anyway`, or run `xattr -dr com.apple.quarantine Context.app` in the Terminal once. The macOS copy does
  not update itself; download the next build when you want it.
- **Windows:** download and run `ContextDesktop-<channel>-Setup.exe`, where `<channel>` is `develop` for a develop
  prerelease and `win` for a release (`ContextDesktop-develop-Setup.exe` or `ContextDesktop-win-Setup.exe`). On the
  SmartScreen warning click `More info`, then `Run anyway`. The installed app keeps itself up to date: at each start
  it looks for a newer build on its own channel (`develop` builds follow the develop prereleases, a release follows
  the releases), downloads it and puts it in place after you close the app, or at the next start (the app then
  restarts once) when it stayed open for a while. `ContextDesktop-<channel>-Portable.zip` runs without installing
  and does not update itself. The `.nupkg`, `releases.*.json`, `assets.*.json` and `RELEASES*` files are for the
  updater.
- **Console tool:** `Context.<version>.nupkg` is the .NET tool package (command `context`).

