# PowerDNS Web

**English** | [Русский](README.ru.md)

PowerDNS Web is a web-based administration panel for PowerDNS servers. It lets you manage DNS zones and records in your browser, configure query forwarding in PowerDNS Recursor, and monitor your DNS services.

## Features

- Create and edit forward and reverse DNS zones.
- Manage Native, Master, and Slave zones. Records in Slave zones are read-only and receive updates from the primary server.
- Add, edit, and delete DNS records with input validation.
- Manage DNSSEC and view zone keys.
- Manage PowerDNS Recursor forward zones, including the root zone `.`.
- View PowerDNS statistics and logs.
- User accounts with administrator and read-only roles.
- English and Russian interfaces, with light and dark themes.

## Requirements

- Debian 12 or later, or Ubuntu 24.04 or later, on **amd64 (x86-64)**.
- PowerDNS Authoritative with its HTTP API enabled. You will need the API URL and access key.
- MySQL or MariaDB for the panel's user accounts.
- PowerDNS Recursor with its API configured, if you want to manage DNS query forwarding.

Installation and updates use APT. You do not need to install .NET separately.

Configure PowerDNS and Recursor separately. Installing the panel does not create a DNS zone database or configure replication between DNS servers.

## Installation

### 1. Add the repository

Run these commands on the server:

```sh
sudo apt update
sudo apt install curl gnupg ca-certificates
sudo install -d -m 0755 /etc/apt/keyrings

curl -fsSL http://repo.medvedev-it.com/keys/medvedev-it-apt.asc | sudo gpg --dearmor -o /etc/apt/keyrings/medvedev-it-apt.gpg
sudo chmod 0644 /etc/apt/keyrings/medvedev-it-apt.gpg

echo "deb [signed-by=/etc/apt/keyrings/medvedev-it-apt.gpg] http://repo.medvedev-it.com/ medvedev-it main" \
| sudo tee /etc/apt/sources.list.d/medvedev-it.list >/dev/null

sudo apt update
```

If this repository is already configured, you do not need to add it again.

### 2. Install and configure the panel

```sh
sudo apt install powerdns-web
sudo powerdns-web-setup
```

The setup wizard will ask you to:

1. Create a new local MariaDB database and user for the panel, or connect to an existing database.
2. Enter the PowerDNS Authoritative API URL, such as `http://127.0.0.1:8081`.
3. Enter the API access key.

Automatic database creation requires a running local MariaDB instance with administrator access through a Unix socket. To use an existing database, have its server address, database name, username, and password ready.

After setup completes successfully, the service starts automatically. The panel listens on `http://127.0.0.1:5000` on the server.

### 3. Open the panel and create an administrator account

You can use an SSH tunnel for your first sign-in. Run the following command on your computer, replacing `user@server` with your SSH username and server address:

```sh
ssh -L 5000:127.0.0.1:5000 user@server
```

Keep the SSH connection open and visit `http://127.0.0.1:5000` in your browser.

When signing in for the first time with a new database, enter your desired username and password. The panel will create an administrator account. Use those credentials for subsequent sign-ins.

For ongoing access through a domain name, configure Nginx with HTTPS. The package includes an example configuration:

```text
/usr/share/doc/powerdns-web/nginx.conf.example
```

Replace `dns.example.org` in the example with your domain. Restrict access to the panel to your infrastructure administrators.

## Getting started

- Add a DNS zone in the zones section, then open it to manage its records.
- For a Slave zone, specify the primary server addresses. Configure zone transfer permissions in PowerDNS beforehand.
- To manage Recursor, enter its API URL and key in the panel settings and enable Recursor support.
- Create additional accounts in the users section.

## Updating

```sh
sudo apt update
sudo apt install --only-upgrade powerdns-web
```

You do not need to run the setup wizard again. Updates restart the web service, so the panel may be briefly unavailable.

If APT asks whether to replace a modified `appsettings.json`, choose to keep the current configuration to retain your connection settings. Copies of the settings are saved to `/var/lib/powerdns-web/backups` before updates. These copies do not replace database backups.

## Checking service status

Check the service:

```sh
sudo systemctl status powerdns-web
```

View recent service messages:

```sh
sudo journalctl -u powerdns-web -n 100 --no-pager
```

If the panel does not start after installation, make sure `powerdns-web-setup` completed successfully and the database is accessible. If zones do not load, check the PowerDNS API URL and key, and confirm that the API is reachable from the panel's server.

## License

[MIT](LICENSE.txt).
