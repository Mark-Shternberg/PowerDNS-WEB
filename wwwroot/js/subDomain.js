// =====================================
// SUBDOMAIN DIALOG
// =====================================

function showAddSubdomainModal() {
    const el = document.getElementById("addSubdomainModal");
    if (!el) return;
    new bootstrap.Modal(el, { backdrop: false }).show();
}

async function addSubdomain() {
    const subdomainName = (document.getElementById("subdomainInput")?.value || "").trim();
    const err = validateSubdomain(subdomainName);
    if (err) { showToast('warning', err); return; }

    try {
        const resp = await fetch(window.location.pathname + "?handler=AddSubdomain", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify({ Subdomain: subdomainName })
        });
        const result = await resp.json();
        if (!resp.ok || !result?.success) throw new Error(result?.message || "Failed to add subdomain");

        showToast('success', result?.message || "Subdomain added");
        bootstrap.Modal.getInstance(document.getElementById("addSubdomainModal"))?.hide();
        setTimeout(() => location.reload(), 600);

    } catch (e) {
        showToast('danger', e.message || "Error adding subdomain");
    }
}
