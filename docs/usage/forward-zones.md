> **_Page URL:_**  /recursor

## Adding a Forward Zone

1. Select a zone from the **Available Zones** dropdown (taken from Authoritative server).
2. Click **"Add"** to add the zone to the forward list.

**Validation:**
- If no zones are available, the dropdown and button are disabled.
- If an error occurs, an error message is displayed.

## Special Zone: "."

### What is the "." Zone?

The `"."` (dot) zone, also known as the **root forward zone**, is a special configuration in PowerDNS Recursor. It determines the **default forwarding behavior** for all DNS queries that are not explicitly matched by other forward zones.

### How It Works

- If a **specific forward zone** exists (e.g., `example.com → 8.8.8.8`), queries for `example.com` will be forwarded to that DNS server.
- If no specific forward zone matches, queries fall back to the `"."` zone.
- The `"."` zone typically forwards **all other DNS queries** to the defined upstream DNS servers.

### Why is it Important?

- It allows the recursor to **act as a caching resolver** by forwarding queries to external DNS providers (e.g., `8.8.8.8`).
- If configured with `127.0.0.1:5300`, it **sends all queries to the authoritative PowerDNS server**, making it a **local resolver**.

### Editing the "." Zone

- Click **Edit** (📝) next to the `"."` zone.
- Add or remove upstream DNS servers.
- Click **Save Changes** to apply the modifications.

**Notes:**
- The `"."` zone **cannot be deleted**, as it is essential for DNS resolution.
- If `"."` is **not configured**, the recursor will rely on the system's default DNS settings.

### Recommended Usage

- If using an **internal DNS infrastructure**, forward `"."` to `127.0.0.1:5300` to resolve all queries via the authoritative PowerDNS server.
- If acting as a **public DNS resolver**, configure upstream DNS servers like:
  - `8.8.8.8` (Google)
  - `1.1.1.1` (Cloudflare)
  - `9.9.9.9` (Quad9)

### Security Considerations

- Avoid setting `"."` to an **untrusted DNS provider** to prevent **DNS hijacking**.
- Ensure the **correct forwarding rules** to maintain resolution efficiency.



## Editing a Forward Zone

1. Click the **Edit** (📝) button next to a zone.
2. Modify the **Upstream DNS servers**:
   - Click **"Add DNS Server"** to add a new entry.
   - Remove entries by clicking the **Trash (🗑)** icon.
3. Click **"Save Changes"** to apply updates.

**Notes:**
- `127.0.0.1:5300` refers to the **authoritative server**.

## Removing a Forward Zone

1. Click the **Remove** (🗑) button next to a forward zone.
2. A confirmation modal appears.
3. Click **Delete** to confirm, or **Cancel** to abort.
4. If the operation is successful, the zone is removed from the table.

## Recursor Status

- If the **recursor is disabled**, a warning message appears.
- Recursor settings can be adjusted on the **Settings** page.