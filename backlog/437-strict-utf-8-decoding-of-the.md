# 437 Strict UTF-8 decoding of the Keychain licence entry

As a user, I want a corrupted Keychain licence entry to be refused, so that a damaged key is never repaired into something else silently.

Category: security

## What

MacLicenceStore decodes the Keychain item with lenient UTF-8. Decode it strictly (throwOnInvalidBytes) and treat invalid bytes as no stored key, with a test through the store's decoding helper.

## Why

Raised by the security review of job 367: lenient decoding turns invalid bytes into U+FFFD instead of refusing them. The key then fails verification anyway, but strict decoding states the rule; it changes behaviour, so it was left.

## Notes

Proposed by job 367-pro-licence-context.

Proposed by job 367-pro-licence-context (`factory/jobs/367-pro-licence-context/`).
