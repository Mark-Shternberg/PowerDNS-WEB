# INSTALL

## 1. Installation via install script
1. Download last release archive (powerdns-web-v*.tar.gz) from [release page](https://gitlab.medvedev-it.com/MedvedevN/powerdns-web/-/releases)
2. Run:
	```bash linenums="1"
	mkdir powerdns-web-install && tar -xzvf powerdns-web-v* -C powerdns-web-install && rm powerdns-web-v*
	cd powerdns-web-install
	sudo chmod +x install.sh && su root ./install.sh
	```

## 2. Manual install
1. Download and install dotnet-sdk-8.0
2. Download and install (if needed) pdns-recursor
3. Download and install MySQL server, pdns, pdns-backend-mysql
4. Create user and table in MySQL for programm
5. Download last release archive from [release page](https://gitlab.medvedev-it.com/MedvedevN/powerdns-web/-/releases)
6. Edit "MySQLConnection" block in appsettings.json
7. Run command: 
	```bash
	dotnet PowerDNS-Web.dll
	```
8. Install reverse proxy to `http://localhost:5000`

---

# UPGRADE

## 1. Upgrade via upgrade script

1. Download last release archive
2. Run: 
	```bash linenums="1"
	mkdir -p powerdns-web-upgrade && tar -xzvf powerdns-web-v* -C powerdns-web-upgrade
	cd powerdns-web-upgrade
	sudo chmod +x install.sh && sudo ./install.sh -update
	```

## 2. Manual upgrade

1. Download last release archive from [release page](https://gitlab.medvedev-it.com/MedvedevN/powerdns-web/-/releases)
2. Run:
	```bash
	mkdir powerdns-web-upgrade && tar -xzvf powerdns-web-v* -C powerdns-web-upgrade
	```
3. Move all exept appsettings.json from powerdns-weby-upgrade to your program folder