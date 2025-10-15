// =====================================
// ZONES PAGE LOGIC (ADD / EDIT / DELETE)
// ALL COMMENTS IN ENGLISH (UPPERCASE)
// REQUIRES: validation.js + site.js (showToast)
// =====================================

// ===== CSRF =====
function getCsrf() {
    const el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

// ===== UI TOGGLES =====
function ZoneToggle() {
    const t = document.getElementById("modal-type")?.value;
    const slaveDiv = document.getElementById("slave-div");
    if (!slaveDiv) return;
    if (t === "Slave") slaveDiv.style.display = "block";
    else slaveDiv.style.display = "none";
}

// ===== OPEN "ADD" MODAL =====
function showModal(type) {
    // RESET FIELDS
    const nameEl = document.getElementById("modal-domain");
    const revEl = document.getElementById("modal-reverse");
    const typeEl = document.getElementById("modal-type");
    const mservEl = document.getElementById("modal-mserver");
    const dnssecEl = document.getElementById("modal-dnssec");
    const titleEl = document.getElementById("addZoneModalLabel");
    const modalEl = document.getElementById("addZoneModal");

    if (!modalEl) return;

    // DEFAULTS
    if (nameEl) { nameEl.value = ""; nameEl.readOnly = false; }
    if (revEl) { revEl.value = ""; }
    if (typeEl) { typeEl.value = "Native"; typeEl.disabled = false; }
    if (mservEl) { mservEl.value = ""; }
    if (dnssecEl) { dnssecEl.value = "Enabled"; }

    // FORWARD / REVERSE VISIBILITY
    if (type === "Forward") {
        titleEl && (titleEl.innerText = "Add forward zone");
        revEl?.classList.add("d-none");
        nameEl?.classList.remove("d-none");
    } else {
        titleEl && (titleEl.innerText = "Add reverse zone");
        nameEl?.classList.add("d-none");
        revEl?.classList.remove("d-none");
    }

    // MAKE MODE EXPLICIT (AVOID RELYING ON TITLE TEXT LATER)
    modalEl.dataset.mode = type; // "Forward" | "Reverse" | "Edit"

    // BUTTON GROUPS
    document.getElementById("update-buttons").style.display = "none";
    document.getElementById("create-buttons").style.display = "block";

    // SLAVE TOGGLE
    ZoneToggle();

    // OPEN WITHOUT BACKDROP
    const bs = new bootstrap.Modal(modalEl, { backdrop: false });
    bs.show();
}

// ===== OPEN "EDIT" MODAL =====
function openEditModal(zoneName, zoneType, masterServer, dnssec, serial) {
    const modalEl = document.getElementById("addZoneModal");
    const titleEl = document.getElementById("addZoneModalLabel");
    const nameEl = document.getElementById("modal-domain");
    const typeEl = document.getElementById("modal-type");
    const mservEl = document.getElementById("modal-mserver");
    const dnssecEl = document.getElementById("modal-dnssec");

    if (!modalEl || !nameEl || !typeEl || !dnssecEl) return;

    // SET VALUES
    titleEl && (titleEl.innerText = "Edit zone");
    nameEl.value = zoneName;
    typeEl.value = zoneType || "Native";
    mservEl && (mservEl.value = masterServer || "");
    dnssecEl.value = (String(dnssec) === "True" || String(dnssec).toLowerCase() === "true") ? "Enabled" : "Disabled";

    // LOCK FIELDS THAT MUST NOT BE CHANGED
    nameEl.readOnly = true;
    typeEl.disabled = true;

    // MODE
    modalEl.dataset.mode = "Edit";

    // BUTTON GROUPS
    document.getElementById("update-buttons").style.display = "block";
    document.getElementById("create-buttons").style.display = "none";

    ZoneToggle();

    new bootstrap.Modal(modalEl, { backdrop: false }).show();

    // FOCUS FOR UX
    setTimeout(() => { nameEl.focus(); }, 100);
}

// ===== VALID HELPERS (RELY ON validation.js) =====
function mustValidForwardName(name) {
    const err = validateForwardZoneName(name);
    if (err) { showToast('warning', err); return false; }
    return true;
}
function mustValidReversePrefix(prefix) {
    const err = validateReverseZoneName(prefix);
    if (err) { showToast('warning', err); return false; }
    return true;
}
function mustValidMasterIP(ip) {
    const err = validateIPv4(ip);
    if (err) { showToast('warning', err); return false; }
    return true;
}

// ===== ADD ZONE =====
async function addZone() {
    try {
        const modalEl = document.getElementById("addZoneModal");
        const mode = modalEl?.dataset?.mode || (document.getElementById("addZoneModalLabel")?.innerText || "Add forward zone");
        const kind = document.getElementById("modal-type")?.value || "Native";
        const dnssecUi = document.getElementById("modal-dnssec")?.value || "Enabled";
        const isSlave = (kind === "Slave");
        const master = isSlave ? (document.getElementById("modal-mserver")?.value || "").trim() : "";

        let name = "";
        if (String(mode).includes("reverse") || String(mode) === "Reverse") {
            const pref = (document.getElementById("modal-reverse")?.value || "").trim();
            if (!mustValidReversePrefix(pref)) return;
            name = pref; // BACKEND WILL DECIDE HOW TO FORM THE FULL REVERSE ZONE NAME
        } else {
            name = (document.getElementById("modal-domain")?.value || "").trim();
            if (!mustValidForwardName(name)) return;
        }

        if (isSlave && !mustValidMasterIP(master)) return;

        const payload = {
            Name: name,
            Kind: kind,
            Master: master,   // YOUR BACKEND EXPECTS "Master" ON ADD (KEEPING YOUR CURRENT CONTRACT)
            Dnssec: (dnssecUi === "Enabled"),
            Serial: 0
        };

        const resp = await fetch(window.location.pathname + "?handler=AddZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify(payload)
        });

        const result = await resp.json();
        if (!resp.ok || !result?.success) {
            throw new Error(result?.message || "Failed to add zone");
        }

        showToast('success', result?.message || "Zone added successfully");
        // CLOSE AND RELOAD
        hideModal();
        setTimeout(() => location.reload(), 600);

    } catch (e) {
        console.error(e);
        showToast('danger', e.message || "Unexpected error while adding zone");
    }
}

// ===== SAVE EDIT =====
async function save() {
    try {
        const name = (document.getElementById("modal-domain")?.value || "").trim();
        const kind = document.getElementById("modal-type")?.value || "Native";
        const isSlave = (kind === "Slave");
        const master = (document.getElementById("modal-mserver")?.value || "").trim();
        const dnssec = (document.getElementById("modal-dnssec")?.value === "Enabled");
        const serial = 0;

        if (!name || mustValidForwardName(name) === false) return;
        if (isSlave && !mustValidMasterIP(master)) return;

        const payload = {
            Name: name,
            Kind: kind,
            Masters: (isSlave && master) ? [master] : [], // YOUR CURRENT EDIT CONTRACT USES "Masters"
            Dnssec: dnssec,
            Serial: serial
        };

        const resp = await fetch(window.location.pathname + "?handler=EditZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify(payload)
        });

        const result = await resp.json();
        if (!resp.ok || !result?.success) {
            throw new Error(result?.message || "Failed to update zone");
        }

        showToast('success', result?.message || "Zone updated successfully");
        hideModal();
        setTimeout(() => location.reload(), 600);

    } catch (e) {
        console.error(e);
        showToast('danger', e.message || "Unexpected error while updating zone");
    }
}

// ===== DNSSEC KEYS =====
async function dnssecKeys(name) {
    try {
        const modal = new bootstrap.Modal(document.getElementById("dnssecKeysModal"), { backdrop: false });
        modal.show();

        const container = document.getElementById("dsRecordsContainer");
        if (container) container.innerHTML = `<div class="text-muted">Loading…</div>`;

        const resp = await fetch(window.location.pathname + "?handler=DnssecKeys&Name=" + encodeURIComponent(name), {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            }
        });
        const result = await resp.json();
        if (!resp.ok || !result?.success) throw new Error(result?.message || "Failed to get DNSSEC keys");

        // result.keys CAN BE A STRINGIFIED JSON OR AN ARRAY; SUPPORT BOTH
        const keys = Array.isArray(result.keys) ? result.keys
            : (typeof result.keys === "string" ? JSON.parse(result.keys) : []);

        // EXTRACT DS RECORDS IF YOU NEED ONLY DS, OR RENDER FULL
        const cont = document.getElementById("dsRecordsContainer");
        if (!cont) return;

        if (!Array.isArray(keys) || keys.length === 0) {
            cont.innerHTML = `<div class="alert alert-info mb-0">No DNSSEC keys found for <strong>${name}</strong>.</div>`;
            return;
        }

        // RENDER EACH KEY (DNSKEY + DS)
        cont.innerHTML = "";
        keys.forEach((key) => {
            const card = document.createElement('div');
            card.className = 'card mb-2';

            const body = document.createElement('div');
            body.className = 'card-body';

            const title = document.createElement('h6');
            title.className = 'card-title';
            title.innerHTML = `<i class="fa fa-lock me-1"></i> Key ID: ${key.id ?? "Unknown"} (${key.algorithm ?? "Unknown"}, ${key.bits ?? "?"} bits)`;

            const dnskeyP = document.createElement('p');
            dnskeyP.className = 'mb-2';
            dnskeyP.innerHTML = `<strong>DNSKEY:</strong> <code class="text-break">${key.dnskey ?? "No DNSKEY"}</code>`;

            // DS LIST
            const dsTitle = document.createElement('p');
            dsTitle.className = 'mb-1';
            dsTitle.innerHTML = `<strong>DS Records:</strong>`;

            const list = document.createElement('div');
            list.className = 'vstack gap-2';
            const dsArr = Array.isArray(key.ds) ? key.ds : [];

            if (dsArr.length === 0) {
                list.innerHTML = `<div class="text-danger">No DS records available</div>`;
            } else {
                dsArr.forEach(ds => {
                    const row = document.createElement('div');
                    row.className = 'd-flex align-items-center justify-content-between border rounded p-2 gap-2';

                    const code = document.createElement('code');
                    code.className = 'text-break';
                    code.textContent = ds;

                    const btn = document.createElement('button');
                    btn.className = 'btn btn-sm btn-outline-secondary';
                    btn.innerHTML = '<i class="fa fa-copy me-1"></i> Copy';
                    btn.addEventListener('click', async () => {
                        try { await navigator.clipboard.writeText(ds); showToast('success', 'Copied to clipboard'); }
                        catch { showToast('warning', 'Copy failed'); }
                    });

                    row.appendChild(code);
                    row.appendChild(btn);
                    list.appendChild(row);
                });
            }

            body.appendChild(title);
            body.appendChild(dnskeyP);
            body.appendChild(dsTitle);
            body.appendChild(list);
            card.appendChild(body);
            cont.appendChild(card);
        });

    } catch (e) {
        console.error(e);
        showToast('danger', e.message || "Error loading DNSSEC keys");
    }
}

// ===== DELETE FLOW =====
function confirmDelete(zoneName) {
    const modal = document.getElementById("confirmModal");
    const msg = document.getElementById("confirmMessage");
    const noBtn = document.getElementById("confirmNo");
    const yesBtn = document.getElementById("confirmYes");
    if (!modal || !msg || !noBtn || !yesBtn) return;

    msg.textContent = `Are you sure you want to delete "${zoneName}"?`;
    modal.style.display = "block";

    // REBIND YES SAFELY (CLONE NODE TO DROP OLD HANDLERS)
    const freshYes = yesBtn.cloneNode(true);
    yesBtn.parentNode.replaceChild(freshYes, yesBtn);

    noBtn.onclick = () => { modal.style.display = "none"; };
    freshYes.onclick = () => deleteZone(zoneName);
}

async function deleteZone(zoneName) {
    try {
        const resp = await fetch(window.location.pathname + "?handler=DeleteZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify({ Name: zoneName })
        });
        const result = await resp.json();
        if (!resp.ok || !result?.success) throw new Error(result?.message || "Failed to delete zone");

        // CLOSE CONFIRM + TOAST
        document.getElementById("confirmModal").style.display = "none";
        showToast('success', result?.message || "Zone deleted successfully");
        setTimeout(() => location.reload(), 600);

    } catch (e) {
        console.error(e);
        showToast('danger', e.message || "Error deleting zone");
    }
}

// ===== CLOSE MODAL (NO BACKDROP) =====
function hideModal() {
    const modalElement = document.getElementById("addZoneModal");
    if (modalElement) {
        const inst = bootstrap.Modal.getInstance(modalElement);
        if (inst) inst.hide();
    }
    // CLEANUP
    document.querySelectorAll(".modal-backdrop").forEach(el => el.remove());
    document.body.classList.remove("modal-open");
    document.body.style.paddingRight = "";
    modalElement?.removeAttribute("aria-hidden");
}

// ===== GLOBAL ESCAPE FOR CUSTOM CONFIRM (OPTIONAL) =====
document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
        const cm = document.getElementById("confirmModal");
        if (cm && cm.style.display === "block") cm.style.display = "none";
    }
});

// ===== EXPORTS =====
window.ZoneToggle = ZoneToggle;
window.showModal = showModal;
window.openEditModal = openEditModal;
window.addZone = addZone;
window.save = save;
window.dnssecKeys = dnssecKeys;
window.confirmDelete = confirmDelete;
window.deleteZone = deleteZone;
window.hideModal = hideModal;
