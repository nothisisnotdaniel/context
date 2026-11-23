# 501 Plain wording for a failed update check

As a Context user, I want a failed update check in Settings to say in plain words what went wrong, so that I understand it without reading an exception type.

Category: ux

## What

Settings shows a failed check as failed: <Type>: <message>. Show a short plain sentence instead (for example "Couldn't reach GitHub to check for updates." for a network error, and a general sentence otherwise), and keep the technical line only in .logs/update.log. Update the tests that check the text.

## Why

review-ux 1 in job 396-version-and-update-log: the technical text is hard to read for a user. It was the spec's wording, so changing it needed its own item.

## Notes

Left by the builder of job 396-version-and-update-log as a nit.
