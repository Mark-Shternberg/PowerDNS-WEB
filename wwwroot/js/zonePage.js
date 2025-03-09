function showAlert(message, type) {
    const alertContainer = document.getElementById("alertContainer");
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        </div>`;
}
function showAlertEdit(message, type) {
    const alertContainer = document.getElementById("alertContainer-edit");
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        </div>`;
}

function toggleFields() {
    var recordType = document.getElementById("recordType").value;

    document.getElementById("valueField").classList.remove("d-none");
    document.getElementById("mxPriorityField").classList.add("d-none");
    document.getElementById("srvPriorityField").classList.add("d-none");
    document.getElementById("srvWeightField").classList.add("d-none");
    document.getElementById("srvPortField").classList.add("d-none");

    if (recordType === "MX") {
        document.getElementById("mxPriorityField").classList.remove("d-none");
    } else if (recordType === "SRV") {
        document.getElementById("srvPriorityField").classList.remove("d-none");
        document.getElementById("srvWeightField").classList.remove("d-none");
        document.getElementById("srvPortField").classList.remove("d-none");
    }
}

function toggleZone(zone) {
    var rows = document.getElementsByClassName(zone);
    var icon = document.getElementById("icon-" + zone);
    for (var i = 0; i < rows.length; i++) {
        rows[i].style.display = rows[i].style.display === "none" ? "table-row" : "none";
    }
    icon.classList.toggle("fa-chevron-right");
    icon.classList.toggle("fa-chevron-down");
}

function closeModal(modalId) {
    var modal = document.querySelector(modalId);
    var modalBackdrop = document.querySelector('.modal-backdrop');

    if (modal) {
        var bootstrapModal = bootstrap.Modal.getInstance(modal);
        if (bootstrapModal) {
            bootstrapModal.hide();
        }
    }

    if (modalBackdrop) {
        modalBackdrop.remove();
    }

    document.body.classList.remove('modal-open');
    document.body.style.paddingRight = '';
}

let deleteTarget = null;

function toggleEditFields() {
    const type = document.getElementById("editRecordType").value;

    document.getElementById("editMxPriorityField").classList.add("d-none");
    document.getElementById("editSrvPriorityField").classList.add("d-none");
    document.getElementById("editSrvWeightField").classList.add("d-none");
    document.getElementById("editSrvPortField").classList.add("d-none");

    if (type === "MX") {
        document.getElementById("editMxPriorityField").classList.remove("d-none");
    } else if (type === "SRV") {
        document.getElementById("editSrvPriorityField").classList.remove("d-none");
        document.getElementById("editSrvWeightField").classList.remove("d-none");
        document.getElementById("editSrvPortField").classList.remove("d-none");
    }
}

