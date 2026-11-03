# Aspire in a fresh Linux Docker sandbox

Recovered from `verification/local-identity-stack` (`ed3f0f5`), where the Probe established this setup. Run it before certificate trust, start, wait or integration tests. A fresh sandbox may have lost tools and certificates; `/tmp/opencode` paths are disposable.

Install the versions in `global.json` and the AppHost: .NET SDK 10.0.402 (runtime 10.0.12 or a later patch), Aspire CLI 13.6.1 and Docker. Put the actual tool locations on `PATH`; set `DOTNET_ROOT` for a private SDK install.

Set these variables in **every terminal call**, preserving existing proxy exclusions:

```bash
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"
export NO_PROXY="${NO_PROXY:-localhost,127.0.0.1,::1},localhost,127.0.0.1,[::1]"
export no_proxy="${no_proxy:-localhost,127.0.0.1,::1},localhost,127.0.0.1,[::1]"
aspire certs trust --non-interactive
```

DCP uses a bracketed IPv6 loopback URI. Bare `::1` did not bypass the sandbox proxy and caused CONNECT 502 / one-minute startup timeouts. `SSL_CERT_DIR` enables Linux developer-certificate trust. Keep TLS validation enabled for the API, browser and Keycloak. macOS uses OS trust instead.

## Headless Chromium trust

The browser harness adds a Linux NSS trust boundary that the protocol-only Probe did not exercise. Install `libnss3-tools` and initialize `$HOME/.pki/nssdb` with `certutil -N -d "sql:$HOME/.pki/nssdb" --empty-password` if it does not exist, then run Aspire certificate trust again. Chromium needs its own trust; OpenSSL/.NET trust alone does not satisfy it.

If Chromium reports `ERR_CERT_INVALID` for the already imported developer certificate, inspect `certutil -L -d "sql:$HOME/.pki/nssdb"`. The developer certificate is a leaf (`CA:FALSE`), so use trusted-peer status for its **exact** nickname:

```bash
certutil -M -d "sql:$HOME/.pki/nssdb" -n 'aspnetcore-localhost-<actual-thumbprint>' -t 'P,,'
```

This was required by the Linux Chromium harness on 15 October 2026. Do not disable browser HTTPS validation. Certificate provisioning experiments in `/tmp` are not application prerequisites or maintained setup commands.

Run `./scripts/prepare.sh` after shell setup. Then follow [local verification](../../verification/README.md). Tests start their own AppHost on random ports and can run next to the presenter AppHost. Never delete the presenter volume to repair test setup.
