# 163 Call signing tools by absolute path

As the maintainer, I want the macOS signing step to call codesign, security, xcrun and ditto by absolute path, so that a changed PATH cannot swap in another tool.

## What

Use /usr/bin paths for the signing tools in the macOS signing script, with a test seam that still lets the tests stub them.

## Why

Raised by the security review of job 146. A PATH lookup lets an earlier step shadow the tools that handle the certificate.

## Notes

Proposed by job 146-signed-desktop-builds (`factory/jobs/146-signed-desktop-builds/`).
