# 139 Bounded read of calendar-link.txt

As a Context user, I want the calendar link file read with a size limit, so that a huge file or FIFO with that name cannot hang the settings page or the app.

## What

Read calendar-link.txt through BoundedFile with a small limit (link max is 2048 characters) in Core, and show a fixed error text when it is too big or not a regular file.

## Why

Review-security (job 138): the desktop settings load now reads this file too, so an oversized file or FIFO would hang it.

## Notes

Proposed by job 138-desktop-settings (`factory/jobs/138-desktop-settings/`).
