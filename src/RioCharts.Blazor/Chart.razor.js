// RioCharts: the small JS side of <Chart>.
// - Measures the container (ResizeObserver) and the inherited font, so C# lays out at the real
//   size with real text widths.
// - Handles hover locally: C# sends the drawn points once per layout; pointer moves find the
//   nearest x by binary search and draw the crosshair and tooltip without a server round trip.
// - Zoom and pan preview locally; only the final range goes to .NET. Linked charts (same group)
//   share the crosshair here in the browser and the zoom through each chart's .NET side.
// Only clicks and finished zooms call back into .NET.

const states = new WeakMap();
const groups = new Map(); // group name -> Set of chart states
const SVG = 'http://www.w3.org/2000/svg';

export function init(container, dotnet) {
    if (!container) {
        return { width: 0, widths: null };
    }

    const state = {
        container,
        dotnet,
        width: Math.round(container.clientWidth),
        timer: 0,
        frame: 0,
        model: null,
        svg: container.querySelector('svg.rio-chart-svg'),
        hover: container.querySelector('svg.rio-hover'),
        tip: container.querySelector('.rio-tooltip'),
        drag: null,
        wheel: null,
        group: null,
    };

    state.observer = new ResizeObserver(() => {
        const width = Math.round(container.clientWidth);
        if (width === state.width || width <= 0) {
            return;
        }

        state.width = width;
        hide(state);
        clearTimeout(state.timer);
        state.timer = setTimeout(() => dotnet.invokeMethodAsync('OnResize', width).catch(() => { }), 60);
    });
    state.observer.observe(container);

    state.onMove = e => {
        if (state.drag) {
            dragMove(state, e);
            return;
        }

        state.lastEvent = e;
        state.hovering = true;
        if (!state.frame) {
            state.frame = requestAnimationFrame(() => {
                state.frame = 0;
                move(state, state.lastEvent);
            });
        }
    };
    state.onDown = e => {
        if (!dragStart(state, e)) {
            state.onMove(e); // touch: show the tooltip where the finger lands
        }
    };
    state.onUp = e => dragEnd(state, e);
    state.onLeave = () => {
        state.hovering = false;
        if (!state.drag) {
            hide(state);
            broadcastHover(state, null);
        }
    };
    state.onClick = e => click(state, e);
    state.onWheel = e => wheel(state, e);
    state.onDblClick = e => {
        const m = state.model;
        const pos = m ? pointer(state, e) : null;
        if (m && m.zoom && m.zoomed && pos && inPlot(m, pos, 0)) {
            commitReset(state);
        }
    };

    const svg = state.svg;
    if (svg) {
        svg.addEventListener('pointermove', state.onMove);
        svg.addEventListener('pointerdown', state.onDown);
        svg.addEventListener('pointerup', state.onUp);
        svg.addEventListener('pointercancel', state.onUp);
        svg.addEventListener('pointerleave', state.onLeave);
        svg.addEventListener('click', state.onClick);
        svg.addEventListener('dblclick', state.onDblClick);
        svg.addEventListener('wheel', state.onWheel, { passive: false });
    }

    states.set(container, state);
    return { width: state.width, widths: measureFont(container), timeZone: viewerTimeZone() };
}

export function update(container, model) {
    const state = states.get(container);
    if (!state) {
        return;
    }

    state.model = model;
    if (!state.drag) {
        resetPreview(state);
    }
    // Live data redraws often: keep the tooltip where the pointer is instead of hiding it.
    if (state.drag) {
        // keep the selection or pan preview
    } else if (state.hovering && state.lastEvent && model) {
        move(state, state.lastEvent);
    } else {
        hide(state);
    }
    joinGroup(state, model && model.group);
    if (state.svg) {
        state.svg.classList.toggle('rio-clickable', !!(model && model.click));
        state.svg.classList.toggle('rio-zoomable', !!(model && model.zoom));
    }
}

export function dispose(container) {
    const state = states.get(container);
    if (state) {
        canvasCharts.delete(state);
        clearTimeout(state.timer);
        if (state.wheel) {
            clearTimeout(state.wheel.timer);
        }

        cancelAnimationFrame(state.frame);
        state.observer.disconnect();
        joinGroup(state, null);
        const svg = state.svg;
        if (svg) {
            svg.removeEventListener('pointermove', state.onMove);
            svg.removeEventListener('pointerdown', state.onDown);
            svg.removeEventListener('pointerup', state.onUp);
            svg.removeEventListener('pointercancel', state.onUp);
            svg.removeEventListener('pointerleave', state.onLeave);
            svg.removeEventListener('click', state.onClick);
            svg.removeEventListener('dblclick', state.onDblClick);
            svg.removeEventListener('wheel', state.onWheel);
        }

        states.delete(container);
    }
}

// ---------- Linked charts ----------

function joinGroup(state, name) {
    if (state.group === (name || null)) {
        return;
    }

    if (state.group && groups.has(state.group)) {
        groups.get(state.group).delete(state);
    }

    state.group = name || null;
    if (state.group) {
        if (!groups.has(state.group)) {
            groups.set(state.group, new Set());
        }

        groups.get(state.group).add(state);
    }
}

function others(state) {
    return state.group && groups.has(state.group) ? [...groups.get(state.group)].filter(s => s !== state && s.model) : [];
}

// Shows the crosshair (no tooltip) at a data x in the other charts of the group; null hides it.
function broadcastHover(state, dataX) {
    for (const other of others(state)) {
        const m = other.model;
        if (dataX === null || !m.xscale || !m.series || m.series.length === 0) {
            clearHover(other);
            continue;
        }

        const px = toPixel(m.xscale, dataX);
        const rows = px >= m.plot.x - 1 && px <= m.plot.x + m.plot.w + 1 ? findShared(m, px) : [];
        if (rows.length === 0) {
            clearHover(other);
        } else {
            drawHover(other, rows, m);
        }
    }
}

// ---------- Scales ----------

function toPixel(s, v) {
    return s.r0 + (v - s.d0) / (s.d1 - s.d0) * (s.r1 - s.r0);
}

function toData(s, px) {
    return s.d0 + (px - s.r0) / (s.r1 - s.r0) * (s.d1 - s.d0);
}

// Pointer position in chart (viewBox) units, not limited to the plot.
function pointer(state, e) {
    if (!state.svg) {
        return null;
    }

    const rect = state.svg.getBoundingClientRect();
    const vb = state.svg.viewBox.baseVal;
    const k = vb && vb.width && rect.width ? vb.width / rect.width : 1;
    return { x: (e.clientX - rect.left) * k, y: (e.clientY - rect.top) * k, k, rect };
}

function inPlot(m, pos, margin) {
    const p = m.plot;
    return pos.x >= p.x - margin && pos.x <= p.x + p.w + margin && pos.y >= p.y - margin && pos.y <= p.y + p.h + margin;
}

// Pointer position in chart units, or null when outside the plot.
function toChart(state, e) {
    const m = state.model;
    const pos = m ? pointer(state, e) : null;
    return pos && inPlot(m, pos, 6) ? pos : null;
}

// ---------- Zoom and pan ----------

function capture(state, e) {
    try {
        state.svg.setPointerCapture(e.pointerId);
    } catch {
        // not an active pointer (e.g. synthetic events)
    }
}

// A draggable reference line under the pointer: move it (mouse or touch).
function annotationDragStart(state, e) {
    const m = state.model;
    const group = e.target && e.target.closest ? e.target.closest('[data-key^="annotation:"]') : null;
    const handle = group && m && m.drags ? m.drags.find(d => 'annotation:' + d.id === group.getAttribute('data-key')) : null;
    const pos = handle ? pointer(state, e) : null;
    if (!pos || e.button !== 0) {
        return false;
    }

    state.suppressClick = false;
    state.drag = { mode: 'annotation', handle, group, start: handle.x ? pos.x : pos.y, to: handle.pos, moved: false, id: e.pointerId };
    capture(state, e);
    e.preventDefault();
    return true;
}

function dragStart(state, e) {
    if (annotationDragStart(state, e)) {
        return true;
    }

    const m = state.model;
    if (!m || !m.zoom || !m.xscale || e.button !== 0 || e.pointerType !== 'mouse') {
        return false;
    }

    const pos = pointer(state, e);
    if (!pos || !inPlot(m, pos, 0)) {
        return false;
    }

    state.suppressClick = false;
    state.drag = { mode: e.shiftKey ? 'pan' : 'select', x0: pos.x, x1: pos.x, moved: false, id: e.pointerId };
    try {
        state.svg.setPointerCapture(e.pointerId);
    } catch {
        // not an active pointer (e.g. synthetic events)
    }
    e.preventDefault();
    return true;
}

function dragMove(state, e) {
    const d = state.drag;
    const m = state.model;
    const pos = pointer(state, e);
    if (!pos) {
        return;
    }

    if (d.mode === 'annotation') {
        const h = d.handle;
        const lo = Math.min(h.s.r0, h.s.r1), hi = Math.max(h.s.r0, h.s.r1);
        d.to = Math.max(lo, Math.min(hi, h.pos + (h.x ? pos.x : pos.y) - d.start));
        d.moved = d.moved || Math.abs(d.to - h.pos) > 1;
        d.group.style.transform = h.x ? `translateX(${d.to - h.pos}px)` : `translateY(${d.to - h.pos}px)`;
        hide(state);
        return;
    }

    const p = m.plot;
    d.x1 = d.mode === 'pan' ? pos.x : Math.max(p.x, Math.min(p.x + p.w, pos.x));
    d.moved = d.moved || Math.abs(pos.x - d.x0) > 3;
    if (!d.moved) {
        return;
    }

    hide(state);
    if (d.mode === 'pan') {
        movePreview(state, d.x1 - d.x0);
    } else if (state.hover) {
        const x = Math.min(d.x0, d.x1), w = Math.abs(d.x1 - d.x0);
        state.hover.replaceChildren(el('rect', { class: 'rio-zoom-select', x, y: p.y, width: w, height: p.h }));
    }
}

function dragEnd(state) {
    const d = state.drag;
    if (!d) {
        return;
    }

    state.drag = null;
    if (state.svg.hasPointerCapture && state.svg.hasPointerCapture(d.id)) {
        state.svg.releasePointerCapture(d.id);
    }

    if (state.hover) {
        state.hover.replaceChildren();
    }

    if (d.mode === 'annotation') {
        if (d.moved) {
            state.suppressClick = true;
            const value = toData(d.handle.s, d.to);
            state.dotnet.invokeMethodAsync('OnAnnotationDragged', d.handle.id, value).catch(() => { });
        }

        return; // the line stays where it was dropped until .NET lays out again
    }

    const s = state.model.xscale;
    if (!d.moved || !s) {
        resetPreview(state);
        return;
    }

    state.suppressClick = true; // the click that ends a drag is not a point click
    if (d.mode === 'pan') {
        const shift = toData(s, s.r0) - toData(s, s.r0 + (d.x1 - d.x0));
        commitRange(state, s.d0 + shift, s.d1 + shift);
    } else if (Math.abs(d.x1 - d.x0) >= 6) {
        commitRange(state, toData(s, Math.min(d.x0, d.x1)), toData(s, Math.max(d.x0, d.x1)));
    }
}

// Ctrl/Cmd + wheel zooms around the pointer; the plain wheel keeps scrolling the page.
function wheel(state, e) {
    const m = state.model;
    if (!m || !m.zoom || !m.xscale || !(e.ctrlKey || e.metaKey)) {
        return;
    }

    const pos = pointer(state, e);
    if (!pos || !inPlot(m, pos, 0)) {
        return;
    }

    e.preventDefault();
    hide(state);
    const s = m.xscale;
    const w = state.wheel || (state.wheel = { d0: s.d0, d1: s.d1, timer: 0 });
    const anchor = toData(s, pos.x);
    const factor = Math.pow(1.0015, e.deltaY);
    w.d0 = anchor - (anchor - w.d0) * factor;
    w.d1 = anchor + (w.d1 - anchor) * factor;
    clearTimeout(w.timer);
    w.timer = setTimeout(() => {
        state.wheel = null;
        commitRange(state, w.d0, w.d1);
    }, 140);
}

function commitRange(state, min, max) {
    for (const s of [state, ...others(state).filter(o => o.model.zoom)]) {
        s.dotnet.invokeMethodAsync('OnZoom', min, max).catch(() => { });
    }
}

function commitReset(state) {
    for (const s of [state, ...others(state).filter(o => o.model.zoom)]) {
        s.dotnet.invokeMethodAsync('OnZoomReset').catch(() => { });
    }
}

// Pan preview: slide the series (the clip stays put) until .NET lays out the new range.
function movePreview(state, dx) {
    const plot = state.svg.querySelector('.rio-plot');
    if (plot) {
        for (const child of plot.children) {
            child.style.transform = `translateX(${dx}px)`;
        }
    }

    const labels = state.svg.querySelector('.rio-labels');
    if (labels) {
        labels.style.opacity = '0';
    }

    if (state.canvasScene) {
        paintCanvas(state, dx);
    }
}

function resetPreview(state) {
    if (!state.svg) {
        return;
    }

    if (state.canvasScene) {
        paintCanvas(state, 0);
    }

    const plot = state.svg.querySelector('.rio-plot');
    if (plot) {
        for (const child of plot.children) {
            child.style.transform = '';
        }
    }

    const labels = state.svg.querySelector('.rio-labels');
    if (labels) {
        labels.style.opacity = '';
    }
}

// ---------- Hit-testing ----------

// Index of the value in the ascending array closest to v (-1 when empty).
function nearest(xs, v) {
    let lo = 0, hi = xs.length - 1;
    if (hi < 0) {
        return -1;
    }

    while (lo < hi) {
        const mid = (lo + hi) >> 1;
        if (xs[mid] < v) {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }

    return lo > 0 && v - xs[lo - 1] <= xs[lo] - v ? lo - 1 : lo;
}

// Shared: the x position closest to the pointer, and each series' point at that x.
function findShared(m, px) {
    let anchor = null;
    for (const s of m.series) {
        const i = nearest(s.x, px);
        if (i >= 0 && (anchor === null || Math.abs(s.x[i] - px) < Math.abs(anchor - px))) {
            anchor = s.x[i];
        }
    }

    if (anchor === null) {
        return [];
    }

    const rows = [];
    for (const s of m.series) {
        const i = nearest(s.x, anchor);
        if (i >= 0 && Math.abs(s.x[i] - anchor) <= s.snap) {
            rows.push({ s, i });
        }
    }

    return rows;
}

// Single: the bar under the pointer, else the nearest point within reach.
function findSingle(m, px, py) {
    let best = null;
    for (const s of m.series) {
        const i = nearest(s.x, s.bar ? px - s.barOffset : px);
        if (i < 0) {
            continue;
        }

        // Check neighbours too: the nearest x is not always the nearest point.
        for (let j = Math.max(0, i - 2); j <= Math.min(s.x.length - 1, i + 2); j++) {
            let d;
            if (s.bar) {
                const dx = Math.abs(s.x[j] + s.barOffset - px);
                if (dx > s.barWidth / 2 + 1) {
                    continue;
                }

                d = dx;
            } else {
                d = Math.hypot(s.x[j] - px, s.y[j] - py);
                if (d > 40) {
                    continue;
                }
            }

            if (best === null || d < best.d) {
                best = { s, i: j, d };
            }
        }
    }

    return best ? [best] : [];
}

function find(m, pos) {
    return m.shared ? findShared(m, pos.x) : findSingle(m, pos.x, pos.y);
}

// ---------- Hover drawing ----------

// ---------- Pie slices and radial bars ----------

const TAU = Math.PI * 2;

// The slice under the pointer: between its radii and its angles (clockwise from 12 o'clock).
function findSlice(m, pos) {
    for (const s of m.slices) {
        const dx = pos.x - s.cx, dy = pos.y - s.cy;
        const d = Math.hypot(dx, dy);
        if (d < s.r0 - 1 || d > s.r1 + 3) {
            continue;
        }

        const angle = ((Math.atan2(dx, -dy) - s.a0) % TAU + TAU) % TAU;
        if (angle <= s.end - s.a0 + 1e-6 || s.end - s.a0 >= TAU - 1e-6) {
            return s;
        }
    }

    return null;
}

function arcPoint(cx, cy, r, a) {
    return [cx + Math.sin(a) * r, cy - Math.cos(a) * r];
}

// SVG path of a ring segment between two radii.
function ringPath(cx, cy, r0, r1, a0, a1) {
    const f = v => Math.round(v * 100) / 100;
    const arc = (r, from, to) => {
        const sweep = to - from;
        if (Math.abs(sweep) >= TAU - 1e-6) {
            const mid = from + sweep / 2;
            return arc(r, from, mid) + arc(r, mid, to);
        }

        const [x, y] = arcPoint(cx, cy, r, to);
        return ` A${f(r)} ${f(r)} 0 ${Math.abs(sweep) > Math.PI ? 1 : 0} ${sweep > 0 ? 1 : 0} ${f(x)} ${f(y)}`;
    };
    const [x0, y0] = arcPoint(cx, cy, r1, a0);
    const [x1, y1] = arcPoint(cx, cy, r0, a1);
    return `M${f(x0)} ${f(y0)}` + arc(r1, a0, a1) + ` L${f(x1)} ${f(y1)}` + (r0 > 0 ? arc(r0, a1, a0) : '') + ' Z';
}

function movePolar(state, m, pos) {
    const s = pos ? findSlice(m, pos) : null;
    if (!s) {
        hide(state);
        return;
    }

    if (state.hover) {
        // A thin halo just outside the hovered slice.
        state.hover.replaceChildren(el('path', {
            class: 'rio-hover-halo', d: ringPath(s.cx, s.cy, s.r1 + 1.5, s.r1 + 5, s.a0, s.a1), style: `fill:${s.color}`,
        }));
    }

    const tip = state.tip;
    if (!tip) {
        return;
    }

    tip.replaceChildren();
    if (s.header) {
        const head = document.createElement('div');
        head.className = 'rio-tooltip-head';
        head.textContent = s.header;
        tip.appendChild(head);
    }

    const row = document.createElement('div');
    row.className = 'rio-tooltip-row';
    const dot = document.createElement('span');
    dot.className = 'rio-tooltip-dot';
    dot.style.background = s.color;
    const name = document.createElement('span');
    name.className = 'rio-tooltip-name';
    name.textContent = s.label;
    const value = document.createElement('span');
    value.className = 'rio-tooltip-value';
    value.textContent = s.value;
    row.append(dot, name, value);
    tip.appendChild(row);
    placeTooltip(state, pos, pos.x, pos.y);
}

function move(state, e) {
    const m = state.model;
    if (m && m.slices && m.slices.length > 0) {
        movePolar(state, m, m.tooltip ? toChart(state, e) : null);
        return;
    }

    const pos = m && m.tooltip ? toChart(state, e) : null;
    const rows = pos ? find(m, pos) : [];
    if (rows.length === 0) {
        hide(state);
        broadcastHover(state, null);
        return;
    }

    drawHover(state, rows, m);
    drawTooltip(state, rows, pos);
    if (state.group && m.xscale) {
        broadcastHover(state, toData(m.xscale, rows[0].s.x[rows[0].i]));
    }
}

function drawHover(state, rows, m) {
    const g = state.hover;
    if (!g) {
        return;
    }

    g.replaceChildren();
    const p = m.plot;
    const first = rows[0];
    const x = first.s.x[first.i];
    if (m.band > 0 && (m.shared || first.s.bar)) {
        const w = m.shared ? m.band : first.s.barWidth + 4;
        const cx = m.shared ? x : x + first.s.barOffset;
        g.appendChild(el('rect', {
            class: 'rio-hover-band', x: cx - w / 2, y: p.y, width: w, height: p.h, rx: 3,
        }));
    } else {
        g.appendChild(el('line', {
            class: 'rio-crosshair', x1: x, x2: x, y1: p.y, y2: p.y + p.h,
        }));
    }

    for (const { s, i } of rows) {
        if (!s.bar) {
            g.appendChild(el('circle', {
                class: 'rio-hover-point', cx: s.x[i], cy: s.y[i], r: 4, style: `fill:${s.color}`,
            }));
        }
    }
}

function drawTooltip(state, rows, pos) {
    const tip = state.tip;
    if (!tip) {
        return;
    }

    const first = rows[0];
    tip.replaceChildren();
    const head = document.createElement('div');
    head.className = 'rio-tooltip-head';
    head.textContent = first.s.xl[first.i];
    tip.appendChild(head);
    for (const { s, i } of rows) {
        const row = document.createElement('div');
        row.className = 'rio-tooltip-row';
        const dot = document.createElement('span');
        dot.className = 'rio-tooltip-dot';
        dot.style.background = s.color;
        const name = document.createElement('span');
        name.className = 'rio-tooltip-name';
        name.textContent = s.name;
        const value = document.createElement('span');
        value.className = 'rio-tooltip-value';
        value.textContent = s.v[i];
        row.append(dot, name, value);
        tip.appendChild(row);
    }

    placeTooltip(state, pos, first.s.x[first.i], pos.y);
}

// Beside x (right, or left when there is no room), vertically centered on y (chart units).
function placeTooltip(state, pos, x, y) {
    const tip = state.tip;
    const containerRect = state.container.getBoundingClientRect();
    const offsetX = pos.rect.left - containerRect.left;
    const offsetY = pos.rect.top - containerRect.top;
    const anchorX = offsetX + x / pos.k;
    tip.classList.add('rio-tooltip-visible');
    const w = tip.offsetWidth, h = tip.offsetHeight;
    const cw = state.container.clientWidth, ch = state.container.clientHeight;
    let left = anchorX + 14;
    if (left + w > cw) {
        left = anchorX - 14 - w;
    }

    left = Math.max(0, Math.min(left, cw - w));
    const top = Math.max(0, Math.min(offsetY + y / pos.k - h / 2, ch - h));
    tip.style.transform = `translate(${Math.round(left)}px, ${Math.round(top)}px)`;
}

function clearHover(state) {
    if (state.hover) {
        state.hover.replaceChildren();
    }
}

function hide(state) {
    if (state.hover) {
        state.hover.replaceChildren();
    }

    if (state.tip) {
        state.tip.classList.remove('rio-tooltip-visible');
    }
}

function el(name, attrs) {
    const node = document.createElementNS(SVG, name);
    for (const [k, v] of Object.entries(attrs)) {
        node.setAttribute(k, typeof v === 'number' ? Math.round(v * 100) / 100 : v);
    }

    return node;
}

// ---------- Clicks ----------

function click(state, e) {
    if (state.suppressClick) {
        state.suppressClick = false;
        return;
    }

    const m = state.model;
    if (!m || !m.click) {
        return;
    }

    const pos = toChart(state, e);
    if (!pos) {
        return;
    }

    if (m.slices && m.slices.length > 0) {
        const s = findSlice(m, pos);
        if (s && s.index >= 0) {
            e.stopPropagation();
            state.dotnet.invokeMethodAsync('OnPointClicked', s.id, s.index).catch(() => { });
        }

        return;
    }

    // Only a click on (or right next to) a point or bar counts, so clicks elsewhere still reach
    // annotations. Of several candidates, the closest wins.
    const hits = find(m, pos)
        .map(r => {
            const s = r.s;
            if (s.bar) {
                const dx = Math.abs(s.x[r.i] + s.barOffset - pos.x);
                return { ...r, d: dx <= s.barWidth / 2 + 1 && pos.y >= s.y[r.i] - 4 ? dx : Infinity };
            }

            return { ...r, d: Math.hypot(s.x[r.i] - pos.x, s.y[r.i] - pos.y) };
        })
        .filter(r => r.d <= 16 || (r.s.bar && r.d !== Infinity))
        .sort((a, b) => a.d - b.d);

    if (hits.length > 0) {
        // Points win over annotations underneath (Blazor handles clicks at the document root, after this).
        e.stopPropagation();
        const { s, i } = hits[0];
        state.dotnet.invokeMethodAsync('OnPointClicked', s.id, s.index[i]).catch(() => { });
    }
}

// ---------- Font ----------

// Width of each printable ASCII character (32..126) relative to the font size.
function measureFont(container) {
    try {
        const style = getComputedStyle(container);
        const context = document.createElement('canvas').getContext('2d');
        context.font = `${style.fontWeight || 400} 100px ${style.fontFamily || 'sans-serif'}`;
        const widths = [];
        for (let code = 32; code <= 126; code++) {
            widths.push(context.measureText(String.fromCharCode(code)).width / 100);
        }

        return widths;
    } catch {
        return null;
    }
}

// The browser's IANA time zone (for TimeZone="local" and to show the zone when it differs).
function viewerTimeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
        return null;
    }
}

// ---------- Canvas series layer ----------

const canvasCharts = new Set();
let themeObserver = null;

// Draw operations for the series (SVG path data, rectangles, CSS paint) from .NET.
export function paint(container, scene) {
    const state = states.get(container);
    if (!state) {
        return;
    }

    state.canvasScene = scene;
    canvasCharts.add(state);
    watchTheme();
    paintCanvas(state, 0);
}

// Theme switches (a class or data-theme attribute, or the OS setting) change the CSS colors the
// canvas resolved: repaint.
function watchTheme() {
    if (themeObserver) {
        return;
    }

    const repaint = () => requestAnimationFrame(() => canvasCharts.forEach(s => s.canvasScene && paintCanvas(s, 0)));
    themeObserver = new MutationObserver(repaint);
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'], subtree: true });
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', repaint);
    }
}

function paintCanvas(state, dx) {
    const canvas = state.container.querySelector('canvas.rio-canvas');
    const scene = state.canvasScene;
    if (!canvas || !scene || !state.svg) {
        return;
    }

    const box = canvas.getBoundingClientRect();
    const vb = state.svg.viewBox.baseVal;
    const k = vb && vb.width ? state.svg.getBoundingClientRect().width / vb.width : 1;
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.round(box.width * dpr)), h = Math.max(1, Math.round(box.height * dpr));
    if (canvas.width !== w) {
        canvas.width = w;
    }

    if (canvas.height !== h) {
        canvas.height = h;
    }

    const ctx = canvas.getContext('2d');
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.setTransform(dpr * k, 0, 0, dpr * k, 0, 0);
    if (scene.clip) {
        ctx.beginPath();
        ctx.rect(scene.clip[0], scene.clip[1], scene.clip[2], scene.clip[3]);
        ctx.clip();
    }

    ctx.translate(dx, 0);
    const colors = new Map();
    const resolve = css => {
        if (!colors.has(css)) {
            canvas.style.color = css;
            colors.set(css, getComputedStyle(canvas).color);
            canvas.style.color = '';
        }

        return colors.get(css);
    };

    for (const op of scene.ops) {
        const s = op.shape;
        let path;
        if (s.t === 'p') {
            path = new Path2D(s.d);
        } else {
            path = new Path2D();
            if (s.rx > 0 && path.roundRect) {
                path.roundRect(s.x, s.y, s.w, s.h, s.rx);
            } else {
                path.rect(s.x, s.y, s.w, s.h);
            }
        }

        const alpha = op.op == null ? 1 : op.op;
        if (op.fill && op.fill !== 'none' && op.fill !== 'transparent') {
            ctx.globalAlpha = alpha * (op.fo == null ? 1 : op.fo);
            ctx.fillStyle = resolve(op.fill);
            ctx.fill(path);
        }

        if (op.stroke && op.stroke !== 'none' && op.stroke !== 'transparent') {
            ctx.globalAlpha = alpha;
            ctx.strokeStyle = resolve(op.stroke);
            ctx.lineWidth = op.sw;
            ctx.lineJoin = 'round';
            ctx.lineCap = 'round';
            ctx.setLineDash(op.dash ? op.dash.split(/[ ,]+/).map(Number) : []);
            ctx.stroke(path);
        }
    }

    ctx.globalAlpha = 1;
}
