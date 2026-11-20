# 023 Voice capture spike

As a Context user, I want to know how voice notes could work in the desktop app, so that the voice notes feature is built on an approach that's known to work.

## What

Find out on macOS and Windows how the Photino desktop app can take voice notes: whether the web view may use the microphone (`getUserMedia`, permission prompts), whether the web view's own speech recognition works and where it sends audio, and what local Whisper (Whisper.net, model size, speed for German and English) would cost. Write the findings and a recommended approach into the job; merge no code. Needs the user at the machine; run it as a spike once the factory has the spike path.

## Why

Microphone access in the web view, where speech recognition sends audio, and how fast local Whisper is are all open questions. Finding out first avoids building on a wrong assumption, especially about privacy.
