async function addSubdomain() {
    const subdomainName = document.getElementById("subdomainInput").value.trim();

    const errorMessage = validateSubdomain(subdomainName);

    if (errorMessage) {
        showAlertSubdomain(errorMessage, "danger");
        return;
    }

    try {
        const response = await fetch(window.location.pathname + "?handler=AddSubdomain", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify({ Subdomain: subdomainName })
        });

        const result = await response.json();

        if (!response.ok || !result.success) {
            throw new Error(result.message || "Failed to add subdomain");
        }

        showAlertSubdomain("Subdomain added successfully!", "success");

        // Закрываем модальное окно через 1 секунду
        setTimeout(() => location.reload(), 1000);

    } catch (error) {
        showAlertSubdomain(error.message, "danger");
    }
}

function showAddSubdomainModal() {
    openModal("addSubdomainModal");
}

// OPEN MODAL
function openModal(modalId) {
    var modalElement = document.getElementById(modalId);
    if (!modalElement) return;

    var modal = new bootstrap.Modal(modalElement, { backdrop: false });
    modal.show();
}

// DISPLAY ALERT
function showAlertSubdomain(message, type) {
    const alertContainer = document.getElementById("alertContainer-subdomain");
    alertContainer.innerHTML = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    </div>`;
}
