// ========================= LOCALIZATION =========================
let M = {};
(function(){ try { const el = document.getElementById('i18n-recursor'); if (el?.textContent) M = JSON.parse(el.textContent); } catch {} })();

// ========================= ANTIFORGERY =========================
function getCsrf() {
    const el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

// ========================= ADD FORWARD ZONE =========================
async function addForwardZone() {
    const selectedZone = document.getElementById("availableZones")?.value;
    if (!selectedZone) {
        showToast('warning', M.Toast_SelectZone || 'Please select a zone.');
        return;
    }

    try {
        const response = await fetch(window.location.pathname + "?handler=AddForwardZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify({ Zone: selectedZone })
        });

        const result = await response.json();
        if (!response.ok || !result.success) throw new Error(result.message || (M.Toast_AddFail || "Failed to add forward zone."));

        showToast('success', result.message || (M.Toast_Added || 'Zone added successfully'));
        setTimeout(() => location.reload(), 600);
    } catch (error) {
        showToast('danger', error.message || (M.Toast_AddFail || 'Unexpected error while adding forward zone'));
    }
}

// ========================= DELETE FLOW =========================
let deleteTarget = null;

function removeForwardZone(name, id) {
    deleteTarget = { name, id };
    const msgTmpl = M.Confirm_DeleteMsg || "Are you sure you want to remove the forward zone: {name}?";
    document.getElementById("confirmMessage").textContent = msgTmpl.replace('{name}', name);
    document.getElementById("confirmModal").style.display = "block";
}

document.getElementById("confirmNo").addEventListener("click", () => {
    document.getElementById("confirmModal").style.display = "none";
    deleteTarget = null;
});

document.getElementById("confirmYes").addEventListener("click", async () => {
    if (!deleteTarget) return;
    document.getElementById("confirmModal").style.display = "none";
    const { name, id } = deleteTarget;
    deleteTarget = null;

    try {
        const response = await fetch(window.location.pathname + "?handler=RemoveForwardZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify({ Id: id, Zone: name })
        });

        const result = await response.json();
        if (!response.ok || !result.success) throw new Error(result.message || (M.Toast_RemoveFail || "Failed to remove forward zone"));

        showToast('success', result.message || (M.Toast_Removed || 'Zone removed successfully'));
        setTimeout(() => location.reload(), 600);
    } catch (error) {
        showToast('danger', error.message || (M.Toast_RemoveFail || 'Unexpected error while removing forward zone'));
    }
});

// ========================= EDIT ZONE =========================
function editZone(name, forwardTo, id) {
    const dnsContainer = document.getElementById("editZoneDNSContainer");
    dnsContainer.innerHTML = "";

    const dnsList = Array.isArray(forwardTo)
        ? forwardTo.map(x => String(x).trim()).filter(Boolean)
        : [];

    if (dnsList.length === 0) dnsList.push("");

    dnsList.forEach((dns, idx) => addDnsField(dns, idx > 0));
    const modal = document.getElementById("editZoneModal");
    document.getElementById("zone_name-modal").textContent = name;
    modal.dataset.zoneId = id || "";

    const transportContainer = document.getElementById("rootTransportContainer");
    const transportSelect = document.getElementById("rootTransport");
    const isRoot = name === ".";
    transportContainer.hidden = !isRoot;
    transportSelect.value = isRoot && dnsList.length > 0 && dnsList.every(x => /:853$/.test(x))
        ? "dot"
        : "direct";

    new bootstrap.Modal(modal, { backdrop: false }).show();
}

function replaceServerPort(value, port) {
    const v = String(value || "").trim();
    if (!v) return v;
    return /:\d+$/.test(v) ? v.replace(/:\d+$/, `:${port}`) : `${v}:${port}`;
}

function applyRootTransportPort() {
    if (document.getElementById("zone_name-modal").textContent !== ".") return;
    const port = document.getElementById("rootTransport").value === "dot" ? 853 : 53;
    document.querySelectorAll("#editZoneDNSContainer input").forEach(input => {
        input.value = replaceServerPort(input.value, port);
    });
}

// ========================= ADD DNS FIELD ROW =========================
function addDnsField(value = "", showRemoveButton = true) {
    const dnsContainer = document.getElementById("editZoneDNSContainer");

    const row = document.createElement("div");
    row.className = "input-group mb-2";

    const input = document.createElement("input");
    input.type = "text";
    input.className = "form-control";
    input.placeholder = M.Placeholder_IpPort || "IP:Port (e.g., 8.8.8.8:53)";
    input.value = value;

    row.appendChild(input);

    if (showRemoveButton) {
        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "btn btn-danger";
        removeButton.innerHTML = '<i class="fa fa-trash"></i>';
        removeButton.addEventListener("click", () => {
            dnsContainer.removeChild(row);
            updateRemoveButtons();
        });
        row.appendChild(removeButton);
    }

    dnsContainer.appendChild(row);
    updateRemoveButtons();
}

// ========================= TOGGLE REMOVE BUTTONS =========================
function updateRemoveButtons() {
    const rows = document.querySelectorAll("#editZoneDNSContainer .input-group");
    const removeButtons = document.querySelectorAll("#editZoneDNSContainer .btn-danger");
    if (rows.length <= 1) removeButtons.forEach(b => b.style.display = "none");
    else removeButtons.forEach(b => b.style.display = "inline-block");
}

// ========================= SAVE CHANGES =========================
async function saveRecord() {
    const inputs = document.querySelectorAll("#editZoneDNSContainer input");
    const dnsServers = Array.from(inputs)
        .map(i => i.value.trim())
        .filter(Boolean);

    const modalElement = document.getElementById("editZoneModal");
    const name = document.getElementById("zone_name-modal").textContent;
    const id = modalElement.dataset.zoneId || "";
    const transport = name === "." ? document.getElementById("rootTransport").value : "";

    if (dnsServers.length === 0) {
        showToast('warning', M.Toast_NoDns || 'At least one DNS server must be specified.');
        return;
    }

    for (let i = 0; i < dnsServers.length; i++) {
        const err = validateIPv4withPort(dnsServers[i]);
        if (err) {
            const tmpl = M.Toast_DnsIdx || "Error in DNS {n}: {err}";
            showToast('warning', tmpl.replace('{n}', (i + 1)).replace('{err}', err));
            return;
        }
    }

    try {
        const response = await fetch(window.location.pathname + "?handler=editZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": getCsrf()
            },
            body: JSON.stringify({ Id: id, Name: name, DnsServers: dnsServers.join(","), Transport: transport })
        });

        const result = await response.json();
        if (!response.ok || !result.success) throw new Error(result.message || (M.Toast_SaveFail || "Failed to save DNS servers"));

        showToast('success', result.message || (M.Toast_SaveOk || 'DNS servers updated successfully'));
        const modal = bootstrap.Modal.getInstance(modalElement);
        modal?.hide();
        setTimeout(() => location.reload(), 600);

    } catch (error) {
        showToast('danger', error.message || (M.Toast_SaveFail || 'Unexpected error while saving DNS servers'));
    }
}

document.querySelectorAll(".js-edit-zone").forEach(button => {
    button.addEventListener("click", () => {
        let forwardTo = [];
        try { forwardTo = JSON.parse(button.dataset.forwardTo || "[]"); } catch { }
        editZone(button.dataset.zoneName || "", forwardTo, button.dataset.zoneId || "");
    });
});

document.getElementById("rootTransport").addEventListener("change", applyRootTransportPort);

document.querySelectorAll(".js-remove-zone").forEach(button => {
    button.addEventListener("click", () => {
        removeForwardZone(button.dataset.zoneName || "", button.dataset.zoneId || "");
    });
});
