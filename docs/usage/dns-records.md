> **_Page URL:_**  /zone/[domain.com.]

## Adding a Record

1. Select a **Subdomain** from the dropdown or click **"+"** to add a new subdomain.
2. Choose a **Record Type** from the dropdown (`A`, `AAAA`, `CNAME`, `TXT`, `MX`, `SRV`, `NS`, `HTTPS`).
3. Enter the **Value** based on the selected type.
4. If applicable, fill in additional fields:
   - **MX**: Requires `Priority`.
   - **SRV**: Requires `Priority`, `Weight`, and `Port`.
   - **NS**: Requires `NS Target`.
   - **HTTPS**: Requires `HTTPS Parameters`.
5. Click **"Add"** to save the record.

**Validation:**
- Invalid inputs will trigger an error message.
- Special formats are enforced for `TXT`, `CNAME`, `SRV`, and `MX` records.

## Editing a Record

1. Click the **Edit** (📝) button next to the record.
2. Modify the value and, if applicable, additional fields like priority.
3. Click **Save Changes** to apply updates.

**Notes:**
- `SOA` records require specific formatting for primary NS, admin email, and time values.
- `MX` and `SRV` records will be split into priority and target components.

## Deleting a Record

1. Click the **Delete** (🗑) button next to a record.
2. A confirmation modal appears.
3. Click **Delete** to confirm, or **Cancel** to abort.
4. If the operation is successful, the record is removed from the table.

## Viewing Records

- Records are grouped by **Subdomain**.
- Clicking a subdomain row expands/collapses its records.
- Root domain records are labeled as `(Root)`.

## Managing Subdomains

1. Click **"+"** next to the **Subdomain** dropdown.
2. Enter a new subdomain name.
3. Click **"Add"** to create the subdomain.

## Record Validation

- **A / AAAA**: Must be valid IPv4 or IPv6 addresses.
- **CNAME / NS**: Must follow domain name syntax.
- **TXT**: Must be enclosed in double quotes (`"example text"`).
- **MX**: Requires a priority number (`10 mail.example.com`).
- **SRV**: Requires a specific format (`priority weight port target`).
