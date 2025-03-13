#!/bin/bash

# CONFIG FILE
CONFIG_FILE="/var/www/powerdnsweb/appsettings.json"

ask() {
    local prompt="$1"
    local default="$2"
    local var

    read -p "$prompt [$default]: " var
    echo "${var:-$default}"
}

# READ
CURRENT_SERVER=$(jq -r '.MySQLConnection.Server' "$CONFIG_FILE")
CURRENT_USER=$(jq -r '.MySQLConnection.User' "$CONFIG_FILE")
CURRENT_PASSWORD=$(jq -r '.MySQLConnection.Password' "$CONFIG_FILE")
CURRENT_DATABASE=$(jq -r '.MySQLConnection.Database' "$CONFIG_FILE")

# REQUEST
NEW_SERVER=$(ask "Enter MySQL server" "$CURRENT_SERVER")
NEW_USER=$(ask "Enter MySQL user" "$CURRENT_USER")
NEW_PASSWORD=$(ask "Enter MySQL password" "$CURRENT_PASSWORD")
NEW_DATABASE=$(ask "Enter MySQL database name" "$CURRENT_DATABASE")

# UPDATE JSON
jq --arg server "$NEW_SERVER" \
   --arg user "$NEW_USER" \
   --arg password "$NEW_PASSWORD" \
   --arg database "$NEW_DATABASE" \
   '.MySQLConnection.Server = $server |
    .MySQLConnection.User = $user |
    .MySQLConnection.Password = $password |
    .MySQLConnection.Database = $database' "$CONFIG_FILE" > /tmp/appsettings.json

# REWRITE FILE
mv /tmp/appsettings.json "$CONFIG_FILE"

echo "Settings updated!"
