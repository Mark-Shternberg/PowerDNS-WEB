// ==========================
// ALL COMMENTS IN ENGLISH
// ==========================

// ===== THEME TOGGLE (BOOTSTRAP 5.3 DATA-BS-THEME) =====
(function () {
    const html = document.documentElement;
    const btn = document.getElementById('themeToggle');
    const icon = document.getElementById('themeIcon');

    function applyIcon(mode) {
        if (!icon) return;
        // FA4/FA5 COMPATIBLE ICONS
        if (mode === 'dark') { icon.classList.remove('fa-moon'); icon.classList.add('fa-sun'); }
        else { icon.classList.remove('fa-sun'); icon.classList.add('fa-moon'); }
    }
    function setPressed(mode) {
        if (!btn) return;
        btn.setAttribute('aria-pressed', mode === 'dark' ? 'true' : 'false');
    }
    function setTheme(mode) {
        try { localStorage.setItem('theme', mode); } catch { /* NOOP */ }
        html.setAttribute('data-bs-theme', mode);
        applyIcon(mode);
        setPressed(mode);
    }
    // SYNC WITH EARLY-APPLIED THEME
    const current = html.getAttribute('data-bs-theme') || 'dark';
    applyIcon(current);
    setPressed(current);

    btn?.addEventListener('click', () => {
        const next = (html.getAttribute('data-bs-theme') === 'dark') ? 'light' : 'dark';
        setTheme(next);
    });
})();

// ===== INPUT MASKS & BASIC IPv4 VALIDATION =====
$(function () {
    // APPLY IPv4 MASK
    $(".ip-input").inputmask("999.999.999.999", { placeholder: "___.___.___.___" });

    // QUICK VALIDATION ON BLUR (0-255)
    $(document).on('blur', '.ip-input', function () {
        const v = String($(this).val() || "").trim();
        if (!isValidIPv4(v)) $(this).addClass('is-invalid');
        else $(this).removeClass('is-invalid');
    });
});

function isValidIPv4(s) {
    const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(s);
    if (!m) return false;
    for (let i = 1; i <= 4; i++) {
        const n = +m[i];
        if (n < 0 || n > 255) return false;
    }
    return true;
}

// ===== TOASTS API (CLIENT-SIDE) =====
(function () {
    // CREATE OR GET CONTAINER
    function ensureContainer() {
        let c = document.getElementById('toastContainer');
        if (!c) {
            c = document.createElement('div');
            c.id = 'toastContainer';
            c.className = 'toast-container position-fixed bottom-0 end-0 p-3';
            c.style.zIndex = '1080';
            document.body.appendChild(c);
        }
        return c;
    }

    // PUBLIC FUNCTION: showToast(type, message, delay?)
    window.showToast = function (type, message, delay = 4000) {
        const palette = { success: 'text-bg-success', warning: 'text-bg-warning', danger: 'text-bg-danger', info: 'text-bg-info' };
        const icons = { success: 'fa-check-circle', warning: 'fa-exclamation-triangle', danger: 'fa-times-circle', info: 'fa-info-circle' };

        const container = ensureContainer();
        const el = document.createElement('div');
        el.className = `toast ${palette[type] || palette.info} border-0`;
        el.setAttribute('role', 'alert');
        el.setAttribute('aria-live', 'assertive');
        el.setAttribute('aria-atomic', 'true');
        el.dataset.bsDelay = String(delay);

        // PREVENT XSS: USE TEXT NODES INSTEAD OF INNERHTML FOR MESSAGE
        const wrapper = document.createElement('div');
        wrapper.className = 'd-flex';

        const body = document.createElement('div');
        body.className = 'toast-body';
        const ico = document.createElement('i');
        ico.className = `fa ${icons[type] || icons.info} me-2`;
        const text = document.createTextNode(message);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `btn-close ${type === 'success' || type === 'danger' ? 'btn-close-white' : ''} me-2 m-auto`;
        btn.setAttribute('data-bs-dismiss', 'toast');
        btn.setAttribute('aria-label', 'Close');

        body.appendChild(ico);
        body.appendChild(text);
        wrapper.appendChild(body);
        wrapper.appendChild(btn);
        el.appendChild(wrapper);
        container.appendChild(el);

        try { new bootstrap.Toast(el).show(); } catch { /* NOOP */ }
    };

    // BACKWARD COMPAT: showNotification(message, typeNumeric)
    // typeNumeric: 1=success, 2=error, 3=warning (LEGACY)
    window.showNotification = function (message, type = 1) {
        const map = { 1: 'success', 2: 'danger', 3: 'warning' };
        const kind = map[type] || 'info';
        const delay = (kind === 'danger') ? 7000 : (kind === 'warning' ? 5000 : 4000);
        window.showToast(kind, message, delay);
    };

    // AUTOSHOW SERVER-SIDE TEMPDATA TOASTS (IF ANY)
    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('#toastContainer .toast').forEach(el => {
            try { new bootstrap.Toast(el).show(); } catch { /* NOOP */ }
        });
    });
})();

// ===== DATE/TIME HELPERS =====
function parseDate(dateString) {
    // FORMAT: DD.MM.YYYY HH:mm
    const re = /^(\d{2})\.(\d{2})\.(\d{4}) (\d{2}):(\d{2})$/;
    const m = re.exec(dateString);
    if (!m) return null;
    const d = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]), Number(m[4]), Number(m[5]));
    if (isNaN(d.getTime())) return null;
    if (d.getFullYear() !== Number(m[3])) return null;
    return d;
}
function getCurrentDateTime() {
    const now = new Date();
    const dd = String(now.getDate()).padStart(2, '0');
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    const yyyy = now.getFullYear();
    const hh = String(now.getHours()).padStart(2, '0');
    const mi = String(now.getMinutes()).padStart(2, '0');
    return `${dd}.${mm}.${yyyy} ${hh}:${mi}`;
}

// ===== MODAL CLOSE WITHOUT BACKDROP =====
function closeModal(modalId) {
    const modal = document.querySelector(modalId);
    const back = document.querySelector('.modal-backdrop');
    if (modal) {
        const inst = bootstrap.Modal.getInstance(modal) || new bootstrap.Modal(modal, { backdrop: false });
        inst.hide();
    }
    if (back) back.remove();
    document.body.classList.remove('modal-open');
    document.body.style.paddingRight = '';
}

// ===== OPTIONAL ESC KEY HANDLER FOR SIDE PANELS (SAFE-NOOP) =====
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        document.body.classList.remove('sidebar-open', 'no-scroll');
    }
});
