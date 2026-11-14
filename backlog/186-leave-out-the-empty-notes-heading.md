# 186 Leave out the empty Notes heading in context person

As a Context user, I want `context person` to print no Notes heading when there are no notes, so that the output stays short and clear.

## What

In `context person <name>`, print the Notes heading only when at least one note mentions the person, as the desktop PersonPanel already does. Update the spec text for that output and its tests.

## Why

Someone who appears only in appointments gets an empty Notes heading, which looks like missing data.

## Notes

Proposed by job 156-people-pages (review-ux round 1, nit 1).
