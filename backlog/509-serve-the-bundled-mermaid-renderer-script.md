# 509 Serve the bundled Mermaid renderer script to the Windows desktop frame

As a Windows desktop user, I want a valid Mermaid diagram to draw and an invalid diagram to show the editor's error message.

Category: bug

## What

- Serve the shipped wwwroot/mermaid/mermaid-frame.js at the URL requested by mermaid/frame.html in the real Photino/WebView2 desktop app, with successful status and JavaScript content. In the observed Debug build, the file was present in the application output but the sandboxed frame's parser received an HTML 404.
- Keep the existing frame sandbox, script-src 'self' policy and local-only rendering. Confirm that a valid flowchart and sequence diagram replace the loading hint with an image in Light, then in Dark after reopening. An ordinary syntax error must settle on the documented error hint.
- Add an integration regression check that exercises the packaged frame and script through the desktop host. Repeat the Windows checklist #475. The separate macOS acceptance stays in #474.

## Why

At develop 29b1b32c on Windows WebView2 158.0.4370.33, two private profiles reproduced the failure. The frame document loaded with HTTP 200. Its parser's request for mermaid-frame.js returned HTTP 404 with Content-Type text/html; charset=us-ascii and WebView2 reported net::ERR_BLOCKED_BY_ORB. Both valid diagram types and the invalid example remained on Drawing diagram… in both themes.

## Notes

From #475 and the original implementation #86 / job 263. #344 concerns the image output after a successful render and does not cover serving the frame script.
