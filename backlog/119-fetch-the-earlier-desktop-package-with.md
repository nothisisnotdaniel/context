# 119 Fetch the earlier desktop package with gh release download

As the Context maintainer, I want the Windows release job to fetch the previous package with gh release download, so that the GitHub token is no longer passed to vpk on its command line.

## What

In the Windows desktop job of release.yml, download the previous release feed and full nupkg with gh release download (GH_TOKEN in env) into the vpk output folder, and drop --token from the vpk call. Update ReleaseWorkflowTests.

## Why

Security review of job 133 (nit 5): a token on a command line can show up in process listings and in tool logs; gh reads it from the environment.

## Notes

Proposed by job 133-desktop-builds-auto-update.
