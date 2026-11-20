# 371 Command bar: fit the window to its content and check the rounded corners

As the user, I want the command bar window to be only as tall as its content, with corners that match its look, so that it feels like a light one-line bar.

Category: ui

## What

The bar window is a fixed 640x300, so there is a large empty area below the input when there are no suggestions, and the 10px CSS border radius sits in a square chromeless window. Resize the window to the input plus the shown suggestion rows, and either make the window transparent so the rounded corners show or drop the radius.

## Why

UI review of job 282 (nits 1 and 2).

## Notes

Proposed by job 282-command-bar (review-ui 1, review-ui 2).

Proposed by job 282-command-bar (`factory/jobs/282-command-bar/`).
