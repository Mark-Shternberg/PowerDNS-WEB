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

    // Убираем aria-hidden
    modal.removeAttribute("aria-hidden");

    // Заполняем поля модального окна
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

    // Показываем slave-div, если выбрана зона Slave
    ZoneToggle();

    // Переключаем кнопки
    document.getElementById("update-buttons").style.display = "block";
    document.getElementById("create-buttons").style.display = "none";

    // Открываем модальное окно
    var bootstrapModal = new bootstrap.Modal(modal, { backdrop: false });
    bootstrapModal.show();

    // Переводим фокус на поле ввода, чтобы избежать ошибки aria-hidden
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

async function addZone() {
    const name = document.getElementById("modal-domain").value.trim();
    const kind = document.getElementById("modal-type").value;
    const master = document.getElementById("modal-type").value === "Slave" ? document.getElementById("modal-mserver").value.trim() : "";
    const dnssec = document.getElementById("modal-dnssec").value === "Enabled";
    const serial = 0;

    let zoneData = { Name: name, Kind: kind, Master: master, Dnssec: dnssec, Serial: serial };

    const errorMessage = validateZoneName(name);

    if (errorMessage) {
        showAlertModal(errorMessage, "danger");
        return;
    }

    if (kind === "Slave") {
        if (validateIPv4(master)) {
            showAlertModal(validateIP(master), "danger");
            return;
        }
    }

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

        showAlertModal("Zone added successfully!", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlertModal(error.message, "danger");
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

    const errorMessage = validateIP(master);
    if (errorMessage) {
        showAlertModal(errorMessage, "danger");
        return;
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

        showAlertModal("Zone updated successfully!", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlertModal(error.message, "danger");
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

        showAlertDelete("Zone deleted successfully!", "success");
        setTimeout(() => location.reload(), 1500);
    } catch (error) {
        showAlertDelete(error.message, "danger");
    }
}

// CANCEL DELETE
document.getElementById("confirmNo").addEventListener("click", function () {
    document.getElementById("confirmModal").style.display = "none";
});

function showAlertDelete(message, type) {
    const alertContainer = document.getElementById("alertContainer-delete");
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    </div>`;
}

function showAlertModal(message, type) {
    const alertContainer = document.getElementById("alertContainer-modal");
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    </div>`;
}

function hideModal() {
    var modalElement = document.getElementById("addZoneModal");

    if (modalElement) {
        var modalInstance = bootstrap.Modal.getInstance(modalElement);
        if (modalInstance) {
            modalInstance.hide();
        }
    }

    // Удаляем backdrop и aria-hidden
    document.querySelectorAll(".modal-backdrop").forEach(el => el.remove());
    document.body.classList.remove("modal-open");
    document.body.style.paddingRight = "";

    modalElement.removeAttribute("aria-hidden");
}

document.addEventListener("hidden.bs.modal", function (event) {
    var modal = event.target;
    modal.removeAttribute("aria-hidden");
});
