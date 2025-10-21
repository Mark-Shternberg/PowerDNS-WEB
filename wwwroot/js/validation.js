// ===== Patterns =====
const PAT = {
    ForwardZone: /^(?!-)(?!(.*\.in-addr\.arpa\.?)$)([a-zA-Z0-9-]{1,63}\.)+[a-zA-Z]{2,63}$/,
    ReversePrefix: /^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}$/,
    IPv4: /^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$/,
    IPv6: /^(([0-9A-Fa-f]{1,4}:){7}([0-9A-Fa-f]{1,4}|:)|(([0-9A-Fa-f]{1,4}:){1,7}:)|(([0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4})|(([0-9A-Fa-f]{1,4}:){1,5}(:[0-9A-Fa-f]{1,4}){1,2})|(([0-9A-Fa-f]{1,4}:){1,4}(:[0-9A-Fa-f]{1,4}){1,3})|(([0-9A-Fa-f]{1,4}:){1,3}(:[0-9A-Fa-f]{1,4}){1,4})|(([0-9A-Fa-f]{1,4}:){1,2}(:[0-9A-Fa-f]{1,4}){1,5})|([0-9A-Fa-f]{1,4}:((:[0-9A-Fa-f]{1,4}){1,6}))|(:((:[0-9A-Fa-f]{1,4}){1,7}|:))|(::([Ff]{4}(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))|([0-9A-Fa-f]{1,4}:([0-9A-Fa-f]{1,4}:){0,5}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])))$/,
    IPv4Port: /^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}:(\d{1,5})$/,
    Subdomain: /^(?!-)[a-z0-9-]{1,63}(?<!-)$/
};

const encoder = new TextEncoder();

// ===== Helpers =====
function _isEmpty(s) { return !s || !String(s).trim(); }
function _t(path) { return (window.t ? window.t(path) : path); }

// ===== Forward zone (FQDN) =====
function validateForwardZoneName(zoneName) {
    if (_isEmpty(zoneName)) return _t('Validation.ForwardZone.Required');
    if (zoneName.length > 255) return _t('Validation.ForwardZone.TooLong');
    if (zoneName.includes('..')) return _t('Validation.ForwardZone.ConsecutiveDots');
    if (!PAT.ForwardZone.test(zoneName)) return _t('Validation.ForwardZone.Invalid');
    return '';
}

// ===== Reverse prefix (e.g., 192.168.0.) =====
function validateReverseZoneName(prefix) {
    if (_isEmpty(prefix)) return _t('Validation.ReverseZone.Required');
    if (prefix.length > 255) return _t('Validation.ReverseZone.TooLong');
    if (prefix.includes('..')) return _t('Validation.ReverseZone.ConsecutiveDots');
    if (!PAT.ReversePrefix.test(prefix)) return _t('Validation.ReverseZone.Invalid');
    return '';
}

// ===== IPv4 / IPv6 =====
function validateIPv4(ip) {
    if (_isEmpty(ip)) return _t('Validation.IPv4.Required');
    if (!PAT.IPv4.test(ip)) return _t('Validation.IPv4.Invalid');
    return '';
}

function validateIPv6(ip) {
    if (_isEmpty(ip)) return _t('Validation.IPv6.Required');
    if (!PAT.IPv6.test(ip)) return _t('Validation.IPv6.Invalid');
    return '';
}

// ===== Subdomain =====
function validateSubdomain(subdomain) {
    if (_isEmpty(subdomain)) return _t('Validation.Subdomain.Required');
    if (!PAT.Subdomain.test(subdomain)) return _t('Validation.Subdomain.Invalid');
    return '';
}

// ===== IPv4:Port =====
function validateIPv4withPort(ip) {
    const v = (ip || '').trim();
    if (!v) return _t('Validation.IPv4Port.Required');
    const m = v.match(PAT.IPv4Port);
    if (!m) return _t('Validation.IPv4Port.Invalid');
    const port = parseInt(m[4], 10);
    if (port < 1 || port > 65535) return _t('Validation.IPv4Port.PortRange');
    return '';
}

// ===== TXT =====
function validateTXT(value) {
    const v = (value || '');
    if (!v.trim()) return _t('Validation.TXT.Required');
    if (encoder.encode(v).length > 65535) return _t('Validation.TXT.TooLong');
    if (v.includes('\n') || v.includes('\r')) return _t('Validation.TXT.Newlines');
    if (v.includes('"')) return _t('Validation.TXT.Quotes');
    return '';
}

function applyPatternValidation(input, pattern, msgKey, required = true) {
    if (!input) return;
    input.setAttribute('pattern', pattern.source);
    if (!required) input.setAttribute('data-optional', '1');

    const handler = () => {
        input.setCustomValidity('');
        const val = input.value.trim();
        if (!val && input.dataset.optional === '1') return;
        if (!val || !new RegExp(pattern).test(val)) {
            input.setCustomValidity(_t(msgKey));
        }
    };
    input.addEventListener('input', handler);
    input.addEventListener('invalid', handler);
    handler();
}
