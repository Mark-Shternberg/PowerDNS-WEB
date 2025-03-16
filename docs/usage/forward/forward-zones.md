> **_Page URL:_**  /recursor

## Adding a Forward Zone

1. Select a zone from the **Available Zones** dropdown (taken from Authoritative server).
2. Click **"Add"** to add the zone to the forward list.

**Validation:**
- If no zones are available, the dropdown and button are disabled.
- If an error occurs, an error message is displayed.

---

## Editing a Forward Zone

1. Click the **Edit** (📝) button next to a zone.
2. Modify the **Upstream DNS servers**:
   - Click **"Add DNS Server"** to add a new entry.
   - Remove entries by clicking the **Trash (🗑)** icon.
3. Click **"Save Changes"** to apply updates.

**Notes:**
- `127.0.0.1:5300` refers to the **authoritative server**.

---

## Removing a Forward Zone

1. Click the **Remove** (🗑) button next to a forward zone.
2. A confirmation modal appears.
3. Click **Delete** to confirm, or **Cancel** to abort.
4. If the operation is successful, the zone is removed from the table.

---

## Recursor Status

- If the **recursor is disabled**, a warning message appears.
- Recursor settings can be adjusted on the **Settings** page.