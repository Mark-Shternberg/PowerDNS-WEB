(function () {
    // ========================= LOCALIZATION =========================
    const T = loginI18n;

    // ========================= FORM VALIDATION =========================
    (function () {
        const forms = document.querySelectorAll('.needs-validation');
        Array.from(forms).forEach(form => {
            form.addEventListener('submit', evt => {
                if (!form.checkValidity()) { evt.preventDefault(); evt.stopPropagation(); }
                form.classList.add('was-validated');
            }, false);
        });
    })();

    // ========================= PASSWORD VISIBILITY =========================
    (function () {
        const btn = document.getElementById('togglePw');
        const pw = document.getElementById('password');
        if (!btn || !pw) return;
        btn.addEventListener('click', () => {
            const isText = pw.type === 'text';
            pw.type = isText ? 'password' : 'text';
            btn.setAttribute('aria-pressed', isText ? 'false' : 'true');
            btn.setAttribute('title', isText ? T.showPw : T.hidePw);
            const icon = btn.querySelector('i');
            if (icon) { icon.classList.toggle('fa-eye'); icon.classList.toggle('fa-eye-slash'); }
        });
    })();

    // ========================= LOGIN SUBMISSION =========================
    const form = document.getElementById('loginForm');
    if (!form) return;

    const notify = (kind, msg) => {
        if (typeof window.showToast === 'function') {
            const delay = kind === 'danger' ? 6000 : 3500;
            window.showToast(kind, msg, delay);
            return;
        }
        const n = document.getElementById('notification-div');
        if (!n) return;
        n.className = `alert mt-3 ${kind === 'success' ? 'alert-success' : kind === 'warning' ? 'alert-warning' : 'alert-danger'}`;
        n.textContent = msg;
        n.classList.remove('d-none');
    };

    form.addEventListener('submit', async function (e) {
        e.preventDefault();

        const u = document.getElementById('username').value.trim();
        const p = document.getElementById('password').value.trim();
        if (!u || !p) {
            notify('warning', T.required);
            return;
        }

        const btn = document.getElementById('btnSignIn');
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const returnUrl = document.getElementById('returnUrl')?.value || '';

        const idleButtonHtml = btn.innerHTML;
        const setBusy = (busy) => {
            btn.disabled = busy;
            btn.innerHTML = busy ? `<span class="spinner-border spinner-border-sm me-2"></span>${T.signingIn}` : idleButtonHtml;
        };

        setBusy(true);
        try {
            const resp = await fetch('/login?handler=Login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({ username: u, password: p, returnUrl: returnUrl })
            });

            const result = await resp.json().catch(() => ({}));

            if (resp.ok && result && result.success) {
                notify('success', T.success);
                // client-side redirect fallback (server тоже может редиректить после SignIn)
                const target = (returnUrl && /^\/(?!\/)/.test(returnUrl)) ? returnUrl : '/';
                setTimeout(() => { window.location.href = target; }, 900);
            } else {
                const msg = (result && result.message) ? String(result.message) : 'Unknown error';
                notify('danger', T.errPrefix + msg);
                setBusy(false);
            }
        } catch {
            notify('danger', T.netErr);
            setBusy(false);
        }
    });
})();
