// ========================= LOCALIZATION =========================
const fmt = (s, ...args) => s.replace(/{(\d+)}/g, (m, i) => (args[i] ?? m));

// ========================= FORM VALIDATION =========================
(() => {
  'use strict';
  const forms = document.querySelectorAll('.needs-validation');
  Array.from(forms).forEach(form => {
    form.addEventListener('submit', evt => {
      if (!form.checkValidity()) { evt.preventDefault(); evt.stopPropagation(); }
      form.classList.add('was-validated');
    }, false);
  });
})();

// ========================= ADD RECORD FORM =========================
function toggleAddRequired() {
  const t = document.getElementById('recordType').value;
  const show = id => document.getElementById(id).classList.remove('d-none');
  const hide = id => document.getElementById(id).classList.add('d-none');

  // hide all optional
  ['txtField','nsField','httpsSvcbField','mxPriorityField','srvPriorityField','srvWeightField','srvPortField'].forEach(hide);
  const val = document.getElementById('recordValue');
  const help = document.getElementById('helpGeneric');
  val.required = false;
  document.getElementById('txtValue').required = false;
  document.getElementById('nsTarget').required = false;
  document.getElementById('httpsValue').required = false;
  ['mxPriority','srvPriority','srvWeight','srvPort'].forEach(id => document.getElementById(id).required = false);
  document.getElementById('valueField').classList.remove('d-none');
  help.classList.add('d-none');

  if (t === 'TXT') { show('txtField'); document.getElementById('txtValue').required = true; document.getElementById('valueField').classList.add('d-none'); }
  else if (t === 'NS') { show('nsField'); document.getElementById('nsTarget').required = true; document.getElementById('valueField').classList.add('d-none'); help.classList.remove('d-none'); }
  else if (t === 'HTTPS') { show('httpsSvcbField'); document.getElementById('httpsValue').required = true; document.getElementById('valueField').classList.add('d-none'); }
  else if (t === 'MX') { show('mxPriorityField'); val.required = true; help.classList.remove('d-none'); }
  else if (t === 'SRV') { ['srvPriorityField','srvWeightField','srvPortField'].forEach(show); val.required = true; help.classList.remove('d-none'); }
  else { val.required = true; if (t==='CNAME' || t==='AAAA' || t==='A') help.classList.remove('d-none'); }
  document.querySelectorAll('#recordValue, #txtValue, #nsTarget, #httpsValue, #mxPriority, #srvPriority, #srvWeight, #srvPort').forEach(input => {
    input.disabled = !!input.closest('.d-none');
    input.setCustomValidity('');
  });
}
toggleAddRequired();

// ========================= RECORD GROUP TOGGLES =========================
document.addEventListener('click', (e) => {
  const header = e.target.closest('tr.zone-header');
  if (!header) return;
  const group = header.getAttribute('data-group');
  const rows = document.querySelectorAll(`tr.zone-record[data-group="${group}"]`);
  const caret = header.querySelector('.caret');

  const currentlyOpen = rows.length && rows[0].style.display !== 'none';
  const willOpen = !currentlyOpen;

  rows.forEach(r => r.style.display = willOpen ? 'table-row' : 'none');

  caret.classList.toggle('fa-chevron-down', willOpen);
  caret.classList.toggle('fa-chevron-right', !willOpen);
});

// ========================= EXPAND / COLLAPSE ALL =========================
document.getElementById('btnExpandAll').addEventListener('click', () => {
    document.querySelectorAll('tr.zone-record').forEach(r => r.style.display = 'table-row');
    document.querySelectorAll('tr.zone-header .caret').forEach(i => { i.classList.remove('fa-chevron-right'); i.classList.add('fa-chevron-down'); });
});
document.getElementById('btnCollapseAll').addEventListener('click', () => {
    document.querySelectorAll('tr.zone-record').forEach(r => r.style.display = 'none');
    document.querySelectorAll('tr.zone-header .caret').forEach(i => { i.classList.add('fa-chevron-right'); i.classList.remove('fa-chevron-down'); });
});

// ========================= FILTER =========================
(function(){
    const input = document.getElementById('searchRecords');
    const clear = document.getElementById('clearSearch');
    const apply = () => {
        const q = (input.value || '').toLowerCase();
        clear.style.display = q ? 'inline-block' : 'none';
        const rows = document.querySelectorAll('tr.zone-record');
        rows.forEach(r => {
            const name = (r.querySelector('td:nth-child(1)')?.textContent || '').toLowerCase();
            const type = (r.querySelector('td:nth-child(2)')?.textContent || '').toLowerCase();
            const val  = (r.querySelector('td:nth-child(3)')?.textContent || '').toLowerCase();
            r.style.display = (name.includes(q) || type.includes(q) || val.includes(q)) ? 'table-row' : 'none';
        });
        // set carets accordingly
        document.querySelectorAll('tr.zone-header').forEach(h => {
            const grp = h.getAttribute('data-group');
            const visible = Array.from(document.querySelectorAll(`tr.zone-record[data-group="${grp}"]`)).some(rr => rr.style.display !== 'none');
            const caret = h.querySelector('.caret');
            caret.classList.toggle('fa-chevron-down', visible);
            caret.classList.toggle('fa-chevron-right', !visible);
        });
    };
    if (input) {
        input.addEventListener('input', apply);
        clear.addEventListener('click', () => { input.value=''; apply(); });
    }
})();

// Copy buttons
document.addEventListener('click', async (e) => {
    const btn = e.target.closest('.btn-copy');
    if (!btn) return;
    try {
        await navigator.clipboard.writeText(btn.getAttribute('data-value'));
        showToast('success', window.i18n.CopySuccess);
    } catch {
        showToast('danger', window.i18n.CopyFail);
    }
});

document.addEventListener('click', event => {
  const button = event.target.closest('[data-record-action]');
  if (!button || button.disabled) return;
  const d = button.dataset;
  if (d.recordAction === 'edit') openEdit(d.name, d.type, d.value, d.ttl);
  else openDelete(d.name, d.type, d.value);
});

// ========================= EDIT MODAL OPEN =========================
function openEdit(name, type, value, ttl) {
  document.getElementById('editRecordName').value = name;
  document.getElementById('editRecordType').value = type;
  document.getElementById('editRecordOldValue').value = value;
  document.getElementById('editRecordTtl').value = ttl;

  ['editValueField','editTXTField','editMxPriorityField','editSrvPriorityField','editSrvWeightField','editSrvPortField','editSoaFields']
    .forEach(id => document.getElementById(id).classList.add('d-none'));

  document.getElementById('editRecordValue').value = value;
  document.getElementById('editValueField').classList.remove('d-none');

  if (type === 'TXT') {
    const chunks = Array.from(value.matchAll(/"((?:\\.|[^"\\])*)"/g));
    const t = chunks.length ? chunks.map(c => c[1].replace(/\\(["\\])/g, '$1')).join('') : value;
    document.getElementById('editTXTValue').value = t;
    document.getElementById('editTXTField').classList.remove('d-none');
    document.getElementById('editValueField').classList.add('d-none');
    setTimeout(() => autoResizeTextarea(document.getElementById('editTXTValue')), 50);
  } else if (type === 'MX') {
    const parts = value.split(' ');
    if (parts.length >= 2) {
      document.getElementById('editMxPriority').value = parts[0];
      document.getElementById('editRecordValue').value = parts.slice(1).join(' ');
    }
    document.getElementById('editMxPriorityField').classList.remove('d-none');
  } else if (type === 'SRV') {
    const p = value.split(' ');
    if (p.length >= 4) {
      document.getElementById('editSrvPriority').value = p[0];
      document.getElementById('editSrvWeight').value   = p[1];
      document.getElementById('editSrvPort').value     = p[2];
      document.getElementById('editRecordValue').value = p.slice(3).join(' ');
    }
    ['editSrvPriorityField','editSrvWeightField','editSrvPortField'].forEach(id => document.getElementById(id).classList.remove('d-none'));
  } else if (type === 'SOA') {
    const p = value.split(' ');
    if (p.length === 7) {
      document.getElementById('editSoaNs').value         = p[0];
      document.getElementById('editSoaEmail').value      = p[1];
      document.getElementById('editSoaRefresh').value    = p[3];
      document.getElementById('editSoaRetry').value      = p[4];
      document.getElementById('editSoaExpire').value     = p[5];
      document.getElementById('editSoaMinimumTtl').value = p[6];
    }
    document.getElementById('editSoaFields').classList.remove('d-none');
    document.getElementById('editValueField').classList.add('d-none');
  }

  document.getElementById('editRecordValue').disabled = type === 'TXT' || type === 'SOA';
  document.getElementById('editTXTValue').disabled = type !== 'TXT';
  document.querySelectorAll('#editRecordModal input[type="number"]').forEach(input => {
    input.disabled = !!input.closest('.d-none');
  });
  new bootstrap.Modal(document.getElementById('editRecordModal'), { backdrop: false }).show();
}

// ========================= DELETE MODAL OPEN =========================
function openDelete(name, type, value) {
  document.getElementById('deleteText').textContent = fmt(window.i18n.DelConfirm, type, value, name);
  document.getElementById('deleteName').value  = name;
  document.getElementById('deleteType').value  = type;
  document.getElementById('deleteValue').value = value;
  new bootstrap.Modal(document.getElementById('deleteRecordModal'), { backdrop: false }).show();
}

// helpers
function autoResizeTextarea(el){ el.style.height='auto'; el.style.height = el.scrollHeight + 'px'; }
