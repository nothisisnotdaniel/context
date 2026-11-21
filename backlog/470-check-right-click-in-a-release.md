# 470 Check right click in a Release build on macOS

As the developer, I want to check right click in a Release build on macOS, so that users never see the browser menu or dev tools.

Category: test

## What

Run the checklist below on macOS with a Release build and tick each step.

## Checklist

- [ ] Start a Release build (the release zip, or `dotnet run -c Release --project src/Context.Desktop`).
- [ ] Right-click empty space in a panel: no browser menu (Back, Reload, Inspect).
- [ ] Right-click a note link and a chip: no browser menu.
- [ ] Right-click a text field (palette, search): the text menu shows, or Cmd+C and Cmd+V work.
- [ ] Right-click selected text in the editor: the text menu shows; copy and paste work.
- [ ] Cmd+Option+I does nothing: no dev tools.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 224 blocks the web view's context menu in script; only a real Release build shows whether it works.

## Notes

- From job 224.
- Split from #308 (one item per OS).
