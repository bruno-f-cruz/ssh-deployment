// Lightweight, self-contained YAML syntax highlighter. Renders a read-only highlighted <pre>
// underneath a transparent <textarea>, kept in sync. No external dependencies (works offline);
// a clean seam to swap in Monaco later if richer editing is wanted.
window.shushYaml = {
    init: function (textareaId, preId) {
        const ta = document.getElementById(textareaId);
        const pre = document.getElementById(preId);
        if (!ta || !pre) return;

        const render = () => { pre.innerHTML = this.highlight(ta.value) + '\n'; };
        const sync = () => { pre.scrollTop = ta.scrollTop; pre.scrollLeft = ta.scrollLeft; };

        ta.addEventListener('input', render);
        ta.addEventListener('scroll', sync);
        render();
        sync();
    },

    highlight: function (text) {
        return text.split('\n').map(line => this.line(line)).join('\n');
    },

    line: function (raw) {
        const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

        if (/^\s*#/.test(raw)) return `<span class="tok-comment">${esc(raw)}</span>`;

        let prefix = '';
        let body = raw;
        const km = raw.match(/^(\s*(?:-\s+)?)([A-Za-z0-9_.\- ]+)(:)(\s|$)/);
        if (km) {
            prefix = esc(km[1]) + `<span class="tok-key">${esc(km[2])}</span>` + km[3] + km[4];
            body = raw.slice(km[0].length);
        }

        const re = /(\$\{[^}]*\})|("[^"]*")|('[^']*')|(#.*$)/g;
        let out = '';
        let last = 0;
        let m;
        while ((m = re.exec(body)) !== null) {
            out += esc(body.slice(last, m.index));
            const cls = m[1] ? 'tok-ref' : (m[4] ? 'tok-comment' : 'tok-str');
            out += `<span class="${cls}">${esc(m[0])}</span>`;
            last = m.index + m[0].length;
        }
        out += esc(body.slice(last));
        return prefix + out;
    }
};
