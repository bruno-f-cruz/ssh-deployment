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
