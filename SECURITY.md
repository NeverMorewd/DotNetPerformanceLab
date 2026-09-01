# Security Policy

## Self-hosted runner safety

Do not run this project on a self-hosted runner that accepts untrusted pull-request jobs. Performance workflows can execute caller repository code or an existing executable on the host.

Use a dedicated non-administrator account, the `metric-test` label, a protected `performance-lab` Environment, read-only workflow permissions, and a dedicated external-target directory. Review changes to workflow triggers and runner labels as security-sensitive changes.

## Reporting vulnerabilities

Report security issues privately through GitHub Security Advisories for this repository. Do not open a public issue for an undisclosed vulnerability.
