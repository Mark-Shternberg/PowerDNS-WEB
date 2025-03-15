> **_Page URL:_**  /zones

## Adding a New Zone

1. Click the **"Add new zone"** button.
2. Fill in the required fields:
   - **Zone Name (domain)**: Example: `example.com`
   - **Zone Type**:
     - `Native`
     - `Master`
     - `Slave` (requires Master Server input)
   - **DNSSEC**: `Enabled` or `Disabled`
3. Click the **"Add"** button to create the zone.

**Validation:**
- If any required field is empty, an error message will appear.
- If a duplicate zone exists, an error notification will be displayed.

## Editing a Zone

1. Click the **"Edit"** (✏️) button next to the desired zone.
2. Modify the zone's properties:
   - **Master Server** (if applicable)
   - **DNSSEC status**
3. Click **"Save"** to apply changes.

**Notes:**
- Only certain attributes can be changed.
- A confirmation message will be displayed upon success.

## Deleting a Zone

1. Click the **"Delete"** (🗑) button next to the zone.
2. A confirmation modal appears.
3. Click **"Delete"** to confirm, or **"Cancel"** to abort.
4. If the operation is successful, the zone is removed from the table.

## DNSSEC Keys

1. If DNSSEC is enabled, a **"Keys"** button is available.
2. Click **"Keys"** to open the **DNSSEC Keys Modal**.
3. The modal displays **DS records** that must be provided to the domain registrar.

## Search Functionality

- Use the search bar to filter zones by domain name.
- The **"Clear search"** button appears when text is entered.
- Clicking **"Clear search"** resets the table view.
