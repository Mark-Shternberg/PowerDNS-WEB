(function () {
    // ========================= I18N =========================
    let M = {};
    try {
        const el = document.getElementById('i18n-settings');
        if (el && el.textContent) M = JSON.parse(el.textContent);
    } catch { /* noop */ }

    // ========================= CSRF =========================
    window.getCsrf = function () {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    };

    // ========================= TOGGLE PASSWORD VISIBILITY =========================
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-toggle-vis]');
        if (!btn) return;
        const sel = btn.getAttribute('data-toggle-vis');
        const input = document.querySelector(sel);
        if (!input) return;
        input.type = input.type === 'password' ? 'text' : 'password';
        const icon = btn.querySelector('i');
        if (icon) { icon.classList.toggle('fa-eye'); icon.classList.toggle('fa-eye-slash'); }
    });

    // ========================= RECURSOR FIELDS VISIBILITY =========================
    window.toggleRecursorFields = function () {
        const status = document.getElementById('recursorStatus')?.value;
        const fields = document.getElementById('recursorFields');
        if (fields) fields.style.display = (status === 'Disabled') ? 'none' : 'block';
    };
    document.addEventListener('DOMContentLoaded', window.toggleRecursorFields);

    // ========================= HELPERS =========================
    function isHttpUrl(u) {
        try { const x = new URL(u); return x.protocol === 'http:' || x.protocol === 'https:'; }
        catch { return false; }
    }
    function ensureTrailingDot(s) {
        if (!s) return s;
        return s.endsWith('.') ? s : (s + '.');
    }
    function stripTrailingDot(s) {
        if (!s) return s;
        return s.endsWith('.') ? s.slice(0, -1) : s;
    }
    // validation.js adapters
    function mustIPv4OrEmpty(v) {
        if (!v) return true;
        return validateIPv4(v) === ""; // "" == OK
    }
    function validateHostFqdnOrErrorText(v) {
        // Валидируем БЕЗ точки, но сохраняем С точкой
        const noDot = stripTrailingDot((v || '').trim());
        return validateForwardZoneName(noDot); // вернёт "" если ОК
    }

    // RNAME ⇄ email
    function rnameToEmail(rname) {
        if (!rname) return "";
        let s = rname.endsWith('.') ? rname.slice(0, -1) : rname;
        let out = "", i = 0, seenAt = false;
        while (i < s.length) {
            const ch = s[i];
            if (ch === '\\' && i + 1 < s.length) { out += s[i + 1]; i += 2; }
            else if (ch === '.' && !seenAt) { out += '@'; seenAt = true; i++; }
            else { out += ch; i++; }
        }
        return out;
    }
    function emailToRname(email) {
        if (!email) return "";
        if (!email.includes('@')) return ensureTrailingDot(email);
        const [local, domain] = email.split('@');
        const escLocal = local.replace(/\./g, '\\.');
        return ensureTrailingDot(`${escLocal}.${domain}`);
    }

    // Показать пользователю e-mail, даже если в конфиге лежит RNAME
    document.addEventListener('DOMContentLoaded', () => {
        const mailEl = document.getElementById('pdnsSoaMail');
        if (mailEl && mailEl.value && !mailEl.value.includes('@')) {
            mailEl.value = rnameToEmail(mailEl.value);
        }
    });

    // ========================= SAVE SETTINGS =========================
    window.saveSettings = async function () {
        const btn = document.getElementById('btnSaveSettings');
        btn.disabled = true;

        // Нормализуем NS: валидируем без точки, сохраняем с точкой
        const uiNsRaw = document.getElementById('pdnsSoaNs')?.value.trim() || "";
        const nsErr = uiNsRaw ? validateHostFqdnOrErrorText(uiNsRaw) : "";
        if (nsErr) {
            showToast('warning', M.soaNsInvalid || 'SOA Primary NS: invalid host.');
            btn.disabled = false; return;
        }
        const nsWithDot = ensureTrailingDot(uiNsRaw);

        // Email → RNAME
        const uiEmail = document.getElementById('pdnsSoaMail')?.value.trim() || "";
        // Если input type="email", используем встроенную валидацию, иначе — мягкая проверка
        const emailInput = document.getElementById('pdnsSoaMail');
        const emailValid = !uiEmail || (emailInput?.type === 'email'
            ? emailInput.checkValidity()
            : /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(uiEmail));
        if (!emailValid) {
            showToast('warning', M.soaEmailInvalid || 'SOA Admin Email: invalid email.');
            btn.disabled = false; return;
        }
        const soaMailRname = emailToRname(uiEmail);

        const settingsData = {
            MySQL: {
                Server:   document.getElementById('mysqlServer').value.trim(),
                User:     document.getElementById('mysqlUser').value.trim(),
                Password: document.getElementById('mysqlPassword').value,
                Database: document.getElementById('mysqlDatabase').value.trim()
            },
            PowerDNS: {
                Url:       document.getElementById('pdnsUrl').value.trim(),
                Api_Key:   document.getElementById('pdnsApiKey').value,
                Default_A: document.getElementById('pdnsDefaultA').value.trim(),
                SOA: {
                    Ns:   nsWithDot,      // <-- сохраняем с точкой
                    Mail: soaMailRname    // <-- в формате RNAME
                }
            },
            Recursor: {
                Enabled: document.getElementById('recursorStatus').value,
                Url:     document.getElementById('recursorUrl')?.value.trim() || "",
                Api_Key: document.getElementById('recursorApiKey')?.value || ""
            }
        };

        // ========================= ВАЛИДАЦИЯ =========================
        if (!settingsData.MySQL.Server || !settingsData.MySQL.User || !settingsData.MySQL.Database) {
            showToast('warning', M.mysqlRequired || 'MySQL: server, user and database are required.');
            btn.disabled = false; return;
        }
        if (!isHttpUrl(settingsData.PowerDNS.Url)) {
            showToast('warning', M.pdnsUrlBad || 'PowerDNS: API URL must be http(s)://…');
            btn.disabled = false; return;
        }
        if (!mustIPv4OrEmpty(settingsData.PowerDNS.Default_A)) {
            showToast('warning', M.defaultAInvalid || 'Default A record: invalid IPv4.');
            btn.disabled = false; return;
        }
        if (settingsData.Recursor.Enabled === 'Enabled' && !isHttpUrl(settingsData.Recursor.Url)) {
            showToast('warning', M.recursorUrlBad || 'Recursor: URL must be http(s)://…');
            btn.disabled = false; return;
        }

        try {
            const response = await fetch(window.location.pathname + "?handler=SaveSettings", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "RequestVerificationToken": getCsrf()
                },
                body: JSON.stringify(settingsData)
            });
            const payload = await response.json();
            if (!response.ok || payload?.success === false) {
                throw new Error(payload?.message || M.saveFail || 'Save failed.');
            }
            showToast('success', payload?.message || M.saveOk || 'Settings saved');
        } catch (err) {
            console.error(err);
            showToast('danger', err.message || M.saveFail || 'Save failed.');
        } finally {
            btn.disabled = false;
        }
    };
})();
