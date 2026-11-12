# 121 Open empty notes in the default editor on macOS and Linux

As a Context user on macOS or Linux, I want "Open in default editor" to work for an empty note, so that I can start writing a new note in my own editor.

## What

"Open in default editor" refuses an empty note off Windows, because an empty file looks the same as a FIFO and opening one to check could hang. Tell the two apart without a blocking open (for example lstat / File.GetUnixFileMode or a file-type check), and allow regular empty files. Keep refusing FIFOs and other special files.

## Why

Raised by the code, security and ux reviews of job 134: a new note starts empty, so today the fallback editor cannot be used for the most common first step on macOS and Linux.

## Notes

Proposed by job 134-desktop-notes.

Proposed by job 134-desktop-notes (`factory/jobs/134-desktop-notes/`).
