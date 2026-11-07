# 098 Sharing notes with key exchange

As the user, I want to plan end-to-end encrypted note sharing before approving it, so that notes can be shared without the server ever seeing them.

## What

Plan in detail before approving. A lightweight server (its own project) that stores only encrypted notes; each user has a public/private key pair, people exchange public keys once (verified, for example by comparing a short fingerprint), and a shared note is encrypted for exactly the people it is shared with, so the server never sees note text. To plan: key storage and recovery, revoking access, editing conflicts, what the server knows (metadata), hosting, and how shared notes look next to private ones.

## Why

Sharing needs a server, keys, verification and recovery, and mistakes there are costly. It needs a detailed plan (key storage and recovery among others) before anything is built.
