async function addZone() {
    const name = document.getElementById("modal-domain").value.trim();
    const kind = document.getElementById("modal-type").value;
    const dnssec = document.getElementById("modal-dnssec").value === "Enabled";

    const data = { Name: name, Kind: kind, Dnssec: dnssec };

    const response = await fetch("/?handler=AddZone", {
        method: "POST",
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify(data)
    });

    const result = await response.json();
    if (response.ok && result.success) {
        location.reload();
    } else {
        console.error("Error response:", result);
        alert("Add zone error: " + result.message || "Неизвестная ошибка");
    }
}


async function editZone() {
    const name = document.getElementById("modal-domain").value.trim();
    const kind = document.getElementById("modal-type").value;
    const dnssec = document.getElementById("modal-dnssec").value === "Enabled";
    const serial = parseInt(document.getElementById("modal-serial").value);

    const data = { Name: name, Kind: kind, Dnssec: dnssec, Serial: serial };

    const response = await fetch("/?handler=EditZone", {
        method: "POST",
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify(data)
    });

    const result = await response.json();
    if (result.success) {
        location.reload();
    } else {
        alert("Update zone error");
    }
}

async function deleteZone(name) {
    if (!confirm(`Удалить зону ${name}?`)) return;

    const response = await fetch("/?handler=DeleteZone", {
        method: "POST",
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify({ Name: name })
    });

    const result = await response.json();
    if (result.success) {
        location.reload();
    } else {
        alert("Delete zone error");
    }
}

function showEditZone(name, kind, dnssec, serial) {
    document.getElementById("modal-domain").value = name;
    document.getElementById("modal-type").value = kind;
    document.getElementById("modal-dnssec").value = dnssec ? "Enabled" : "Disabled";
    document.getElementById("modal-serial").value = serial;

    document.getElementById("create-buttons").style.display = "none";
    document.getElementById("update-buttons").style.display = "block";

    showModal();
}
