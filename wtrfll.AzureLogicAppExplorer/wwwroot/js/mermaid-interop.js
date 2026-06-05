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
};
