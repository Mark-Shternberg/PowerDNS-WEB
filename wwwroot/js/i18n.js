(function () {
    const lang = (document.documentElement.getAttribute('lang') || 'en').slice(0, 2);
    const primary = `/i18n/${lang}.json`;
    const fallback = `/i18n/en.json`;

    const dict = {};
    function set(d) { Object.assign(dict, d || {}); }
    function get(obj, path) {
        return path.split('.').reduce((o, k) => (o && o[k] != null ? o[k] : undefined), obj);
    }
    function t(key, params) {
        let s = get(dict, key) || key;
        if (params && typeof s === 'string') {
            s = s.replace(/\{(\w+)\}/g, (_, k) => (params[k] ?? ''));
        }
        return s;
    }

    window.t = t;
    window.i18nReady = fetch(primary)
        .then(r => r.ok ? r.json() : fetch(fallback).then(r => r.json()))
        .then(set)
        .catch(() => set({}));
})();
