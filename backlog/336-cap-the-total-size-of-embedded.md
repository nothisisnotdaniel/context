# 336 Cap the total size of embedded images in a shared note

As a desktop user, I want Share to cap the total size of the images it embeds, so that sharing a note with many large images doesn't produce a huge HTML file or stall the app.

Category: security

## What

Share (src/Context.Core/NoteHtml.cs and the Share path in src/Context.Desktop) embeds up to 20 images of up to 10 MB each as base64 data. Add a total-bytes cap for embedded images (for example 25 MB); images past the cap are left out with the same placeholder used for a missing image, and the dialog or status line says how many were left out.

## Why

Security review of job 250: one page can grow to about 270 MB of base64, which is slow to write and to open; capping the total changes the output, so it needs its own job.

## Notes

Proposed by job 250-share-a-note (review-security 1, rounds 1 and 2).

Proposed by job 250-share-a-note (`factory/jobs/250-share-a-note/`).
