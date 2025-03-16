#!/bin/bash
colGreen="\033[32m"
colRed="\033[31m"
resetCol="\033[0m"

if [ $(id -u) -ne 0 ]; then
  echo -e "${colRed}This script can be executed only as root, Exiting...${resetCol}"
  exit 1
fi

if [[ " $@ " =~ " -update " ]]; then
    if ! { systemctl list-units --type=service --all 2>/dev/null | grep -q "powerdns-web"; }; then
        echo -e "${colRed}PowerDNS-WEB isn't installed. Run script without arguments, Exiting...${resetCol}"
        exit 0
    fi

    if [[ ! -d /var/www/powerdns-web ]]; then
        echo -e "${colRed}PowerDNS-WEB isn't installed in default directory.\nYou can update program manually, Exiting...${resetCol}"
        exit 0;
    fi
    systemctl stop powerdns-web.service
    sleep 1

    find "powerdns-web" -type f ! -name "appsettings.json" -exec cp --parents {} "/var/www/" \;

    systemctl start powerdns-web.service
    if [ $? -eq 0 ]; then
        echo -e "${colGreen}\tPowerDNS-WEB upgraded!${resetCol}"
    else
        echo -e "$colRed Upgrade error. $resetCol"
        exit 0 
    fi
    exit 0 
fi

check_port_53() {
    if ss -tuln | grep -q ":53 "; then
        echo -e "\033[31mError: Port 53 is already in use. Exiting...\033[0m"
        exit 1
    fi
}

check_port_53

if sudo lsof /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock /var/cache/apt/archives/lock > /dev/null 2>&1; then
    echo -e "\033[31mError: Another package management process is running (dpkg/apt locked). Exiting...\033[0m"
    exit 1
fi

if pgrep -x "unattended-upgrade" > /dev/null || pgrep -x "dpkg" > /dev/null || pgrep -x "apt" > /dev/null; then
    echo -e "\033[31mError: Another package management process (unattended-upgrade, dpkg, or apt) is running. Exiting...\033[0m"
    exit 1
fi

read -p "Do you want to install PowerDNS Recursor? [Yy/Nn]: " install_recursor
case $install_recursor in
    [yY] ) recursor_enabled="Enabled"; install_recursor_flag=true;;
    [nN] ) recursor_enabled="Disabled"; install_recursor_flag=false;;
    * ) echo -e "$colRed Invalid input! Type only Y or N. Exiting...$resetCol"; exit 1;;
esac

execute_by_distro() {
  if [ -f /etc/os-release ]; then
    . /etc/os-release
    DISTRO=$ID
  elif command -v lsb_release > /dev/null 2>&1; then
    DISTRO=$(lsb_release -si)
  elif [ -f /etc/lsb-release ]; then
    . /etc/lsb-release
    DISTRO=$DISTRIB_ID
  elif [ -f /etc/debian_version ]; then
    DISTRO="debian"
  elif [ -f /etc/redhat-release ]; then
    DISTRO="redhat"
  else
    DISTRO="unknown"
  fi

  case "$DISTRO" in
    ubuntu|debian)
      apt -qqq update 
      apt install mysql-server nginx pdns-server pdns-backend-mysql jq -y || { echo -e "${colRed}Error installing packages! Exiting...${resetCol}"; exit 1; }
      if [[ "$install_recursor_flag" == "true" ]]; then
        apt install pdns-recursor -y
      fi
      ;;
    centos|rhel)
      yum update -q -y
      yum install mysql-server nginx pdns-server pdns-backend-mysql jq -y || { echo -e "${colRed}Error installing packages! Exiting...${resetCol}"; exit 1; }
      if [[ "$install_recursor_flag" == "true" ]]; then
        yum install pdns-recursor -y
      fi
      ;;
    fedora)
      dnf update -q -y
      dnf install mysql-server nginx pdns-server pdns-backend-mysql jq -y || { echo -e "${colRed}Error installing packages! Exiting...${resetCol}"; exit 1; }
      if [[ "$install_recursor_flag" == "true" ]]; then
        dnf install pdns-recursor -y
      fi
      ;;
    arch)
      pacman -Syu --noconfirm
      pacman -S mariadb nginx pdns-server pdns-backend-mysql jq --noconfirm || { echo -e "${colRed}Error installing packages! Exiting...${resetCol}"; exit 1; }
      if [[ "$install_recursor_flag" == "true" ]]; then
        pacman -S pdns-recursor --noconfirm
      fi
      ;;
    *)
      echo -e "${colRed}Unknown or unsupported Linux distribution: $DISTRO\nExiting...${resetCol}"
      exit 1
      ;;
  esac
}

while true; do
    echo -n "Will be also installed: .NET SDK 8.0, jq, mysql-server, pdns-backend-mysql, pdns-server and nginx. Ok? [Yy/Nn]: "
    read accept
    case $accept in
        [yY] ) break;;
        [nN] ) echo "Exiting..."; exit 0;;
        * ) echo -e " $colRed Type only Y or N !$resetCol";;
    esac
done

if [[ ! -d /var/www ]]; then
  mkdir -p /var/www
fi
if [[ ! -d /var/opt/pdns-rec-api ]]; then
  mkdir -p /var/opt/pdns-rec-api
fi
if [[ -d /var/www/powerdns-web ]]; then
  echo -e "${colRed}Directory /var/www/powerdns-web already exists! Please remove it manually or use update mode.${resetCol}"
  exit 1
fi

mv powerdns-web /var/www/powerdns-web
cd /var/www/powerdns-web
chmod +x ./dotnet-install.sh
./dotnet-install.sh --channel 8.0
if [ $? -eq 0 ]; then
    echo -e "${colGreen}\tDotNET installed${resetCol}"
else
  echo -e "$colRed Error while installing DotNET. $resetCol"
  exit 0 
fi
sleep 5
mv /root/.dotnet /var/www/powerdns-web/

DBpassword=$(tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 16)
API_KEY=$(tr -dc 'A-Za-z0-9@#^&*' < /dev/urandom | head -c 12)

execute_by_distro

read -p "Enter root password for MySQL: " mysql_root_password

mysql -u root -p"$(printf '%q' "$mysql_root_password")" << eof
CREATE DATABASE powerdnsweb;
CREATE DATABASE powerdns;
CREATE USER 'powerdnsweb'@'localhost' IDENTIFIED WITH mysql_native_password BY '$DBpassword';
CREATE USER 'powerdns'@'localhost' IDENTIFIED WITH mysql_native_password BY '$DBpassword';
GRANT ALL PRIVILEGES ON powerdnsweb.* TO 'powerdnsweb'@'localhost';
GRANT ALL PRIVILEGES ON powerdns.* TO 'powerdns'@'localhost';
FLUSH PRIVILEGES;
eof

JSON_FILE="/var/www/powerdns-web/appsettings.json"

jq --arg server "localhost" \
   --arg user "powerdnsweb" \
   --arg password "$DBpassword" \
   --arg database "powerdnsweb" \
   --arg apikey "$API_KEY" \
   --arg recursor_enabled "$recursor_enabled" \
   '.MySQLConnection.Server = $server | 
    .MySQLConnection.User = $user | 
    .MySQLConnection.Password = $password | 
    .MySQLConnection.Database = $database |
    .pdns.Api_Key = $apikey |
    .recursor.Api_Key = $apikey |
    .recursor.Enabled = $recursor_enabled' \
   "$JSON_FILE" > tmp.$$.json && mv tmp.$$.json "$JSON_FILE"

chown -R www-data:www-data /var/www/powerdns-web
chmod -R 744 /var/www/powerdns-web

read -p "Enter your server IP/DNS for NGINX: " server_name

rm /etc/nginx/sites-enabled/default

NGINX_CONF="/etc/nginx/sites-enabled/powerdns-web"

if [[ -f $NGINX_CONF ]]; then
  mv "$NGINX_CONF" "$NGINX_CONF.bak"
fi

echo -e "server {\n\
server_name $server_name;\n\
  location / {\n\
    proxy_pass http://localhost:5000;\n\
  }\n\
}" > /etc/nginx/sites-enabled/powerdns-web

PDNS_CONFIG="/etc/powerdns/pdns.conf"
PDNS_PORT=53

if [[ "$install_recursor_flag" == "true" ]]; then
  PDNS_PORT=5300
fi

cat << EOF > "$PDNS_CONFIG"
launch=gmysql
gmysql-host=127.0.0.1
gmysql-dbname=powerdns
gmysql-user=powerdns
gmysql-password=$DBpassword
gmysql-dnssec=yes
local-port=$PDNS_PORT
allow-dnsupdate-from=127.0.0.0/8,::1,0.0.0.0/0
api=yes
api-key=$API_KEY
dnsupdate=yes
include-dir=/etc/powerdns/pdns.d
load-modules=gmysql
security-poll-suffix=
webserver=yes
webserver-address=127.0.0.1
webserver-allow-from=127.0.0.1,::1
webserver-port=8081
EOF

mysql -u powerdns -p"$(printf '%q' "$DBpassword")" powerdns < /usr/share/doc/pdns-backend-mysql/schema.mysql.sql

echo -e "${colGreen}PowerDNS configuration written to $PDNS_CONFIG${resetCol}"

if [[ "$install_recursor_flag" == "true" ]]; then
  RECURSOR_CONFIG="/etc/powerdns/recursor.conf"

  cat << EOF > "$RECURSOR_CONFIG"
forward-zones=
forward-zones-recurse=
api-config-dir=/var/opt/pdns-rec-api
include-dir=/var/opt/pdns-rec-api
allow-from=127.0.0.0/8, 10.0.0.0/8, 100.64.0.0/10, 169.254.0.0/16, 192.168.0.0/16, 172.16.0.0/12, ::1/128, fc00::/7, fe80::/10
api-key=$API_KEY
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
EOF

  echo -e "${colGreen}PowerDNS Recursor configuration written to $RECURSOR_CONFIG${resetCol}"
fi

systemctl restart pdns
if [[ "$install_recursor_flag" == "true" ]]; then
  systemctl restart pdns-recursor
fi

if command -v systemctl > /dev/null 2>&1; then
  echo -e "[Unit]\nDescription=WEB application for PowerDNS server\n\
[Service]\n\
User=www-data\n\
WorkingDirectory=/var/www/powerdns-web\n\
ExecStart=/var/www/powerdns-web/.dotnet/dotnet PowerDNS-Web.dll\n\
Restart=always\nRestartSec=5\n\
[Install]\n\
WantedBy=multi-user.target" > /etc/systemd/system/powerdns-web.service

  systemctl daemon-reload
  systemctl reload nginx
  systemctl start powerdns-web.service

  if [ $? -eq 0 ]; then
    systemctl enable powerdns-web.service
    echo -e "${colGreen}\tGreat! powerdns-web service installed and started\n\
\tNow you can go to: http://$server_name!${resetCol}"
  else
    echo -e "$colRed Error starting service. Check logs! $resetCol"
    exit 0 
  fi
fi