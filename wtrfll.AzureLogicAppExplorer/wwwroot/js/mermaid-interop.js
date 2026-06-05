// Mermaid interop for wtrfll.AzureLogicAppExplorer
// Loaded after mermaid.min.js from CDN (see App.razor)

window.azureLogicAppExplorer = {
    _initialized: false,

    async init() {
        if (this._initialized) return;
        mermaid.initialize({
            startOnLoad: false,
            theme: 'default',
            flowchart: { useMaxWidth: true, htmlLabels: true },
            securityLevel: 'loose',
        });
        this._initialized = true;
    },

    async render(elementId, definition) {
        await this.init();
        const el = document.getElementById(elementId);
        if (!el) return;

        try {
            // Give the element a fresh unique id for each render
            const renderId = 'mmd-' + Math.random().toString(36).slice(2);
            const { svg } = await mermaid.render(renderId, definition);
            el.innerHTML = svg;
        } catch (e) {
            el.innerHTML = `<pre class="text-danger small">Mermaid render error:\n${e.message}\n\nDefinition:\n${definition}</pre>`;
        }
    },

    copyToClipboard(text) {
        navigator.clipboard.writeText(text).catch(() => {
            // Fallback for non-HTTPS
            const ta = document.createElement('textarea');
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        });
    },

    downloadFile(filename, content) {
        const blob = new Blob([content], { type: 'text/plain' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    },

    downloadDiagramPng(filename) {
        const svgEl = document.getElementById('mermaid-diagram')?.querySelector('svg');
        if (!svgEl) return;

        // Use rendered bounding rect for pixel dimensions; fall back to viewBox
        const rect = svgEl.getBoundingClientRect();
        const vb = svgEl.viewBox?.baseVal;
        const w = rect.width  || (vb && vb.width)  || 1200;
        const h = rect.height || (vb && vb.height) || 800;

        // Inline the SVG so it renders self-contained (no external stylesheet deps)
        const clone = svgEl.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        clone.setAttribute('width', w);
        clone.setAttribute('height', h);
        const svgData = new XMLSerializer().serializeToString(clone);
        const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
        const svgUrl = URL.createObjectURL(svgBlob);

        const scale = 2; // 2× for retina / high-res export
        const canvas = document.createElement('canvas');
        canvas.width  = w * scale;
        canvas.height = h * scale;
        const ctx = canvas.getContext('2d');

        const img = new Image();
        img.onload = () => {
            ctx.fillStyle = '#ffffff';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
            URL.revokeObjectURL(svgUrl);
            const a = document.createElement('a');
            a.download = filename;
            a.href = canvas.toDataURL('image/png');
            a.click();
        };
        img.onerror = () => {
            // SVG-to-canvas failed (e.g. foreign object security); fall back to SVG download
            URL.revokeObjectURL(svgUrl);
            this.downloadDiagramSvg(filename.replace(/\.png$/i, '.svg'));
        };
        img.src = svgUrl;
    },

    downloadDiagramSvg(filename) {
        const svgEl = document.getElementById('mermaid-diagram')?.querySelector('svg');
        if (!svgEl) return;
        const clone = svgEl.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        const svgData = new XMLSerializer().serializeToString(clone);
        const blob = new Blob([svgData], { type: 'image/svg+xml' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    },
};
