# 201 Mention --template in the empty /new hint

As a Context user, I want the empty /new hint to mention templates, so that I find out templates exist without reading the docs.

## What

When /new is run without a title, change the hint from `Type /new <title>.` to also mention `[--template <name>]`, matching the palette hint. Update the spec text and tests.

## Why

The palette shows --template, but the hint shown on a blank /new does not, so the feature is easy to miss.

## Notes

Proposed by job 165-note-templates (review-ui and review-ux round 1, nit 1).

Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
