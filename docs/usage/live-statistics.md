> **_Page URL:_**  /

## Active Port Display

- Displays the active port of PowerDNS.
- If the recursor is enabled, the port is `5300`; otherwise, it's `53`.

## Uptime Display

- Shows the uptime of the PowerDNS services.
- If the recursor is enabled, displays its uptime separately.

---

# Statistics

## Query Log Updates

- The lists update dynamically every **5 seconds**.

---

## Authoritative Statistics

### Total Queries

- Displays the total number of queries handled by PowerDNS.

### Queries for Missing Types

- Shows a list of queries for record types that do not exist.
- Data is fetched dynamically.

### Queries for Nonexistent Records

- Displays a list of queries for records that do not exist in the DNS zones.

### UDP Queries Received

- Lists all UDP queries that have been processed.

---

## Recursor Statistics (If Enabled)

### Recursor Cache Hits

- Displays the number of successful cache lookups.

### Recursor Cache Misses

- Displays the number of times queries were not found in the cache.

### Top Requests

- Lists the most frequently requested domain names.

### Top Remotes

- Displays the most active remote clients making queries.