// ZONES PAGE JS
// - bootstrap form validation
// - modal wiring via data-* (with click prefilling fallback)
// - dnssec keys rendering
// - simple client-side search

(function () {
    // --------- HELPERS ---------
    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
    const ensureDot = (s) => !s ? s : (s.endsWith('.') ? s : s + '.');

    // Bootstrap validation (generic)
    const hookValidation = () => {
        const forms = $$('.needs-validation');
        forms.forEach(form => {
            form.addEventListener('submit', function (e) {
                applyConditionalRequireds();
                // синхронизируем скрытое поле kind перед сабмитом
                syncKindHidden();
                if (!form.checkValidity()) {
                    e.preventDefault();
                    e.stopPropagation();
                }
                form.classList.add('was-validated');
            }, false);
        });
    };

    // держим hidden kind = значению селекта
    const syncKindHidden = () => {
        const sel = $('#zone-kind');
        const hid = $('#zone-kind-hidden');
        if (sel && hid) hid.value = sel.value || 'Native';
    };

    // Conditional requireds + block toggles
    const applyConditionalRequireds = () => {
        const mode = $('#form-mode')?.value || 'add-forward';
        const kind = $('#zone-kind')?.value || 'Native';

        const forwardBlock = $('#forward-block');
        const reverseBlock = $('#reverse-block');
        const nameEl = $('#zone-name');
        const revEl = $('#reverse-prefix');

        if (mode === 'add-reverse') {
            forwardBlock.classList.add('d-none');
            reverseBlock.classList.remove('d-none');
            if (nameEl) { nameEl.removeAttribute('required'); nameEl.value = ''; }
            if (revEl) { revEl.setAttribute('required', 'required'); }
        } else {
            reverseBlock.classList.add('d-none');
            forwardBlock.classList.remove('d-none');
            if (nameEl) { nameEl.setAttribute('required', 'required'); }
            if (revEl) { revEl.removeAttribute('required'); revEl.value = ''; }
        }

        const slaveBlock = $('#slave-block');
        const masterEl = $('#zone-master');

        if (kind === 'Slave') {
            slaveBlock.classList.remove('d-none');
            masterEl?.setAttribute('required', 'required');
        } else {
            slaveBlock.classList.add('d-none');
            masterEl?.removeAttribute('required');
            if (masterEl) masterEl.value = '';
        }
    };

    // Keys rendering
    const renderKeys = (container, keysArr) => {
        container.innerHTML = '';
        if (!Array.isArray(keysArr) || keysArr.length === 0) {
            container.innerHTML = `<div class="alert alert-info mb-0">No DNSSEC keys found.</div>`;
            return;
        }
        keysArr.forEach(key => {
            const dsList = Array.isArray(key.ds) ? key.ds : [];
            const card = document.createElement('div');
            card.className = 'card';

            const body = document.createElement('div');
            body.className = 'card-body';

            const header = document.createElement('h6');
            header.className = 'card-title';
            header.innerHTML = `<i class="fa fa-lock me-1"></i> Key ID: ${key.id ?? 'Unknown'} (${key.algorithm ?? 'Unknown'}, ${key.bits ?? '?'} bits)`;

            const dnskey = document.createElement('p');
            dnskey.className = 'mb-2';
            dnskey.innerHTML = `<strong>DNSKEY:</strong> <code class="text-break">${key.dnskey ?? 'No DNSKEY'}</code>`;

            const dsTitle = document.createElement('p');
            dsTitle.className = 'mb-1';
            dsTitle.innerHTML = `<strong>DS Records:</strong>`;

            const list = document.createElement('div');
            list.className = 'vstack gap-2';

            if (dsList.length === 0) {
                list.innerHTML = `<div class="text-danger">No DS records available</div>`;
            } else {
                dsList.forEach(ds => {
                    const row = document.createElement('div');
                    row.className = 'd-flex align-items-center justify-content-between border rounded p-2 gap-2';

                    const code = document.createElement('code');
                    code.className = 'text-break';
                    code.textContent = ds;

                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'btn btn-sm btn-outline-secondary';
                    btn.innerHTML = '<i class="fa fa-copy me-1"></i> Copy';
                    btn.addEventListener('click', async () => {
                        try { await navigator.clipboard.writeText(ds); showToast('success', 'Copied'); }
                        catch { showToast('warning', 'Copy failed'); }
                    });

                    row.appendChild(code);
                    row.appendChild(btn);
                    list.appendChild(row);
                });
            }

            body.appendChild(header);
            body.appendChild(dnskey);
            body.appendChild(dsTitle);
            body.appendChild(list);
            card.appendChild(body);
            container.appendChild(card);
        });
    };

    // --------- PREFILLERS ---------
    const prefillAdd = (mode) => {
        const form = $('#zoneForm');
        if (!form) return;
        form.reset();
        form.classList.remove('was-validated');

        $('#zone-kind').disabled = false;
        $('#zone-name').readOnly = false;

        $('#form-mode').value = mode || 'add-forward';
        $('#addEditZoneLabel').textContent = (mode === 'add-reverse') ? 'Add reverse zone' : 'Add forward zone';

        $('#btn-add').classList.remove('d-none');
        $('#btn-save').classList.add('d-none');

        // синхронизация hidden kind (по умолчанию Native)
        syncKindHidden();
        $('#zone-serial').value = '0';
        applyConditionalRequireds();
    };

    const prefillEdit = (btn) => {
        const form = $('#zoneForm');
        if (!form) return;
        form.reset();
        form.classList.remove('was-validated');

        const name = btn.getAttribute('data-name') || '';
        const kind = btn.getAttribute('data-kind') || 'Native';
        const master = btn.getAttribute('data-master') || '';
        const dnssec = btn.getAttribute('data-dnssec') || 'Disabled';
        const serial = btn.getAttribute('data-serial') || '0';

        $('#form-mode').value = 'edit';
        $('#addEditZoneLabel').textContent = 'Edit zone';

        $('#btn-add').classList.add('d-none');
        $('#btn-save').classList.remove('d-none');

        $('#zone-name').value = name;
        $('#zone-kind').value = kind;
        $('#zone-dnssec').value = dnssec;
        $('#zone-master').value = master;
        $('#zone-serial').value = serial;

        // зафиксируем значение kind в hidden
        syncKindHidden();

        // edit state: no reverse, lock name+type визуально
        $('#forward-block').classList.remove('d-none');
        $('#reverse-block').classList.add('d-none');
        $('#zone-kind').disabled = true;   // disabled — но hidden "kind" унесёт реальное значение
        $('#zone-name').readOnly = true;

        applyConditionalRequireds();
    };

    // --------- EVENT WIRING ---------

    // поддерживаем hidden kind при ручном переключении (в add-режиме)
    $('#zone-kind')?.addEventListener('change', () => {
        syncKindHidden();
        applyConditionalRequireds();
    });

    // 1) Click-prefill fallback
    document.addEventListener('click', (e) => {
        const addBtn = e.target.closest('button[data-bs-target="#addEditZoneModal"].js-open-add');
        const editBtn = e.target.closest('button[data-bs-target="#addEditZoneModal"].js-edit');
        if (addBtn) {
            const mode = addBtn.getAttribute('data-mode') || 'add-forward';
            prefillAdd(mode);
        } else if (editBtn) {
            prefillEdit(editBtn);
        }
    });

    // 2) Also prefill on show
    const addEditModal = document.getElementById('addEditZoneModal');
    if (addEditModal) {
        addEditModal.addEventListener('show.bs.modal', function (ev) {
            const btn = ev.relatedTarget;
            if (!btn) return;
            const mode = btn.getAttribute('data-mode') || 'add-forward';
            if (mode === 'edit') prefillEdit(btn);
            else prefillAdd(mode);
        });
    }

    // Confirm Delete modal — fill zone name
    const delModal = document.getElementById('confirmDeleteModal');
    if (delModal) {
        delModal.addEventListener('show.bs.modal', function (ev) {
            const btn = ev.relatedTarget;
            if (!btn) return;
            const name = btn.getAttribute('data-name') || '';
            $('#delete-zone-name').value = name;
            $('#delete-zone-name-view').textContent = name;
        });
    }

    // DNSSEC keys modal — load keys
    const keysModal = document.getElementById('dnssecKeysModal');
    if (keysModal) {
        keysModal.addEventListener('show.bs.modal', async function (ev) {
            const btn = ev.relatedTarget;
            const container = $('#dsRecordsContainer', keysModal);
            if (container) container.innerHTML = `<div class="text-muted">Loading…</div>`;
            if (!btn) return;

            const rawName = btn.getAttribute('data-zone') || '';
            const name = ensureDot(rawName);

            try {
                const resp = await fetch(`?handler=DnssecKeys&Name=${encodeURIComponent(name)}`, {
                    headers: { 'Accept': 'application/json' }
                });
                const result = await resp.json();

                if (!resp.ok || !result || result.success !== true) {
                    throw new Error(result?.message || `HTTP ${resp.status}`);
                }

                let keys = result.keys;
                // robust parsing: stringified JSON / object / array
                if (typeof keys === 'string') {
                    try { keys = JSON.parse(keys); } catch { keys = []; }
                } else if (keys && typeof keys === 'object' && !Array.isArray(keys)) {
                    if (Array.isArray(keys.keys)) keys = keys.keys; else keys = [];
                }

                renderKeys(container, keys);
            } catch (e) {
                console.error('DNSSEC keys load error:', e);
                if (container) {
                    container.innerHTML = `<div class="alert alert-danger mb-0">
                        Failed to load DNSSEC keys. ${e?.message ? ('<small class="text-muted">' + e.message + '</small>') : ''}
                    </div>`;
                }
            }
        });
    }

    // --------- SEARCH ---------
    document.addEventListener('DOMContentLoaded', () => {
        const searchInput = $('#search');
        const clearButton = $('#clear-btn');
        if (!searchInput || !clearButton) return;

        const filterTable = () => {
            const filter = (searchInput.value || '').toLowerCase();
            const rows = $$('#main_table tbody tr');
            rows.forEach(r => {
                const cell = $('td:first-child', r);
                const txt = (cell?.textContent || '').trim().toLowerCase();
                r.style.display = txt.includes(filter) ? '' : 'none';
            });
        };
        const toggleClear = () => clearButton.style.display = searchInput.value.length > 0 ? 'inline-block' : 'none';

        searchInput.addEventListener('input', () => { toggleClear(); filterTable(); });
        clearButton.addEventListener('click', () => { searchInput.value = ''; toggleClear(); filterTable(); });
    });

    // init validation
    hookValidation();
})();
