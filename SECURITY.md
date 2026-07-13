# Security Policy

## Supported Versions

Security fixes are applied to the active `beta` branch and the latest promoted
`stable` release.

## Reporting a Vulnerability

Do not open public issues containing secrets, customer data, logs with tokens,
API keys, passwords, DPAPI files, or exploitable details.

For the private repository, create a GitHub issue with:

- short summary;
- affected version;
- affected component;
- reproduction steps without secrets;
- sanitized logs or screenshots;
- expected impact;
- proposed mitigation if known.

For public/customer releases, use the security contact published on
`iiko-plugin.kz`.

## Secret Handling

Never commit:

- Webkassa API keys;
- Webkassa login/password pairs;
- Webkassa tokens;
- Bitwarden session values;
- DPAPI secret files;
- production config files;
- customer support bundles;
- raw iikoFront/Webkassa logs containing customer data.

Use placeholders, SecretRef labels, or sanitized examples in documentation.

## Package Integrity

Production releases must provide at least a SHA256 checksum for downloadable
packages. Authenticode signing and detached package signatures are the preferred
target for stable releases.

The updater must verify checksums or signatures before replacing an installed
plugin and must never disable TLS certificate validation.
