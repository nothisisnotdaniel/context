# App icon

`context-icon.svg` is the source artwork: a white "C" around an amber dot on a blue rounded square.
The three icon files are made from it and committed:

- `Context.icns`: the macOS bundle icon (`.github/scripts/macos-app.sh` copies it to `Context.app/Contents/Resources/`).
- `Context.ico`: the Windows exe icon (`ApplicationIcon`), the window icon on Windows and the `Setup.exe` icon (`vpk pack --icon`).
  Sizes 16, 24, 32, 48, 64 and 256, stored as PNG entries.
- `Context.png`: 512x512, the window icon off Windows.

After changing the SVG, run `sh src/Context.Desktop/Assets/make-icons.sh` on a Mac and commit the three files.
It uses only tools that come with macOS (`sips` renders the SVG and scales it, `iconutil` packs the `.icns`,
`python3` writes the `.ico`). `dotnet build` and `dotnet test` never run it.
