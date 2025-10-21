#!/usr/bin/env bash
set -euo pipefail

# === COLORS ===
colGreen="\033[32m"
colRed="\033[31m"
resetCol="\033[0m"

# === HELPERS ===
die() { echo -e "${colRed}$*${resetCol}" >&2; exit 1; }
ok()  { echo -e "${colGreen}$*${resetCol}"; }

trap 'die "ERROR ON LINE $LINENO"' ERR

# === ROOT CHECK ===
[[ "$(id -u)" -eq 0 ]] || die "THIS SCRIPT MUST BE RUN AS ROOT."

# === PATHS / NAMES ===
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="${SCRIPT_DIR}/powerdns-web"
INSTALL_DIR="/opt/medvedev-it/powerdns-web"
SERVICE_NAME="powerdns-web.service"

# === CHECK PORT 53 FREE (FRESH INSTALL ONLY) ===
check_port_53() {
  if ss -tuln | awk '{print $5}' | grep -qE '(^|:)53$'; then
    die "ERROR: PORT 53 IS ALREADY IN USE."
  fi
}

# === OS PACKAGE INSTALLER ===
execute_by_distro() {
  local DISTRO=""
  if [[ -f /etc/os-release ]]; then
    . /etc/os-release
    DISTRO="${ID}"
  fi

  case "$DISTRO" in
    ubuntu|debian)
      apt -qq update
      DEBIAN_FRONTEND=noninteractive apt install -y \
        mysql-server nginx pdns-server pdns-backend-mysql jq || die "ERROR INSTALLING PACKAGES."
      if [[ "${install_recursor_flag}" == "true" ]]; then
        DEBIAN_FRONTEND=noninteractive apt install -y pdns-recursor
        apt install -y publicsuffix || true
      fi
      ;;
    centos|rhel)
      yum -q -y update
      yum -y install mysql-server nginx pdns-server pdns-backend-mysql jq || die "ERROR INSTALLING PACKAGES."
      if [[ "${install_recursor_flag}" == "true" ]]; then
        yum -y install pdns-recursor || true
      fi
      ;;
    fedora)
      dnf -q -y upgrade
      dnf -y install mysql-server nginx pdns-server pdns-backend-mysql jq || die "ERROR INSTALLING PACKAGES."
      if [[ "${install_recursor_flag}" == "true" ]]; then
        dnf -y install pdns-recursor || true
      fi
      ;;
    arch)
      pacman -Syu --noconfirm
      pacman -S --noconfirm mariadb nginx pdns-server pdns-backend-mysql jq || die "ERROR INSTALLING PACKAGES."
      if [[ "${install_recursor_flag}" == "true" ]]; then
        pacman -S --noconfirm pdns-recursor || true
      fi
      ;;
    *)
      die "UNKNOWN OR UNSUPPORTED LINUX DISTRIBUTION."
      ;;
  esac
}

# === UPDATE MODE ===
if [[ " ${*} " == *" -update "* ]]; then
  systemctl list-units --type=service --all 2>/dev/null | grep -q "^${SERVICE_NAME}" \
    || die "POWERDNS-WEB IS NOT INSTALLED. RUN WITHOUT -update."
  [[ -d "${INSTALL_DIR}" ]] || die "NOT INSTALLED IN DEFAULT DIR (${INSTALL_DIR}). UPDATE MANUALLY."
  [[ -d "${SRC_DIR}" ]]     || die "NEW VERSION FOLDER NOT FOUND: ${SRC_DIR}"

  systemctl stop "${SERVICE_NAME}"
  sleep 1

  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete \
      --exclude "appsettings.json" \
      "${SRC_DIR}/" "${INSTALL_DIR}/"
  else
    shopt -s dotglob
    for f in "${INSTALL_DIR}"/*; do
      [[ "$(basename "$f")" == "appsettings.json" ]] && continue
      rm -rf "$f"
    done
    cp -a "${SRC_DIR}/." "${INSTALL_DIR}/"
  fi

  systemctl start "${SERVICE_NAME}"
  systemctl is-active --quiet "${SERVICE_NAME}" \
    && ok "\tPOWERDNS-WEB UPGRADED." \
    || die "UPGRADE FAILED."
  exit 0
fi

# === APT/DPKG LOCKS CHECK (DEBIAN/UBUNTU) ===
if command -v lsof >/dev/null 2>&1; then
  if lsof /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock /var/cache/apt/archives/lock >/dev/null 2>&1; then
    die "ANOTHER APT/DPKG PROCESS IS RUNNING."
  fi
fi
pgrep -x "unattended-upgrade" >/dev/null && die "APT UNATTENDED-UPGRADE IS RUNNING."

# === ASK ABOUT RECURSOR ===
read -r -p "DO YOU WANT TO INSTALL POWERDNS RECURSOR? [Yy/Nn]: " install_recursor
case "${install_recursor}" in
  [yY]) recursor_enabled="Enabled"; install_recursor_flag=true ;;
  [nN]) recursor_enabled="Disabled"; install_recursor_flag=false ;;
  *)    die "INVALID INPUT. TYPE Y OR N." ;;
esac

# === CONFIRM PACKAGES ===
while true; do
  read -r -p "WILL BE INSTALLED: JQ, MYSQL-SERVER, PDNS-BACKEND-MYSQL, PDNS-SERVER, NGINX. OK? [Yy/Nn]: " accept
  case "${accept}" in
    [yY]) break ;;
    [nN]) echo "EXITING..."; exit 0 ;;
    *)    echo -e " ${colRed}TYPE Y OR N!${resetCol}" ;;
  esac
done

# === PREPARE DIRECTORIES ===
mkdir -p /var/opt/pdns-rec-api /var/www

# === FRESH INSTALL MUST ENSURE PORT 53 IS FREE ===
check_port_53

# === CHECK DOTNET IN PATH AND VERSION ===
if ! command -v dotnet >/dev/null 2>&1; then
  die "DOTNET NOT FOUND IN PATH. INSTALL .NET SDK 8.0 (UBUNTU 20: SNAP INSTALL DOTNET-SDK && SNAP ALIAS DOTNET-SDK.DOTNET DOTNET; UBUNTU 22+: APT INSTALL DOTNET-SDK-8.0) AND RE-RUN."
fi
DOTNET_V_RAW="$(dotnet --version || true)"
DOTNET_MAJOR="${DOTNET_V_RAW%%.*}"
if [[ -z "${DOTNET_MAJOR}" || "${DOTNET_MAJOR}" -lt 8 ]]; then
  die "FOUND DOTNET '${DOTNET_V_RAW}', BUT .NET 8.0+ IS REQUIRED."
fi
ok "\tDETECTED .NET ${DOTNET_V_RAW}"

# === MOVE APP INTO PLACE ===
[[ -d "${INSTALL_DIR}" ]] && die "DIRECTORY ${INSTALL_DIR} ALREADY EXISTS. REMOVE IT OR USE -update."
[[ -d "${SRC_DIR}" ]]     || die "SOURCE FOLDER NOT FOUND: ${SRC_DIR}"

mkdir -p "$(dirname "${INSTALL_DIR}")"
mv "${SRC_DIR}" "${INSTALL_DIR}"
cd "${INSTALL_DIR}"

# === GENERATE PASSWORDS/KEYS ===
set +o pipefail
DBpassword="$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9'    | head -c 16)"
API_KEY="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9@#^&*' | head -c 20)"
set -o pipefail
: "${DBpassword:?RANDOM FAILED}"; : "${API_KEY:?RANDOM FAILED}"

# === INSTALL OS PACKAGES ===
execute_by_distro

# === MYSQL SETUP ===
systemctl enable --now mysql >/dev/null 2>&1 || systemctl start mysql
systemctl is-active --quiet mysql || die "MYSQL FAILED TO START."
ok "\tMYSQL READY"

read -r -s -p "ENTER ROOT PASSWORD FOR MYSQL (EMPTY IF NONE): " mysql_root_password
echo
if [[ -n "${mysql_root_password}" ]]; then
  MYSQL_ROOT_AUTH=(-u root -p"${mysql_root_password}")
else
  MYSQL_ROOT_AUTH=(-u root)
fi

mysql "${MYSQL_ROOT_AUTH[@]}" <<SQL
CREATE DATABASE IF NOT EXISTS powerdnsweb;
CREATE DATABASE IF NOT EXISTS powerdns;
CREATE USER IF NOT EXISTS 'powerdnsweb'@'localhost' IDENTIFIED WITH mysql_native_password BY '${DBpassword}';
CREATE USER IF NOT EXISTS 'powerdns'@'localhost' IDENTIFIED WITH mysql_native_password BY '${DBpassword}';
GRANT ALL PRIVILEGES ON powerdnsweb.* TO 'powerdnsweb'@'localhost';
GRANT ALL PRIVILEGES ON powerdns.* TO 'powerdns'@'localhost';
FLUSH PRIVILEGES;
SQL

SCHEMA_PATH="$(ls /usr/share/doc/pdns-backend-mysql/schema.mysql.sql* 2>/dev/null | head -n1 || true)"
[[ -z "${SCHEMA_PATH}" ]] && SCHEMA_PATH="$(ls /usr/share/pdns-backend-mysql/schema.mysql.sql* 2>/dev/null | head -n1 || true)"
[[ -n "${SCHEMA_PATH}" ]] || die "POWERDNS MYSQL SCHEMA NOT FOUND."

if [[ "${SCHEMA_PATH}" == *.gz ]]; then
  zcat "${SCHEMA_PATH}" | mysql -u powerdns -p"${DBpassword}" powerdns
else
  mysql -u powerdns -p"${DBpassword}" powerdns < "${SCHEMA_PATH}"
fi

# === APPSETTINGS.JSON (NO DUPLICATE KEYS, NORMALIZED) ===
JSON_FILE="${INSTALL_DIR}/appsettings.json"
PDNS_URL="http://127.0.0.1:8081"
RECURSOR_URL="http://127.0.0.1:8082"

# CREATE MINIMAL FILE IF MISSING
if [[ ! -f "${JSON_FILE}" ]]; then
  cat > "${JSON_FILE}" <<'JSON'
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "MySQLConnection": { "Server": "", "User": "", "Password": "", "Database": "" },
  "pdns": {},
  "recursor": {}
}
JSON
fi

# REWRITE WITHOUT DUPLICATE MIXED-CASE KEYS:
# - REPLACE ENTIRE "pdns" AND "recursor" OBJECTS WITH CANONICAL KEYS ONLY.
jq --arg server "localhost" \
   --arg user "powerdnsweb" \
   --arg password "${DBpassword}" \
   --arg database "powerdnsweb" \
   --arg apikey "${API_KEY}" \
   --arg pdnsUrl "${PDNS_URL}" \
   --arg recUrl "${RECURSOR_URL}" \
   --arg recursor_enabled "${recursor_enabled}" \
   '
   .MySQLConnection.Server   = $server   |
   .MySQLConnection.User     = $user     |
   .MySQLConnection.Password = $password |
   .MySQLConnection.Database = $database |

   .pdns = { "Api_Key": $apikey, "Url": $pdnsUrl } |
   .recursor = { "Api_Key": $apikey, "Url": $recUrl, "Enabled": $recursor_enabled }
   ' "${JSON_FILE}" > "${JSON_FILE}.tmp"
mv "${JSON_FILE}.tmp" "${JSON_FILE}"

chown -R www-data:www-data "${INSTALL_DIR}"
chmod -R u=rwX,go=rX "${INSTALL_DIR}"

# === NGINX VHOST ===
read -r -p "ENTER YOUR SERVER IP/DNS FOR NGINX: " server_name
NGINX_CONF="/etc/nginx/sites-enabled/powerdns-web"
rm -f /etc/nginx/sites-enabled/default || true
[[ -f "${NGINX_CONF}" ]] && mv "${NGINX_CONF}" "${NGINX_CONF}.bak"

cat > "${NGINX_CONF}" <<NGX
server {
  listen 80;
  server_name ${server_name};

  location / {
    proxy_pass         http://127.0.0.1:5000;
    proxy_http_version 1.1;
    proxy_set_header   Host \$host;
    proxy_set_header   X-Real-IP \$remote_addr;
    proxy_set_header   X-Forwarded-For \$proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto \$scheme;
  }
}
NGX

systemctl enable --now nginx
systemctl reload nginx || true

# === POWERDNS (AUTHORITATIVE) CONFIG ===
PDNS_CONFIG="/etc/powerdns/pdns.conf"
PDNS_PORT=53
[[ "${install_recursor_flag}" == "true" ]] && PDNS_PORT=5300

cat > "${PDNS_CONFIG}" <<EOF
launch=gmysql
gmysql-host=127.0.0.1
gmysql-dbname=powerdns
gmysql-user=powerdns
gmysql-password=${DBpassword}
gmysql-dnssec=yes
local-port=${PDNS_PORT}
allow-dnsupdate-from=127.0.0.0/8,::1,10.0.0.0/8,192.168.0.0/16,172.16.0.0/12
api=yes
api-key=${API_KEY}
dnsupdate=yes
include-dir=/etc/powerdns/pdns.d
load-modules=gmysql
security-poll-suffix=
webserver=yes
webserver-address=127.0.0.1
webserver-allow-from=127.0.0.1,::1
webserver-port=8081
EOF
ok "POWERDNS CONFIG WRITTEN TO ${PDNS_CONFIG}"

# === POWERDNS RECURSOR CONFIG ===
if [[ "${install_recursor_flag}" == "true" ]]; then
  RECURSOR_CONFIG="/etc/powerdns/recursor.conf"
  touch /etc/powerdns/recursor.lua

  cat > "${RECURSOR_CONFIG}" <<EOF
forward-zones=
forward-zones-recurse=
api-config-dir=/var/opt/pdns-rec-api
include-dir=/var/opt/pdns-rec-api
allow-from=127.0.0.0/8,10.0.0.0/8,100.64.0.0/10,169.254.0.0/16,192.168.0.0/16,172.16.0.0/12,::1/128,fc00::/7,fe80::/10
api-key=${API_KEY}
config-dir=/etc/powerdns
hint-file=/usr/share/dns/root.hints
local-address=0.0.0.0
local-port=53
lua-config-file=/etc/powerdns/recursor.lua
public-suffix-list-file=/usr/share/publicsuffix/public_suffix_list.dat
quiet=yes
security-poll-suffix=
webserver=yes
webserver-address=127.0.0.1
webserver-allow-from=127.0.0.1,::1
webserver-port=8082
EOF
  ok "RECURSOR CONFIG WRITTEN TO ${RECURSOR_CONFIG}"
fi

systemctl enable --now pdns
[[ "${install_recursor_flag}" == "true" ]] && systemctl enable --now pdns-recursor || true

# === RESOLVE ABSOLUTE DOTNET PATH AND APP DLL ===
DOTNET_BIN="$(command -v dotnet 2>/dev/null || which dotnet 2>/dev/null || true)"
[[ -n "${DOTNET_BIN}" ]] || die "DOTNET NOT FOUND IN PATH. INSTALL .NET 8+ AND RE-RUN."

APP_DLL="${INSTALL_DIR}/PowerDNS_Web.dll"

# === SYSTEMD UNIT ===
cat > /etc/systemd/system/${SERVICE_NAME} <<UNIT
[Unit]
Description=WEB APPLICATION FOR POWERDNS SERVER
After=network.target

[Service]
User=root
WorkingDirectory=${INSTALL_DIR}
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
ExecStart=${DOTNET_BIN} ${APP_DLL}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now "${SERVICE_NAME}"

systemctl is-active --quiet "${SERVICE_NAME}" \
  && ok "\tPOWERDNS-WEB SERVICE INSTALLED AND STARTED\n\tOPEN: http://${server_name}" \
  || die "FAILED TO START SERVICE. CHECK LOGS."
