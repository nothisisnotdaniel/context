# 091 Windows device check at open

As a Context user on Windows, I want device files detected on the opened handle, so that a file can't be swapped between the check and the open.

## What

On Windows, `BoundedFile` checks for device files before it opens the file (check-then-use). Check the opened handle instead, so the file can't change between the check and the open.

## Why

Checking before opening leaves a small time-of-check to time-of-use gap. Checking the handle closes it.
