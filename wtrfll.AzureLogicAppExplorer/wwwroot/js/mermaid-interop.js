// Mermaid interop for wtrfll.AzureLogicAppExplorer
// Loaded after mermaid.min.js from CDN (see App.razor)

window.azureLogicAppExplorer = {
    _initialized: false,

    async init() {
        if (this._initialized) return;

        let layout = undefined;
        try {
            const elk = await import('https://esm.sh/@mermaid-js/layout-elk@0.2.1');
            mermaid.registerLayoutLoaders(elk.default ?? elk);
            layout = 'elk';
        } catch (e) {
            console.warn('ELK layout plugin failed to load, falling back to default layout', e);
        }

        mermaid.initialize({
            startOnLoad: false,
            theme: 'default',
            layout,
            elk: { mergeEdges: false, nodePlacementStrategy: 'BRANDES_KOEPF' },
            flowchart: { useMaxWidth: true, htmlLabels: true, nodeSpacing: 80, rankSpacing: 120 },
            securityLevel: 'loose',
        });

        const style = document.createElement('style');
        style.textContent = `
            #mermaid-diagram svg .flowchart-link {
                cursor: pointer;
                transition: stroke 0.1s ease, stroke-width 0.1s ease;
            }
            #mermaid-diagram svg .flowchart-link:hover {
                stroke: #fd7e14 !important;
                stroke-width: 3px !important;
            }
        `;
        document.head.appendChild(style);

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

    // Hides/shows nodes (and their connected edges) belonging to the given
    // Mermaid classDef names. Pure client-side — no re-render needed.
    applyLegendFilter(hiddenClasses) {
        const svg = document.querySelector('#mermaid-diagram svg');
        if (!svg) return;

        const hiddenNodeIds = [];

        svg.querySelectorAll('.node').forEach(node => {
            const hide = hiddenClasses.some(c => node.classList.contains(c));
            node.style.display = hide ? 'none' : '';
            if (hide) {
                // Node ids look like "mmd-<renderId>-flowchart-<nodeName>-<index>"
                const m = node.id.match(/flowchart-(.+)-\d+$/);
                if (m) hiddenNodeIds.push(m[1]);
            }
        });

        svg.querySelectorAll('.edgePaths > path, .edge').forEach(edge => {
            const hide = hiddenNodeIds.some(nid => (edge.id || '').includes(nid));
            edge.style.display = hide ? 'none' : '';
        });

        // Edge label <g> elements carry no id of their own — the data-id
        // identifying their source/target lives on the nested .label element.
        svg.querySelectorAll('.edgeLabels > .edgeLabel').forEach(edgeLabel => {
            const dataId = edgeLabel.querySelector('.label[data-id]')?.getAttribute('data-id') || '';
            const hide = hiddenNodeIds.some(nid => dataId.includes(nid));
            edgeLabel.style.display = hide ? 'none' : '';
        });
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
        const svgRect = svgEl.getBoundingClientRect();
        const vb = svgEl.viewBox?.baseVal;
        const w = svgRect.width  || (vb && vb.width)  || 1200;
        const h = svgRect.height || (vb && vb.height) || 800;

        // Canvas export can't handle <foreignObject> (HTML labels) — Chrome taints
        // any canvas drawn from an SVG that contains one. Replace each one with a
        // native <text>/<tspan> equivalent built from its own x/y/width/height
        // (SVG user units), so the result lines up with the untouched vector
        // drawing regardless of how the SVG is currently scaled/scrolled on screen.
        const measureCtx = document.createElement('canvas').getContext('2d');
        const replacements = [...svgEl.querySelectorAll('foreignObject')].map(fo => {
            const fx = fo.x.baseVal.value;
            const fy = fo.y.baseVal.value;
            const fw = fo.width.baseVal.value;
            const fh = fo.height.baseVal.value;

            const lines = [];
            fo.querySelectorAll('p').forEach(p => {
                const text = (p.textContent || '').trim().replace(/\s+/g, ' ');
                if (!text) return;
                const cs = getComputedStyle(p);
                const fontSize = parseFloat(cs.fontSize) || 16;
                const lineHeight = parseFloat(cs.lineHeight) || fontSize * 1.5;
                measureCtx.font = `${cs.fontStyle} ${cs.fontWeight} ${fontSize}px ${cs.fontFamily}`;
                for (const line of this._wrapText(measureCtx, text, fw)) {
                    lines.push({ text: line, color: cs.color, fontFamily: cs.fontFamily, fontSize, fontWeight: cs.fontWeight, fontStyle: cs.fontStyle, lineHeight });
                }
            });
            if (lines.length === 0) return null;

            const totalHeight = lines.reduce((sum, l) => sum + l.lineHeight, 0);
            let y = fy + (fh - totalHeight) / 2 + lines[0].lineHeight / 2;

            const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            text.setAttribute('text-anchor', 'middle');
            text.setAttribute('dominant-baseline', 'central');
            for (const line of lines) {
                const tspan = document.createElementNS('http://www.w3.org/2000/svg', 'tspan');
                tspan.setAttribute('x', String(fx + fw / 2));
                tspan.setAttribute('y', String(y));
                tspan.setAttribute('fill', line.color);
                tspan.setAttribute('font-family', line.fontFamily);
                tspan.setAttribute('font-size', String(line.fontSize));
                tspan.setAttribute('font-weight', line.fontWeight);
                tspan.setAttribute('font-style', line.fontStyle);
                tspan.textContent = line.text;
                text.appendChild(tspan);
                y += line.lineHeight;
            }
            return text;
        });

        // Inline the SVG so it renders self-contained (no external stylesheet deps)
        const clone = svgEl.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        clone.setAttribute('width', w);
        clone.setAttribute('height', h);
        clone.querySelectorAll('foreignObject').forEach((fo, i) => {
            const replacement = replacements[i];
            if (replacement) fo.replaceWith(replacement);
            else fo.remove();
        });
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
            // SVG-to-canvas failed; fall back to SVG download
            URL.revokeObjectURL(svgUrl);
            this.downloadDiagramSvg(filename.replace(/\.png$/i, '.svg'));
        };
        img.src = svgUrl;
    },

    // Word-wraps `text` to fit `maxWidth` using the given canvas context's
    // current font, returning the resulting lines.
    _wrapText(ctx, text, maxWidth) {
        const words = text.split(' ');
        const lines = [];
        let line = '';
        for (const word of words) {
            const candidate = line ? `${line} ${word}` : word;
            if (line && ctx.measureText(candidate).width > maxWidth) {
                lines.push(line);
                line = word;
            } else {
                line = candidate;
            }
        }
        if (line) lines.push(line);
        return lines;
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
