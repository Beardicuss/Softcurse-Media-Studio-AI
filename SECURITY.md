# Security policy

## Supported versions

Security fixes are applied to the latest released `3.x` version. Older builds should be upgraded before reporting behavior that may already be fixed.

## Reporting a vulnerability

Do not open a public issue containing exploit details, credentials, private endpoints, or personal media. Use the repository's private GitHub Security Advisory reporting flow instead:

`https://github.com/Beardicuss/Softcurse-Media-Studio-AI/security/advisories/new`

Include the affected version, module, reproduction conditions, impact, and the smallest sanitized proof of concept possible. Remove API keys, access tokens, local usernames, private paths, and copyrighted/private media.

## Release trust

Official releases should provide:

- a versioned installer;
- a valid Softcurse Authenticode signature;
- an RFC 3161 timestamp;
- a matching SHA-256 entry in `SHA256SUMS.txt`.

Unsigned internal test installers must not be presented as public production releases.
