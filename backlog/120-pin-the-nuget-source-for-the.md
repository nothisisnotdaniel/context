# 120 Pin the NuGet source for the desktop packages

As the Context maintainer, I want Velopack and vpk to restore only from nuget.org, so that adding a second NuGet source later cannot swap in a look-alike package.

## What

Add package source mapping (nuget.config) that maps Velopack and vpk to nuget.org, or document why it is not needed yet.

## Why

Security review of job 133 (nit 6): it only matters once a second source exists, but by then nobody remembers to add the mapping.

## Notes

Proposed by job 133-desktop-builds-auto-update.
