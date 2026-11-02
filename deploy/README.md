# APT_Admin: PowerDNS Web

Supported: Debian 12+ and Ubuntu 24.04+, amd64. No RPM builds.
The package is self-contained; .NET is not required on the target server.

## Build and publish

Update APT_Admin to include the powerdns-web profile, then create a project:

| Field | Value |
| --- | --- |
| Packaging profile | PowerDNS Web (DEB only), internal packaging enabled |
| Project | PowerDNS_Web.csproj |
| Runtime / configuration | linux-x64 / Release |
| Package / architecture | powerdns-web / amd64 |
| Install path | /opt/medvedev-it/powerdns-web |
| Repository | Your signed Debian repository |

Commit deploy/ and packages.lock.json before tagging the release. APT_Admin
validates the tag and restores locked dependencies. Build host needs .NET 8 SDK
and dpkg-deb. Recipe: deploy/apt-admin-package.json.

## Install and configure

Connect the signed repository using APT_Admin's instructions for your configured
repository URL, distribution and signing key, then:

```sh
sudo apt-get update
sudo apt-get install powerdns-web
sudo powerdns-web-setup
```

Setup requests database credentials and Authoritative API URL/key. It can create a
new local MariaDB database/account with local root socket authentication, or reuse
an existing database. Generated credentials are saved immediately for recovery if
setup is interrupted. The service starts on 127.0.0.1:5000 after setup.

Configure PowerDNS and Recursor separately, including API access, primary/secondary
mode and AXFR permissions. No DNS database, DNS configuration or nginx vhost is
created or overwritten by this package. Optional local servers are Recommends.
Recursor can be enabled in web settings after its API is configured.
Reverse proxy example: /usr/share/doc/powerdns-web/nginx.conf.example.

The service runs as root to preserve the current application's system configuration
functions. Restrict access to the administration interface.

## Update

```sh
sudo apt-get update
sudo apt-get install --only-upgrade powerdns-web
```

appsettings.json is a dpkg conffile. Local changes use normal dpkg conffile handling.
For unattended updates choose --force-confdef --force-confold. Scripts also snapshot
settings in /var/lib/powerdns-web/backups (root only). Updates restart only the web
service and honor policy-rc.d through deb-systemd-invoke. No apt calls or prompts run
inside maintainer scripts.

Removal keeps configuration; purge removes package conffiles and the setup marker.
Databases, logs and backups are retained for recovery.

## Manual installation migration

Back up the database and appsettings.json. Stop/disable the old powerdnsweb.service
if used. Move any old /etc/systemd/system/powerdns-web.service to a backup location:
it overrides the packaged /lib/systemd/system unit. Check custom drop-ins for an old
dotnet ExecStart. Keep your existing settings when dpkg prompts. Configured installs
are detected and started without rerunning setup.

Adding this integration does not publish a package or change production.

## Smoke test

Publish self-contained linux-x64 into a temporary directory, then mount the
repository at /src and the publish directory at /publish (both read-only) in
a disposable debian:bookworm-slim container and run:

```sh
bash /src/deploy/tests/package-smoke.sh
```

This builds two DEBs and verifies payload, config permissions, install, upgrade
conffile preservation, removal, purge and retained backups. It deliberately omits
dependency resolution and systemd startup: those require a complete target VM.
