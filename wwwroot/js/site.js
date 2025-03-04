// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// MASK FOR DATES
$(document).ready(function () {
    $(".datetime-input").inputmask("99.99.9999 99:99", { placeholder: "__.__.____ __:__" });
});

$(document).ready(function () {
    $(".2-dates-input").inputmask("99.99.9999 - 99.99.9999", { placeholder: "__.__.____ - __.__.____" });
});

// NOTIFICATION
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