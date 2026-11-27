window.scrollLogToBottom = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
};

window.scrollToId = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

window.downloadFile = function (fileName, content, mimeType) {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
};

window.shushTimezoneOffset = function () {
    return new Date().getTimezoneOffset();
};

window.shushCopy = async function (text) {
    // The clipboard API needs a secure context (HTTPS or localhost); over plain HTTP fall back to a hidden textarea.
    if (navigator.clipboard && window.isSecureContext) {
        try { await navigator.clipboard.writeText(text); return; } catch { /* fall through */ }
    }
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); } finally { area.remove(); }
};


// "⋯" menus (<details class="menu">): close on picking an item, clicking outside, or Escape.
(function () {
    const closeAll = except => document.querySelectorAll('details.menu[open]').forEach(d => { if (d !== except) d.removeAttribute('open'); });
    document.addEventListener('click', e => {
        const menu = e.target.closest('details.menu');
        if (e.target.closest('.menu__item')) { closeAll(null); return; }
        closeAll(menu);
    });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') closeAll(null); });
})();

// Table rows with a .row-link anchor: clicking anywhere on the row follows it (unless the click was on a control).
document.addEventListener('click', e => {
    const row = e.target.closest('tr.is-link');
    if (!row || e.target.closest('a, button, input, select, textarea, label, summary')) return;
    const link = row.querySelector('a.row-link');
    if (link) link.click();
});

// Live timers (<span data-since="ISO time">): tick every second, formatted like Ui.Duration (45s, 2m 05s, 1h 03m).
(function () {
    const format = ms => {
        const s = Math.max(0, Math.floor(ms / 1000));
        if (s < 60) return `${s}s`;
        if (s < 3600) return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`;
        return `${Math.floor(s / 3600)}h ${String(Math.floor(s / 60) % 60).padStart(2, '0')}m`;
    };
    setInterval(() => {
        const now = Date.now();
        document.querySelectorAll('[data-since]').forEach(el => {
            const since = Date.parse(el.dataset.since);
            if (!isNaN(since)) el.textContent = format(now - since);
        });
    }, 1000);
})();