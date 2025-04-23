function showModal() {
    document.getElementById("addZoneModalLabel").innerText = "Add zone";
    // CLEAR INPUTS
    document.getElementById("modal-domain").value = "";
    document.getElementById("modal-type").value = "Native";
    document.getElementById("modal-mserver").value = "";
    document.getElementById("modal-dnssec").value = "Enabled";

    // HIDE SLAVE DIV
    document.getElementById("slave-div").style.display = "none";

    // TOGGLE BUTTONS
    document.getElementById("update-buttons").style.display = "none";
    document.getElementById("create-buttons").style.display = "block";

    // OPEN MODAL
    var modal = new bootstrap.Modal(document.getElementById("addZoneModal"), { backdrop: false });
    modal.show();
}

function openEditModal(zoneName, zoneType, masterServer, dnssec, serial) {
    var modal = document.getElementById("addZoneModal");

    modal.removeAttribute("aria-hidden");

    document.getElementById("addZoneModalLabel").innerText = "Edit zone";
    let name = document.getElementById("modal-domain");
    let kind = document.getElementById("modal-type");
    let master = document.getElementById("modal-mserver");

    name.value = zoneName;
    kind.value = zoneType;
    master.value = masterServer || "";

    name.readOnly = true;
    kind.disabled = true;

    // DNSSEC select
    var dnssecSelect = document.getElementById("modal-dnssec");
    if (dnssecSelect) {
        dnssecSelect.value = dnssec.toString() === "True" ? "Enabled" : "Disabled";
    }

    ZoneToggle();

    document.getElementById("update-buttons").style.display = "block";
    document.getElementById("create-buttons").style.display = "none";

    var bootstrapModal = new bootstrap.Modal(modal, { backdrop: false });
    bootstrapModal.show();

    setTimeout(() => {
        document.getElementById("modal-domain").focus();
    }, 100);
}

function ZoneToggle() {
    var type = document.getElementById("modal-type").value;
    var slaveDiv = document.getElementById("slave-div");

    if (type === "Slave") {
        slaveDiv.style.display = "block";
    } else {
        slaveDiv.style.display = "none";
    }
}

async function addZone(type) {
    const name = document.getElementById("modal-domain").value.trim();
    const kind = document.getElementById("modal-type").value;
    const master = document.getElementById("modal-type").value === "Slave" ? document.getElementById("modal-mserver").value.trim() : "";
    var dnssec = document.getElementById("modal-dnssec").value;
    const serial = 0;
    var errorMessage;

    if (type === "Forward") {
        errorMessage = validateForwardZoneName(name);
    }
    else if (type === "Reverse") { 
        errorMessage = validateReverseZoneName(name);
    }

    if (errorMessage) {
        showAlert(errorMessage, "alertContainer-modal", "danger");
        return;
    }

    if (kind === "Slave") {
        if (validateIPv4(master)) {
            showAlert(validateIPv4(master), "alertContainer-modal", "danger");
            return;
        }
    }

    if (dnssec === "Enabled") dnssec = true;
    else dnssec = false;

    let zoneData = { Name: name, Kind: kind, Master: master, Dnssec: dnssec, Serial: serial };

    try {
        const response = await fetch(window.location.pathname + "?handler=AddZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify(zoneData)
        });

        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "Failed to add zone");
        }

        showAlert("Zone added successfully!", "alertContainer-modal", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlert(error.message, "alertContainer-modal", "danger");
    }
}

async function save() {
    const name = document.getElementById("modal-domain").value.trim();
    const kind = document.getElementById("modal-type").value;
    const master = document.getElementById("modal-mserver").value.trim();
    const dnssec = document.getElementById("modal-dnssec").value === "Enabled";
    const serial = 0;

    let zoneData = {
        Name: name,
        Kind: kind,
        Masters: kind === "Slave" && master ? [master] : [],
        Dnssec: dnssec,
        Serial: serial
    };

    if (kind === "Slave") {
        const errorMessage = validateIPv4(master);
        if (errorMessage) {
            showAlert(errorMessage, "alertContainer-modal", "danger");
            return;
        }
    }

    try {
        const response = await fetch(window.location.pathname + "?handler=EditZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify(zoneData)
        });

        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "Failed to update zone");
        }

        showAlert("Zone updated successfully!", "alertContainer-modal", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlert(error.message, "alertContainer-modal", "danger");
    }
}

async function dnssecKeys(name) {
    try {
        const response = await fetch(window.location.pathname + "?handler=DnssecKeys&Name=" + encodeURIComponent(name), {
            method: "GET",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            }
        });

        const result = await response.json();

        if (!response.ok || !result.success) {
            throw new Error(result.message || "Failed to get DNSSEC keys");
        }
        const keys = JSON.parse(result.keys);

        // CHECK IF KEYS EXIST
        if (!Array.isArray(keys) || keys.length === 0) {
            throw new Error("No DNSSEC keys found.");
        }
        let keysHtml = "";
        // PARSE DS RECORDS
        keys.forEach((key, index) => {
            keysHtml += `
                <div class="card mb-2">
                    <div class="card-body">
                        <h6 class="card-title">
                            <i class="fa fa-lock"></i> Key ID: ${key.id || "Unknown"} (${key.algorithm || "Unknown"}, ${key.bits || "Unknown"} bits)
                        </h6>
                        <p class="mb-2"><strong>DNSKEY:</strong> <code>${key.dnskey || "No DNSKEY"}</code></p>
                        <p><strong>DS Records:</strong></p>
                        <ul class="list-group">
                            ${Array.isArray(key.ds) && key.ds.length > 0 ? key.ds.map(ds => `
                                <li class="list-group-item justify-content-between align-items-center">
                                    <code>${ds}</code>
                                </li>
                            `).join("") : "<li class='list-group-item text-danger'>No DS records available</li>"}
                        </ul>
                    </div>
                </div>`;
        });

        document.getElementById("dsRecordsContainer").innerHTML = keysHtml;

        // SHOW MODAL
        let modal = new bootstrap.Modal(document.getElementById("dnssecKeysModal"), { backdrop: false });
        modal.show();

    } catch (error) {
        console.error("Error loading DNSSEC keys:", error);
        showAlert(error.message, "alertContainer-keys", "danger");
    }
}

function confirmDelete(zoneName) {
    document.getElementById("confirmMessage").textContent = `Are you sure you want to delete ${zoneName}?`;

    const confirmYes = document.getElementById("confirmYes");
    confirmYes.onclick = function () {
        deleteZone(zoneName);
    };

    document.getElementById("confirmModal").style.display = "block";
}

async function deleteZone(zoneName) {
    try {
        const response = await fetch(window.location.pathname + "?handler=DeleteZone", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify({ Name: zoneName })
        });

        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "Failed to delete zone");
        }

        showAlert("Zone deleted successfully!", "alertContainer-delete", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlert(error.message, "alertContainer-delete", "danger");
    }
}

function hideModal() {
    var modalElement = document.getElementById("addZoneModal");

    if (modalElement) {
        var modalInstance = bootstrap.Modal.getInstance(modalElement);
        if (modalInstance) {
            modalInstance.hide();
        }
    }

    document.querySelectorAll(".modal-backdrop").forEach(el => el.remove());
    document.body.classList.remove("modal-open");
    document.body.style.paddingRight = "";

    modalElement.removeAttribute("aria-hidden");
}

document.addEventListener("hidden.bs.modal", function (event) {
    var modal = event.target;
    modal.removeAttribute("aria-hidden");
});
