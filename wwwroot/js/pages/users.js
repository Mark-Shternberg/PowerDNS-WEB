// ========================= CSRF =========================
function getCsrf() {
    const el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

// ========================= ADD NEW USER =========================
document.getElementById('loginForm').addEventListener('submit', async (e) => {
    e.preventDefault();

    const username = document.getElementById('username').value.trim();
    const role = document.getElementById('role').value;
    const password = document.getElementById('password').value;

    if (!username || !password) {
        showToast('warning', T.fillAll);
        return;
    }
    if (username.length < 3) {
        showToast('warning', T.usernameShort);
        return;
    }

    try {
        const btn = document.getElementById('btn-save');
        btn.disabled = true;

        const resp = await fetch(window.location.pathname + '?handler=add_new_user', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrf()
            },
            body: JSON.stringify({ username, role, password })
        });

        const data = await resp.json();
        if (!resp.ok || !data?.success) throw new Error(data?.message || T.regError);

        showToast('success', T.userAdded);
        setTimeout(() => location.reload(), 600);
    } catch (err) {
        showToast('danger', err.message || T.regError);
    } finally {
        document.getElementById('btn-save').disabled = false;
    }
});

// ========================= EVENT DELEGATION FOR TABLE BUTTONS =========================
document.addEventListener('click', (e) => {
    const editBtn = e.target.closest('.btn-edit');
    const saveBtn = e.target.closest('.btn-save-edit');
    const cancelBtn = e.target.closest('.btn-default');
    const delBtn = e.target.closest('.btn-delete');

    if (editBtn) onEdit(editBtn);
    else if (saveBtn) onSave(saveBtn);
    else if (cancelBtn) onCancel(cancelBtn);
    else if (delBtn) onDelete(delBtn);
});

// ========================= EDIT MODE =========================
function onEdit(btn) {
    const row = btn.closest('tr');
    if (!row) return;

    row.querySelector('.js-password-editor').hidden = false;
    row.querySelector('.js-role-display').hidden = true;
    row.querySelector('.js-role-editor').hidden = false;

    row.querySelector('.btn-edit').hidden = true;
    row.querySelector('.btn-save-edit').hidden = false;
    row.querySelector('.btn-cancel').hidden = false;
    row.querySelector('.btn-delete').hidden = true;
}

// ========================= CANCEL EDIT =========================
function onCancel(btn) {
    const row = btn.closest('tr');
    if (!row) return;

    row.querySelector('.js-password-editor').value = '';
    row.querySelector('.js-password-editor').hidden = true;
    row.querySelector('.js-role-display').hidden = false;
    row.querySelector('.js-role-editor').hidden = true;

    row.querySelector('.btn-edit').hidden = false;
    row.querySelector('.btn-save-edit').hidden = true;
    row.querySelector('.btn-cancel').hidden = true;
    row.querySelector('.btn-delete').hidden = false;
}

// ========================= SAVE USER =========================
async function onSave(btn) {
    const id = btn.dataset.id;
    const row = btn.closest('tr');
    if (!row) return;

    const username = id;
    const role = row.querySelector('.js-role-editor').value;
    const password = row.querySelector('.js-password-editor').value;

    try {
        btn.disabled = true;

        const resp = await fetch(window.location.pathname + '?handler=Update_user', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrf()
            },
            body: JSON.stringify({ username, role, password })
        });

        const data = await resp.json();
        if (!resp.ok || !data?.success) throw new Error(data?.message || T.saveError);

        showToast('success', T.saveOk);
        // UPDATE ROW UI
        row.querySelector('.js-password-editor').value = '';
        row.querySelector('.js-password-editor').hidden = true;
        row.querySelector('.js-role-display').textContent = role;
        row.querySelector('.js-role-display').hidden = false;
        row.querySelector('.js-role-editor').hidden = true;

        row.querySelector('.btn-edit').hidden = false;
        row.querySelector('.btn-save-edit').hidden = true;
        row.querySelector('.btn-cancel').hidden = true;
        row.querySelector('.btn-delete').hidden = false;

    } catch (err) {
        showToast('danger', err.message || T.saveError);
    } finally {
        btn.disabled = false;
    }
}

// ========================= DELETE USER (WITH CONFIRM MODAL) =========================
let pendingDelete = null;

function onDelete(btn) {
    const username = btn.dataset.id;
    pendingDelete = { username, row: btn.closest('tr') };

    document.getElementById('confirmMessage').textContent = `${T.confirmDeletePrefix} "${username}"?`;

    // RESET "YES" HANDLER TO AVOID ACCUMULATION
    const yes = document.getElementById('confirmYes');
    const yesClone = yes.cloneNode(true);
    yes.parentNode.replaceChild(yesClone, yes);

    yesClone.addEventListener('click', confirmDelete, { once: true });

    new bootstrap.Modal(document.getElementById('confirmModal'), { backdrop: false }).show();
}

async function confirmDelete() {
    if (!pendingDelete) return;

    const { username, row } = pendingDelete;
    pendingDelete = null;

    try {
        const resp = await fetch(window.location.pathname + '?handler=Delete_user', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrf()
            },
            body: JSON.stringify({ username })
        });

        const data = await resp.json();
        if (!resp.ok || !data?.success) throw new Error(data?.message || T.deletionError);

        // CLOSE MODAL + REMOVE ROW + TOAST
        bootstrap.Modal.getInstance(document.getElementById('confirmModal'))?.hide();
        row?.remove();
        showToast('success', `${T.deletedPrefix} "${username}".`);

    } catch (err) {
        showToast('danger', err.message || T.deletionError);
    }
}
