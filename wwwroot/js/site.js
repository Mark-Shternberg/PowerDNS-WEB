// MASK FOR IPv4
$(document).ready(function () {
    $(".ip-input").inputmask("999.999.999.999", { placeholder: "___.___.___.___" });
});

// ------------ NOTIFICATION ------------ //

// SHOW POPUP NOTIFICATION
function showNotification(message, type) {
    var notification = document.getElementById("notification");
    var messageSpan = document.getElementById("notification-message");

    messageSpan.innerText = message;

    notification.classList.remove("notification-success", "notification-error");

    if (type === 1) {
        notification.classList.add("notification-success");
    } else if (type === 2) {
        notification.classList.add("notification-error");
    }

    notification.classList.add("show");

    // Hide in 3s
    setTimeout(function () {
        notification.classList.remove("show");
    }, 3000);
}

// SHOW DIV NOTIFICATION
function showDivNotification(message, type) {
    var notification = document.getElementById("notification-div");
    //var messageSpan = document.getElementById("notification-message");

    notification.innerText = message;

    notification.classList.remove("notification-success", "notification-error");

    if (type === 1) {
        notification.classList.add("notification-success");
    } else if (type === 2) {
        notification.classList.add("notification-error");
    }

    notification.classList.remove('d-none');
    notification.classList.add("show");

    // HIDE NOTIFICATION AFTER 3 SECONDS
    setTimeout(function () {
        notification.classList.remove("show");
        notification.classList.add('d-none');
    }, 3000);
}

// SHOW ALERT NOTIFICATION
function showAlert(message, id, type) {
    const alertContainer = document.getElementById(id);
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        </div>`;
}

// ------------ ------- ------------ //

function parseDate(dateString) {
    let regex = /^(\d{2})\.(\d{2})\.(\d{4}) (\d{2}):(\d{2})$/;
    let match = dateString.match(regex);

    if (!match) {
        return null;
    }

    let day = parseInt(match[1], 10);
    let month = parseInt(match[2], 10);
    let year = parseInt(match[3], 10);
    let hours = parseInt(match[4], 10);
    let minutes = parseInt(match[5], 10);

    let date = new Date(year, month - 1, day, hours, minutes);

    if (
        date.getFullYear() !== year ||
        date.getMonth() + 1 !== month ||
        date.getDate() !== day ||
        date.getHours() !== hours ||
        date.getMinutes() !== minutes
    ) {
        return null;
    }

    return date;
}

function getCurrentDateTime() {
    const now = new Date();

    const day = String(now.getDate()).padStart(2, '0');
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const year = now.getFullYear();

    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');

    return `${day}.${month}.${year} ${hours}:${minutes}`;
}

// ------------ MODAL ------------ //
// CANCEL CONFIRM
document.getElementById("confirmNo").addEventListener("click", function () {
    document.getElementById("confirmModal").style.display = "none";
});

// CLOSE MODAL
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