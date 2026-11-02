// ========================= SAFE NUMBER PARSING =========================
const N = v => Number.isFinite(+v) ? +v : 0;

// ========================= FORMAT UPTIME (H:M) =========================
function formatUptime(seconds) {
    const s = N(seconds);
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    return `${h}${I18N.TimeH} ${m}${I18N.TimeM}`;
}

// ========================= UPDATE TYPED LIST INTO PRE (NO NESTED <PRE>) =========================
function updateQueryList(elementId, data) {
    const container = document.getElementById(elementId);
    if (!container) return;

    if (!data || data.length === 0) {
        container.textContent = I18N.NoData;
        return;
    }

    const maxNameLength = 40;
    const maxValueLength = 8;

    const lines = data.map(entry => {
        const fullName = String(entry?.name ?? "");
        const shortName = fullName.length > maxNameLength
            ? fullName.substring(0, maxNameLength - 3) + "..."
            : fullName;

        const value = String(entry?.value ?? "").padStart(maxValueLength, " ");
        return `<span class="query-item" title="${fullName}">${shortName}</span> <span class="query-value">${value}</span>`;
    });

    container.innerHTML = lines.join("\n");
}

// ========================= QPS ROLLING BUFFER (5 MIN  5S) =========================
const BUFFER_POINTS = 60; // 60 * 5s = 5 minutes
const labels = [];
const qpsData = [];
let lastTotal = null;
let lastTs = null;

// ========================= CHART INIT (SINGLE DATASET) =========================
let qpsChart = null;
function ensureChart() {
    if (qpsChart) return qpsChart;
    const ctx = document.getElementById('queryChart');
    if (!ctx) return null;

    qpsChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels,
            datasets: [{
                label: I18N.ChartQpsLabel,
                data: qpsData,
                tension: 0.25,
                pointRadius: 0,
                fill: true
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                x: { ticks: { maxTicksLimit: 10 } },
                y: { beginAtZero: true }
            },
            plugins: {
                legend: { display: false }
            }
        }
    });
    ctx.parentElement.style.height = '260px';
    return qpsChart;
}

function pushQpsPoint(qps) {
    const now = new Date();
    const label = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    labels.push(label);
    qpsData.push(qps);

    if (labels.length > BUFFER_POINTS) { labels.shift(); qpsData.shift(); }
    const c = ensureChart();
    if (c) c.update('none');
}

// ========================= FETCH & RENDER POWERDNS STATS =========================
async function fetchPowerDNSStats() {
    try {
        const resp = await fetch("/Index?handler=Stats", {
            method: "GET",
            headers: { "Accept": "application/json" }
        });
        const data = await resp.json();
        if (!data?.success) { console.error("Stats error:", data?.message); return; }

        // CORE METRICS
        const activePort = (recursorEnabled.toLowerCase() === "enabled") ? 5300 : 53;
        document.getElementById("activePort").textContent = activePort;

        document.getElementById("uptime").textContent = formatUptime(data.uptime);
        document.getElementById("totalQueries").textContent = data.totalQueries ?? "--";

        // QPS CALC
        const ts = Date.now();
        if (lastTotal !== null && lastTs !== null) {
            const delta = Math.max(0, N(data.totalQueries) - N(lastTotal));
            const elapsed = Math.max(1, (ts - lastTs) / 1000.0);
            const qps = +(delta / elapsed).toFixed(2);
            document.getElementById("qps").textContent = qps.toString();
            pushQpsPoint(qps);
        }
        lastTotal = N(data.totalQueries);
        lastTs = ts;

        // LISTS
        updateQueryList("noerror-queries", data.noerrorQueries);
        updateQueryList("nxdomain-queries", data.nxdomainQueries);
        updateQueryList("queries", data.queries);

        // RECURSOR (IF DOM NODES EXIST)
        const haveRecursor = document.getElementById("recursorCacheHits");
        if (haveRecursor) {
            const hits = N(data.recursorCacheHits);
            const misses = N(data.recursorCacheMisses);
            const total = hits + misses;

            document.getElementById("recursorCacheHits").textContent = (hits || hits === 0) ? hits : "--";
            document.getElementById("recursorCacheMisses").textContent = (misses || misses === 0) ? misses : "--";
            document.getElementById("recursorUptime").textContent = formatUptime(data.recursorUptime ?? 0);
            document.getElementById("recursorHitRatio").textContent = total > 0 ? ((hits / total) * 100).toFixed(1) + "%" : "--";

            updateQueryList("top-queries", data.topQueries);
            updateQueryList("top-remotes", data.topRemotes);
        }
    } catch (e) {
        console.error("Failed to load stats:", e);
    }
}

// ========================= INIT =========================
fetchPowerDNSStats();
setInterval(fetchPowerDNSStats, 5000);
