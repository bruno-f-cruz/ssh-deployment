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

window.shushCopy = function (text) {
    if (navigator.clipboard) navigator.clipboard.writeText(text);
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