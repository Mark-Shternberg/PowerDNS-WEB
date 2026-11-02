// ========================= LOG LOADING & REFRESH =========================
async function fetchPDNSLogs() {
    try {
        const response = await fetch(window.location.pathname + "?handler=Logs", {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            }
        });

        const data = await response.json();
        document.getElementById("authoritativeLogs").innerText = data.authoritativeLogs || L.NoLogs;
    } catch (error) {
        console.error("Failed to load logs:", error);
        document.getElementById("authoritativeLogs").innerText = L.FetchError;
    }
}

// AUTO UPDATE LOGS EVERY 5 SECONDS
setInterval(fetchPDNSLogs, 5000);
fetchPDNSLogs();
