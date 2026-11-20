# 164 Set up Apple signing and check the first signed build

As the maintainer, I want to set up the Apple signing credentials and check the first signed build by hand, so that the notarized macOS app really installs without warnings.

## What

Get an Apple Developer ID Application certificate and an App Store Connect notary API key. Set the repository secrets MACOS_CERTIFICATE_P12, MACOS_CERTIFICATE_PASSWORD, NOTARY_KEY_ID, NOTARY_ISSUER_ID and NOTARY_KEY_P8, then set the variable MACOS_SIGNING. Let a develop run finish, download the arm64 and x64 zips, and check that Context.app opens without a Gatekeeper warning (spctl -a -vv), also from a second user account. Then decide whether entitlements should apply only to the main executable.

## Why

Job 146 added the signing path switched off; dotnet test cannot cover real signing, notarization or the first start.
