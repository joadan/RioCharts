// A quick self-check for the render mode pages: run `await rioCheck()` in the browser console (or from a test
// driver). It waits for the page to become interactive (not on the static pages), then reports what the charts drew,
// whether they have a tab stop and hover layer, whether the canvas layer painted, and whether a tooltip shows.
// Clicks are left to a real pointer: synthetic events do not go through pointer capture.
window.rioCheck = async function () {
    const sleep = ms => new Promise(r => setTimeout(r, ms));
    const mode = () => document.querySelector('[data-testid=mode]')?.textContent.replace(/\s+/g, ' ').trim();
    const wantInteractive = !/^\/(static|streaming)/.test(location.pathname);
    for (let i = 0; i < 80 && wantInteractive && !/interactive: True/.test(mode() || ''); i++) {
        await sleep(250);
    }

    await sleep(800);
    const charts = [...document.querySelectorAll('.rio-chart')];
    const out = { path: location.pathname, mode: mode(), charts: charts.length };
    out.paths = charts.map(c => c.querySelectorAll('.rio-chart-svg path').length);
    out.tabStops = charts.map(c => c.querySelector('.rio-chart-svg')?.getAttribute('tabindex') ?? '-');
    out.hoverLayers = charts.filter(c => c.querySelector('.rio-hover')).length;
    const canvas = document.querySelector('.rio-canvas');
    if (canvas && canvas.width && canvas.height) {
        const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
        let painted = 0;
        for (let i = 3; i < data.length; i += 4 * 97) {
            if (data[i]) painted++;
        }

        out.canvas = `${canvas.width}x${canvas.height}, ${painted} painted samples`;
    } else {
        out.canvas = canvas ? 'empty' : 'none';
    }

    const svg = charts[0]?.querySelector('.rio-chart-svg');
    if (svg) {
        const r = svg.getBoundingClientRect();
        const x = r.left + r.width * 0.55, y = r.top + r.height * 0.5;
        const fire = type => svg.dispatchEvent(new PointerEvent(type, {
            bubbles: true, cancelable: true, clientX: x, clientY: y, pointerType: 'mouse', pointerId: 1, isPrimary: true,
        }));
        fire('pointerover');
        fire('pointermove');
        await sleep(800);
        const tip = charts[0].querySelector('.rio-tooltip:not(.rio-legend-tip)');
        out.tooltip = !tip ? 'none'
            : tip.classList.contains('rio-tooltip-visible') ? tip.innerText.replace(/\s+/g, ' ').slice(0, 40) : 'hidden';
        fire('pointerleave');
    }

    out.errorUi = getComputedStyle(document.getElementById('blazor-error-ui')).display !== 'none';
    return out;
};

// Keyboard and screen reader check for every chart on the page (the "types" page has one of each type): focuses each
// chart, presses the arrow keys and Escape, and reports its accessible name, role, what the live region announced
// and whether the tooltip followed.
window.rioKeys = async function () {
    const sleep = ms => new Promise(r => setTimeout(r, ms));
    for (let i = 0; i < 80 && !/interactive: True/.test(document.querySelector('[data-testid=mode]')?.textContent || ''); i++) {
        await sleep(250);
    }

    await sleep(800);
    const results = [];
    for (const chart of document.querySelectorAll('.rio-chart')) {
        const svg = chart.querySelector('.rio-chart-svg');
        const live = chart.querySelector('.rio-live');
        const tip = chart.querySelector('.rio-tooltip:not(.rio-legend-tip)');
        const key = k => svg.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
        const row = {
            type: chart.closest('[data-type]')?.dataset.type,
            role: svg.getAttribute('role'),
            label: svg.getAttribute('aria-label'),
            tabindex: svg.getAttribute('tabindex'),
            desc: svg.querySelector('desc')?.textContent.slice(0, 80),
        };
        if (svg.getAttribute('tabindex') === '0') {
            svg.focus();
            key('ArrowRight');
            await sleep(250);
            row.first = live?.textContent;
            key('ArrowRight');
            await sleep(250);
            row.second = live?.textContent;
            key('ArrowDown');
            await sleep(250);
            row.down = live?.textContent;
            key('End');
            await sleep(250);
            row.end = live?.textContent;
            row.tooltip = tip?.classList.contains('rio-tooltip-visible') ?? false;
            key('Escape');
            await sleep(250);
            row.afterEscape = tip?.classList.contains('rio-tooltip-visible') ?? false;
            svg.blur();
        }

        results.push(row);
    }

    return results;
};
