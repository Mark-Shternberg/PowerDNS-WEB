(() => {
    'use strict';
    const ru = document.documentElement.lang === 'ru';
    const ipv4 = value => /^(?:\d{1,3}\.){3}\d{1,3}$/.test(value) &&
        value.split('.').every(x => Number(x) <= 255 && (x === '0' || !x.startsWith('0')));
    const hostname = value => value.replace(/\.$/, '').split('.').every(x =>
        x.length > 0 && x.length <= 63 && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(x));
    function validate(form) {
        const editing = form.querySelector('#editRecordType');
        const type = (editing || form.querySelector('#recordType'))?.value;
        if (!type) return;
        const value = editing ? form.querySelector('#editRecordValue') :
            form.querySelector(type === 'NS' ? '#nsTarget' : '#recordValue');
        if (!value) return;
        value.setCustomValidity('');
        if (value.disabled || value.closest('.d-none')) return;
        const text = value.value.trim();
        let valid = true;
        if (type === 'A') valid = ipv4(text);
        else if (type === 'AAAA') {
            try { valid = text.includes(':') && new URL('http://[' + text + ']/').hostname.startsWith('['); }
            catch { valid = false; }
        } else if (['NS', 'CNAME', 'PTR', 'MX', 'SRV'].includes(type)) {
            valid = text === '.' || hostname(text);
        }
        if (!valid) value.setCustomValidity(ru ? 'Проверьте формат значения для ' + type : 'Check the value format for ' + type);
    }
    document.addEventListener('input', event => {
        if (event.target.form) validate(event.target.form);
    });
    document.addEventListener('change', event => {
        if (event.target.form) validate(event.target.form);
    });
    document.addEventListener('submit', event => {
        validate(event.target);
        if (!event.target.checkValidity()) {
            event.preventDefault();
            event.target.querySelector(':invalid')?.focus();
        }
    }, true);
})();
