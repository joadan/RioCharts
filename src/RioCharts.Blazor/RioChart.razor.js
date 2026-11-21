// RioCharts: the small JS side of <RioChart>.
// - Measures the container (ResizeObserver) and the inherited font, so C# lays out at the real
//   size with real text widths.
// - Handles hover locally: C# sends the drawn points once per layout; pointer moves find the
//   nearest x by binary search and draw the crosshair and tooltip without a server round trip.
// - Zoom and pan preview locally; only the final range goes to .NET. Linked charts (same group)
//   share the crosshair here in the browser and the zoom through each chart's .NET side.
// - With a TooltipTemplate, .NET renders the tooltip content: JS asks for it only when the hovered
//   points change (one call in flight, the latest wins) and places the card when it arrives.
// - Keyboard: with the chart focused, arrow keys move between the drawn points (or slices) here,
//   drawing the crosshair and tooltip and announcing the point through a live region.
// - Export: copies the drawn chart into a standalone SVG (theme colors resolved) and rasterizes it
//   for PNG, so images match the page's CSS; downloads are saved here without a round trip.
// Only clicks, finished zooms and (with a template) hovered-point changes call back into .NET.

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
        tip: container.querySelector('.rio-tooltip:not(.rio-legend-tip)'),
        legendTip: container.querySelector('.rio-legend-tip'),
        legendItem: null,
        drag: null,
        wheel: null,
        group: null,
        touches: new Map(), // touch pointers down on the chart: id -> x in chart units
        touchRest: false,
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

    // Template tooltips: place the card when .NET has rendered new content.
    state.tipObserver = new MutationObserver(() => showTemplate(state));
    if (state.tip) {
        state.tipObserver.observe(state.tip, { childList: true, subtree: true, characterData: true });
    }

    state.onMove = e => {
        if (e.pointerType === 'touch' && state.touches.has(e.pointerId)) {
            const pos = pointer(state, e);
            if (pos) {
                state.touches.set(e.pointerId, pos.x);
            }
        }

        if (state.drag) {
            dragMove(state, e);
            return;
        }

        if (state.touchRest && e.pointerType === 'touch') {
            return; // the finger left on the chart after a pinch shows no tooltip
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
        if (e.pointerType === 'touch') {
            const pos = pointer(state, e);
            if (pos) {
                state.touches.set(e.pointerId, pos.x);
            }

            if (pinchStart(state, e)) {
                return;
            }
        }

        if (!dragStart(state, e)) {
            state.onMove(e); // touch: show the tooltip where the finger lands
        }
    };
    state.onUp = e => {
        state.touches.delete(e.pointerId);
        dragEnd(state, e);
        if (state.touches.size === 0) {
            state.touchRest = false;
        }
    };
    state.onLeave = () => {
        state.hovering = false;
        if (!state.drag) {
            hide(state);
            broadcastHover(state, null);
            showShown(state);
        }
    };
    state.onClick = e => click(state, e);
    state.onKey = e => keyDown(state, e);
    state.onBlur = () => blurKeys(state);
    state.onMenuKey = e => menuKey(e);
    state.onTipOver = e => showLegendTip(state, e.target);
    state.onTipOut = e => hideLegendTip(state, e.relatedTarget);
    container.addEventListener('keydown', state.onMenuKey);
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
        svg.addEventListener('keydown', state.onKey);
        svg.addEventListener('blur', state.onBlur);
        svg.addEventListener('pointerover', state.onTipOver);
        svg.addEventListener('pointerout', state.onTipOut);
        svg.addEventListener('focusin', state.onTipOver);
        svg.addEventListener('focusout', state.onTipOut);
    }

    states.set(container, state);
    return { width: state.width, widths: measureFont(container), timeZone: viewerTimeZone() };
}

/** Payloads arrive as UTF-8 JSON bytes (source-generated on the .NET side, safe for trimming and AOT). */
const utf8 = new TextDecoder();
function json(bytes) {
    return bytes instanceof Uint8Array ? JSON.parse(utf8.decode(bytes)) : bytes;
}

export function update(container, model) {
    model = json(model);
    const state = states.get(container);
    if (!state) {
        return;
    }

    state.model = model;
    state.tipKey = null; // new layout: indexes may point at other data
    if (!state.drag) {
        resetPreview(state);
    }
    // Live data redraws often: keep the tooltip where the pointer is instead of hiding it.
    if (state.drag) {
        // keep the selection or pan preview
    } else if (state.hovering && state.lastEvent && model) {
        move(state, state.lastEvent);
    } else if (state.kb && model && model.keys && document.activeElement === state.svg) {
        // Live data or a resize: stay on the focused point. After a key zoom: on the same x.
        const anchor = state.kbAnchor;
        state.kbAnchor = null;
        if (anchor !== null && anchor !== undefined && model.xscale && state.kb.s !== undefined) {
            const s = model.series.filter(x => x.x.length > 0)[state.kb.s];
            if (s) {
                state.kb.i = Math.max(0, nearest(s.x, toPixel(model.xscale, anchor)));
            }
        }

        showKey(state, anchor !== null && anchor !== undefined);
    } else {
        state.kb = null;
        hide(state);
        showShown(state);
    }
    joinGroup(state, model && model.group);
    alignGroup(state.group);
    if (state.svg) {
        state.svg.classList.toggle('rio-clickable', !!(model && model.click));
        state.svg.classList.toggle('rio-zoomable', !!(model && (model.zoom || model.select === 'range')));
    }
}

// ShowTooltipAsync: the crosshair and tooltip at an x (chart units), as if hovered. It stays until
// hideTooltip, and comes back when the pointer leaves the chart (or after a new layout).
export function showTooltip(container, px) {
    const state = states.get(container);
    if (state) {
        state.shown = px;
        if (!state.hovering && !state.drag) {
            showShown(state);
        }
    }
}

export function hideTooltip(container) {
    const state = states.get(container);
    if (state && state.shown !== null && state.shown !== undefined) {
        state.shown = null;
        if (!state.hovering && !state.drag) {
            hide(state);
            broadcastHover(state, null);
        }
    }
}

function showShown(state) {
    const m = state.model, px = state.shown;
    if (px === null || px === undefined || !m || !m.series || !state.svg || (m.slices && m.slices.length > 0)) {
        return;
    }

    let rows = findShared(m, px);
    if (!m.shared) {
        rows = rows.slice(0, 1);
    }

    if (rows.length === 0) {
        hide(state);
        return;
    }

    const first = rows[0];
    rows.anchor = first.s.x[first.i];
    const rect = state.svg.getBoundingClientRect();
    const vb = state.svg.viewBox.baseVal;
    const k = vb && vb.width && rect.width ? vb.width / rect.width : 1;
    drawHover(state, rows, m);
    if (m.tooltip) {
        drawTooltip(state, rows, { x: rows.anchor, y: first.s.y[first.i], k, rect });
    }

    if (state.group && m.xscale) {
        broadcastHover(state, toData(m.xscale, rows.anchor));
    }
}

// The Reset zoom button: resets this chart and the zoomable charts linked to it.
export function resetZoom(container) {
    const state = states.get(container);
    if (!state) {
        return false;
    }

    commitReset(state);
    return true;
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
        container.removeEventListener('keydown', state.onMenuKey);
        state.observer.disconnect();
        state.tipObserver.disconnect();
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
            svg.removeEventListener('keydown', state.onKey);
            svg.removeEventListener('blur', state.onBlur);
            svg.removeEventListener('pointerover', state.onTipOver);
            svg.removeEventListener('pointerout', state.onTipOut);
            svg.removeEventListener('focusin', state.onTipOver);
            svg.removeEventListener('focusout', state.onTipOut);
        }

        states.delete(container);
    }
}

// ---------- Linked charts ----------

function joinGroup(state, name) {
    if (state.group === (name || null)) {
        return;
    }

    const left = state.group;
    if (left && groups.has(left)) {
        groups.get(left).delete(state);
    }

    state.group = name || null;
    state.insets = null;
    if (left) {
        alignGroup(left); // the others may need less space now
    }

    if (state.group) {
        if (!groups.has(state.group)) {
            groups.set(state.group, new Set());
        }

        groups.get(state.group).add(state);
    }
}

// Lines up the plots of linked charts: each gets the largest left and right insets any of them
// needs (e.g. for wider y labels), so the same x is at the same place in all. Asks .NET to lay
// out again only when a chart's insets change.
function alignGroup(name) {
    const members = name && groups.has(name) ? [...groups.get(name)].filter(s => s.model && s.model.need) : [];
    if (members.length === 0) {
        return;
    }

    const l = Math.max(...members.map(s => s.model.need.l));
    const r = Math.max(...members.map(s => s.model.need.r));
    const same = (a, b) => !!a && !!b && Math.abs(a.l - b.l) <= 0.5 && Math.abs(a.r - b.r) <= 0.5;
    for (const s of members) {
        // What the last layout used: its own needs, raised to the minimum .NET applied (model.at).
        const m = s.model;
        const now = { l: Math.max(m.need.l, m.at ? m.at.l : 0), r: Math.max(m.need.r, m.at ? m.at.r : 0) };
        if (!same(now, { l, r }) && !same(s.insets, { l, r })) {
            s.insets = { l, r }; // asked for; not again until it changes
            s.dotnet.invokeMethodAsync('OnGroupInsets', l, r).catch(() => { });
        }
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
    const range = !!(m && m.select === 'range');
    if (!m || !(m.zoom || range) || !m.xscale || e.button !== 0 || e.pointerType !== 'mouse') {
        return false;
    }

    const pos = pointer(state, e);
    if (!pos || !inPlot(m, pos, 0)) {
        return false;
    }

    // With Selection="Range" a drag selects (Shift+drag still pans a zoomable chart).
    state.suppressClick = false;
    const mode = e.shiftKey && m.zoom ? 'pan' : range ? 'range' : 'select';
    state.drag = { mode, x0: pos.x, x1: pos.x, moved: false, id: e.pointerId };
    try {
        state.svg.setPointerCapture(e.pointerId);
    } catch {
        // not an active pointer (e.g. synthetic events)
    }
    e.preventDefault();
    return true;
}

// Two fingers on a zoomable chart: pinch zooms x around the fingers, moving both pans. The x under
// the fingers' midpoint stays under it; the range scales with the fingers' horizontal spread
// (at least 24 units, so fingers above each other only pan).
function pinchStart(state, e) {
    const m = state.model;
    const s = m && m.xscale;
    if (!s || !m.zoom || state.touches.size !== 2 || state.drag) {
        return false;
    }

    const pos = pointer(state, e);
    if (!pos || !inPlot(m, pos, 0)) {
        return false;
    }

    const ids = [...state.touches.keys()];
    const xs = ids.map(i => state.touches.get(i));
    const mid = (xs[0] + xs[1]) / 2;
    hide(state);
    broadcastHover(state, null);
    state.suppressClick = false;
    state.drag = {
        mode: 'pinch', ids, mid, spread: Math.max(24, Math.abs(xs[0] - xs[1])),
        s: { d0: s.d0, d1: s.d1, r0: s.r0, r1: s.r1 }, c: toData(s, mid), to: null, moved: false,
    };
    for (const id of ids) {
        try {
            state.svg.setPointerCapture(id);
        } catch {
            // not an active pointer (e.g. synthetic events)
        }
    }

    e.preventDefault();
    return true;
}

function pinchMove(state, e) {
    const d = state.drag;
    if (!d.ids.includes(e.pointerId)) {
        return;
    }

    const xs = d.ids.map(i => state.touches.get(i));
    const mid = (xs[0] + xs[1]) / 2;
    const spread = Math.max(24, Math.abs(xs[0] - xs[1]));
    d.moved = d.moved || Math.abs(mid - d.mid) > 3 || Math.abs(spread - d.spread) > 3;
    if (!d.moved) {
        return;
    }

    const s = d.s;
    const span = (s.d1 - s.d0) * d.spread / spread;
    if (!(span > 0) || !isFinite(span)) {
        return;
    }

    const d0 = d.c - (mid - s.r0) / (s.r1 - s.r0) * span;
    d.to = [d0, d0 + span];
    // Preview: map the drawn x (old range) to the new range, x' = a·x + b.
    const a = (s.d1 - s.d0) / span;
    const b = s.r0 + (s.d0 - d0) * (s.r1 - s.r0) / span - s.r0 * a;
    hide(state);
    movePreview(state, b, a);
}

function dragMove(state, e) {
    const d = state.drag;
    const m = state.model;
    if (d.mode === 'pinch') {
        pinchMove(state, e);
        return;
    }

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

function dragEnd(state, e) {
    const d = state.drag;
    if (!d || (d.mode === 'pinch' && e && !d.ids.includes(e.pointerId))) {
        return;
    }

    state.drag = null;
    for (const id of d.mode === 'pinch' ? d.ids : [d.id]) {
        if (state.svg.hasPointerCapture && state.svg.hasPointerCapture(id)) {
            state.svg.releasePointerCapture(id);
        }
    }

    if (state.hover) {
        state.hover.replaceChildren();
    }

    if (d.mode === 'pinch') {
        state.touchRest = state.touches.size > 0;
        if (d.moved && d.to) {
            state.suppressClick = true;
            commitRange(state, d.to[0], d.to[1]);
        } else {
            resetPreview(state);
        }

        return;
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
    if (d.mode === 'range') {
        if (d.moved && s && Math.abs(d.x1 - d.x0) >= 6) {
            state.suppressClick = true;
            state.dotnet.invokeMethodAsync('OnRangeSelected', toData(s, Math.min(d.x0, d.x1)), toData(s, Math.max(d.x0, d.x1))).catch(() => { });
        } else if (!d.moved && state.model.sel) {
            state.dotnet.invokeMethodAsync('OnSelectionCleared').catch(() => { }); // a click without a drag
        }

        return;
    }

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

// Pan and pinch preview: slide (and for a pinch, stretch) the series along x, x' = scale·x + dx;
// the clip stays put until .NET lays out the new range.
function movePreview(state, dx, scale = 1) {
    const plot = state.svg.querySelector('.rio-plot');
    if (plot) {
        for (const child of plot.children) {
            child.style.transform = scale === 1 ? `translateX(${dx}px)` : `matrix(${scale}, 0, 0, 1, ${dx}, 0)`;
        }
    }

    const labels = state.svg.querySelector('.rio-labels');
    if (labels) {
        labels.style.opacity = '0';
    }

    if (state.canvasScene) {
        paintCanvas(state, dx, scale);
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

// Shared: the x position closest to the pointer, and each series' point at that x. A step line
// has a value everywhere between its first and last point: the last point at or before the x
// (marked where that point is). rows.anchor is the x.
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
        if (s.dots) {
            continue; // scatter points are found in 2D (charts with them are not shared)
        }

        if (s.step) {
            const i = atOrBefore(s.x, anchor);
            if (i >= 0 && anchor <= s.x[s.x.length - 1] + 0.5) {
                rows.push({ s, i });
            }

            continue;
        }

        const i = nearest(s.x, anchor);
        if (i >= 0 && Math.abs(s.x[i] - anchor) <= s.snap) {
            rows.push({ s, i });
        }
    }

    rows.anchor = anchor;
    return rows;
}

// Index of the last value in the ascending array at or before v (half a pixel of slack); -1 when none.
function atOrBefore(xs, v) {
    let lo = 0, hi = xs.length - 1, found = -1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (xs[mid] <= v + 0.5) {
            found = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }

    return found;
}

// The x label for a set of hovered rows: from a row that has a point at the hovered x.
function headerOf(rows) {
    const at = rows.anchor === undefined ? null : rows.find(r => Math.abs(r.s.x[r.i] - rows.anchor) < 0.5);
    const r = at || rows[0];
    return r.s.xl[r.i];
}

// Single: the bar under the pointer, else the nearest point within reach.
function findSingle(m, px, py) {
    let best = null;
    for (const s of m.series) {
        if (s.ch > 0) {
            // Heatmap cells and horizontal bars: the one under the pointer.
            for (let j = 0; j < s.x.length; j++) {
                const w = s.cw ? s.cw[j] : s.barWidth;
                if (Math.abs(s.x[j] - px) <= w / 2 + 0.5 && Math.abs(s.y[j] - py) <= s.ch / 2 + 0.5) {
                    return [{ s, i: j, d: 0 }];
                }
            }

            continue;
        }

        if (s.dots) {
            // Scatter: every point within reach in x (x is ascending), the nearest in 2D.
            for (let j = atOrBefore(s.x, px - 40) + 1; j < s.x.length && s.x[j] <= px + 40; j++) {
                const d = Math.hypot(s.x[j] - px, s.y[j] - py);
                if (d <= 40 && (best === null || d < best.d)) {
                    best = { s, i: j, d };
                }
            }

            continue;
        }

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

function movePolar(state, m, pos, slice) {
    const s = slice || (pos ? findSlice(m, pos) : null);
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

    if (m.template) {
        requestTemplate(state, [[s.id, s.index]], pos, pos.x, pos.y);
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
    if (s.pl && s.pl.length > 0) {
        tip.appendChild(partRows(s.pl, s.pv));
    }

    placeTooltip(state, pos, pos.x, pos.y);
}

// The categories in a grouped "Other" slice: smaller rows under the total, set off by a line.
function partRows(labels, values) {
    const parts = document.createElement('div');
    parts.className = 'rio-tooltip-parts';
    labels.forEach((label, i) => {
        const row = document.createElement('div');
        row.className = 'rio-tooltip-row rio-tooltip-part';
        const name = document.createElement('span');
        name.className = 'rio-tooltip-name';
        name.textContent = label;
        const value = document.createElement('span');
        value.className = 'rio-tooltip-value';
        value.textContent = values[i] ?? '';
        row.append(name, value);
        parts.appendChild(row);
    });
    return parts;
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
    const x = rows.anchor !== undefined ? rows.anchor : first.x !== undefined ? first.x : first.s.x[first.i];
    if (first.s.ch > 0) {
        // Heatmap: outline the cell.
        const w = first.s.cw ? first.s.cw[first.i] : first.s.barWidth, h = first.s.ch, cx = first.s.x[first.i], cy = first.s.y[first.i];
        g.appendChild(el('rect', { class: 'rio-hover-cell', x: cx - w / 2, y: cy - h / 2, width: w, height: h, rx: 2 }));
        return;
    } else if (first.s.dots) {
        // Scatter: no crosshair, the point is marked below.
    } else if (m.band > 0 && (m.shared || first.s.bar)) {
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

    for (const r of rows) {
        if (!r.s.bar) {
            g.appendChild(el('circle', {
                class: 'rio-hover-point', cx: r.x !== undefined ? r.x : r.s.x[r.i], cy: r.s.y[r.i], r: 4, style: `fill:${r.s.color}`,
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
    if (state.model && state.model.template) {
        requestTemplate(state, rows.map(({ s, i }) => [s.id, s.index[i]]), pos, rows.anchor !== undefined ? rows.anchor : first.s.x[first.i], pos.y);
        return;
    }

    tip.replaceChildren();
    const head = document.createElement('div');
    head.className = 'rio-tooltip-head';
    head.textContent = headerOf(rows);
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

    placeTooltip(state, pos, rows.anchor !== undefined ? rows.anchor : first.s.x[first.i], pos.y);
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

// Template tooltips: ask .NET for content when the hovered points change. One call at a time;
// points hovered meanwhile are sent when it returns (only the latest).
function requestTemplate(state, points, pos, x, y) {
    state.tipAnchor = { pos, x, y };
    const key = points.map(p => p[0] + '' + p[1]).join('');
    if (key !== state.tipKey) {
        state.tipKey = key;
        state.tipWanted = points;
        sendTemplate(state);
    }

    showTemplate(state);
}

function sendTemplate(state) {
    const points = state.tipWanted;
    if (state.tipBusy || !points) {
        return;
    }

    state.tipWanted = null;
    state.tipBusy = true;
    state.dotnet.invokeMethodAsync('OnTooltipHover', points.map(p => p[0]), points.map(p => p[1]))
        .catch(() => { })
        .finally(() => {
            state.tipBusy = false;
            sendTemplate(state);
        });
}

// Shows the card at the hovered point once it has content (the previous point's until the new arrives).
function showTemplate(state) {
    const a = state.tipAnchor, tip = state.tip;
    if (a && tip && state.model && state.model.template && (tip.firstElementChild || tip.textContent.trim())) {
        placeTooltip(state, a.pos, a.x, a.y);
    }
}

// ---------- Legend tooltips ----------

/** Shows a legend item's summary (data-tip) under it, or above when there is no room. */
function showLegendTip(state, target) {
    const tip = state.legendTip;
    const item = tip && target && target.closest ? target.closest('[data-tip]') : null;
    if (!item || !state.svg.contains(item) || item === state.legendItem) {
        return;
    }

    state.legendItem = item;
    const text = item.getAttribute('data-tip');
    if (text.includes('\t')) {
        // "name<tab>value" lines; lines starting with "• " are the parts of the line above (Other).
        tip.replaceChildren();
        const lines = text.split('\n');
        for (let i = 0; i < lines.length;) {
            const [label, value] = lines[i++].split('\t');
            const row = document.createElement('div');
            row.className = 'rio-tooltip-row';
            const name = document.createElement('span');
            name.className = 'rio-tooltip-name';
            name.textContent = label;
            const v = document.createElement('span');
            v.className = 'rio-tooltip-value';
            v.textContent = value ?? '';
            row.append(name, v);
            tip.appendChild(row);
            const labels = [], values = [];
            while (i < lines.length && lines[i].startsWith('• ')) {
                const [l, pv] = lines[i++].slice(2).split('\t');
                labels.push(l);
                values.push(pv ?? '');
            }

            if (labels.length > 0) {
                tip.appendChild(partRows(labels, values));
            }
        }
    } else {
        tip.textContent = text;
    }

    tip.classList.add('rio-tooltip-visible');
    const c = state.container.getBoundingClientRect();
    const r = item.getBoundingClientRect();
    const w = tip.offsetWidth, h = tip.offsetHeight;
    const cw = state.container.clientWidth, ch = state.container.clientHeight;
    const left = Math.max(0, Math.min(r.left - c.left, cw - w));
    let top = r.bottom - c.top + 6;
    if (top + h > ch) {
        top = r.top - c.top - 6 - h;
    }

    tip.style.transform = `translate(${Math.round(left)}px, ${Math.round(Math.max(0, top))}px)`;
}

function hideLegendTip(state, related) {
    const item = related && related.closest ? related.closest('[data-tip]') : null;
    if (item && item === state.legendItem) {
        return; // moving within the same item
    }

    state.legendItem = null;
    if (state.legendTip) {
        state.legendTip.classList.remove('rio-tooltip-visible');
    }
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

    state.tipAnchor = null;
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

// ---------- Keyboard navigation ----------

// With the chart focused: Left/Right move along the series (Home/End: first/last, PageUp/PageDown:
// a tenth), Up/Down change series; on pies every arrow moves between slices. Enter/Space clicks
// the point, Escape leaves it. Only clicks go to .NET.
function keyDown(state, e) {
    const m = state.model;
    if (e.target !== state.svg || !m || !m.keys || e.altKey || e.ctrlKey || e.metaKey) {
        return; // legend items and annotations handle their own keys
    }

    if (zoomKey(state, e)) {
        return;
    }

    const polar = !!(m.slices && m.slices.length > 0);
    const series = polar ? [] : m.series.filter(s => s.x.length > 0);
    if (!polar && series.length === 0) {
        return;
    }

    let kb = state.kb;
    const key = e.key;
    if (key === 'Escape') {
        if (kb) {
            e.preventDefault();
            blurKeys(state);
        }

        return;
    }

    if (key === 'Enter' || key === ' ' || key === 'Spacebar') {
        if (!kb) {
            return;
        }

        e.preventDefault();
        const target = polar ? m.slices[kb.i] : series[kb.s] && { id: series[kb.s].id, index: series[kb.s].index[kb.i] };
        if (m.click && target && target.index >= 0) {
            state.dotnet.invokeMethodAsync('OnPointClicked', target.id, target.index, !!e.shiftKey).catch(() => { });
        }

        return;
    }

    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'].includes(key)) {
        return;
    }

    e.preventDefault();
    const back = key === 'ArrowLeft' || key === 'End' || (polar && key === 'ArrowUp');
    if (!kb) {
        // The first key lands on the first point (the last for Left and End).
        state.kb = polar ? { i: back ? m.slices.length - 1 : 0 } : { s: 0, i: back ? series[0].x.length - 1 : 0 };
        showKey(state, true);
        return;
    }

    if (polar) {
        const last = m.slices.length - 1;
        const step = { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1, PageUp: -5, PageDown: 5 }[key];
        kb.i = key === 'Home' ? 0 : key === 'End' ? last : Math.max(0, Math.min(last, kb.i + step));
    } else {
        kb.s = Math.min(kb.s, series.length - 1);
        const s = series[kb.s];
        const last = s.x.length - 1;
        const page = Math.max(1, Math.round(s.x.length / 10));
        if (key === 'ArrowUp' || key === 'ArrowDown') {
            // Another series: its point nearest the current x.
            const next = Math.max(0, Math.min(series.length - 1, kb.s + (key === 'ArrowUp' ? -1 : 1)));
            if (next !== kb.s) {
                const x = s.x[Math.min(kb.i, last)];
                kb.s = next;
                kb.i = Math.max(0, nearest(series[next].x, x));
            }
        } else {
            const step = { ArrowLeft: -1, ArrowRight: 1, PageUp: -page, PageDown: page }[key];
            kb.i = key === 'Home' ? 0 : key === 'End' ? last : Math.max(0, Math.min(last, kb.i + step));
        }
    }

    showKey(state, true);
}

// Zoomable charts: + and - zoom in and out (2x) around the focused point or the middle, Shift with
// Left/Right pans a fifth of the view, 0 shows all data. Linked charts follow, as with the pointer.
function zoomKey(state, e) {
    const m = state.model;
    const s = m.xscale;
    if (!m.zoom || !s) {
        return false;
    }

    const key = e.key;
    const pan = e.shiftKey && (key === 'ArrowLeft' || key === 'ArrowRight');
    if (!pan && !['+', '=', '-', '_', '0'].includes(key)) {
        return false;
    }

    e.preventDefault();
    const series = (m.series || []).filter(x => x.x.length > 0);
    const kb = state.kb;
    const focused = kb && kb.s !== undefined && series[kb.s] ? toData(s, series[kb.s].x[kb.i]) : null;
    state.kbAnchor = kb ? focused : null;
    if (key === '0') {
        if (m.zoomed) {
            commitReset(state);
        }

        return true;
    }

    const width = s.d1 - s.d0;
    if (pan) {
        const shift = width * 0.2 * (key === 'ArrowLeft' ? -1 : 1);
        commitRange(state, s.d0 + shift, s.d1 + shift);
        return true;
    }

    const factor = key === '-' || key === '_' ? 2 : 0.5;
    const anchor = focused !== null ? focused : (s.d0 + s.d1) / 2;
    commitRange(state, anchor - (anchor - s.d0) * factor, anchor + (s.d1 - anchor) * factor);
    return true;
}

// Draws the focused point like a hover and (when announce) tells screen readers about it.
function showKey(state, announce) {
    const m = state.model, kb = state.kb;
    if (!m || !kb || !state.svg) {
        return;
    }

    const rect = state.svg.getBoundingClientRect();
    const vb = state.svg.viewBox.baseVal;
    const k = vb && vb.width && rect.width ? vb.width / rect.width : 1;
    if (m.slices && m.slices.length > 0) {
        kb.i = Math.max(0, Math.min(m.slices.length - 1, kb.i));
        const slice = m.slices[kb.i];
        const [x, y] = arcPoint(slice.cx, slice.cy, (slice.r0 + slice.r1) / 2, (slice.a0 + slice.a1) / 2);
        if (m.tooltip) {
            movePolar(state, m, { x, y, k, rect }, slice);
        } else if (state.hover) {
            state.hover.replaceChildren(el('path', {
                class: 'rio-hover-halo', d: ringPath(slice.cx, slice.cy, slice.r1 + 1.5, slice.r1 + 5, slice.a0, slice.a1), style: `fill:${slice.color}`,
            }));
        }

        if (announce) {
            const parts = slice.pl && slice.pl.length > 0 ? ` (${slice.pl.map((l, j) => `${l} ${slice.pv[j]}`).join(', ')})` : '';
            say(state, `${slice.header ? slice.header + ', ' : ''}${slice.label}: ${slice.value}${parts}. ${position(m, kb.i + 1, m.slices.length)}`);
        }

        return;
    }

    const series = m.series.filter(s => s.x.length > 0);
    if (series.length === 0) {
        blurKeys(state);
        return;
    }

    kb.s = Math.max(0, Math.min(series.length - 1, kb.s));
    const s = series[kb.s];
    kb.i = Math.max(0, Math.min(s.x.length - 1, kb.i));
    const rows = m.shared ? findShared(m, s.x[kb.i]) : [{ s, i: kb.i }];
    if (!rows.some(r => r.s === s)) {
        rows.unshift({ s, i: kb.i });
    }

    rows.anchor = s.x[kb.i];

    // The focused series first, so the tooltip and the announcement start with it.
    rows.sort((a, b) => (a.s === s ? -1 : b.s === s ? 1 : 0));
    drawHover(state, rows, m);
    if (m.tooltip) {
        drawTooltip(state, rows, { x: s.x[kb.i], y: s.y[kb.i], k, rect });
    }

    if (state.group && m.xscale) {
        broadcastHover(state, toData(m.xscale, s.x[kb.i]));
    }

    if (announce) {
        const values = rows.map(r => `${r.s.name} ${r.s.v[r.i]}`).join(', ');
        say(state, `${s.xl[kb.i]}: ${values}. ${position(m, kb.i + 1, s.x.length)}`);
    }
}

function blurKeys(state) {
    if (state.kb) {
        state.kb = null;
        hide(state);
        broadcastHover(state, null);
        say(state, '');
    }
}

// The download menu: Up/Down (wrapping), Home and End move between its items. Blazor handles
// Escape and the clicks.
function menuKey(e) {
    const item = e.target && e.target.closest ? e.target.closest('[role=menuitem]') : null;
    const menu = item ? item.closest('[role=menu]') : null;
    if (!menu || !['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key)) {
        return;
    }

    e.preventDefault();
    const items = [...menu.querySelectorAll('[role=menuitem]')];
    const i = items.indexOf(item), n = items.length;
    const next = { ArrowDown: (i + 1) % n, ArrowUp: (i - 1 + n) % n, Home: 0, End: n - 1 }[e.key];
    items[next].focus();
}

// The live region (Blazor renders it empty): screen readers read what is written here.
/** "3 of 12", from the chart's texts ({0} of {1}). */
function position(m, i, n) {
    return (m.pos || '{0} of {1}').replace('{0}', i).replace('{1}', n);
}

function say(state, text) {
    const live = state.container.querySelector('.rio-live');
    if (live) {
        live.textContent = text;
    }
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
            state.dotnet.invokeMethodAsync('OnPointClicked', s.id, s.index, additive(e)).catch(() => { });
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
        state.dotnet.invokeMethodAsync('OnPointClicked', s.id, s.index[i], additive(e)).catch(() => { });
    } else if (m.select === 'points' && m.sel && !additive(e)) {
        state.dotnet.invokeMethodAsync('OnSelectionCleared').catch(() => { }); // a click beside the points
    }
}

// Ctrl/Cmd or Shift: add a point to the selection (or remove it) instead of replacing it.
function additive(e) {
    return !!(e.ctrlKey || e.metaKey || e.shiftKey);
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
    scene = json(scene);
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

function paintCanvas(state, dx, scale = 1) {
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

    ctx.transform(scale, 0, 0, 1, dx, 0);
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

// ---------- Export (SVG, PNG, downloads) ----------

// Presentation properties copied from the live chart, so the file looks the same without the
// page's CSS (theme variables, legend fading). Inherited ones are written only where they differ
// from the parent; the rest only where they differ from the SVG default.
const EXPORT_INHERITED = [
    'fill', 'fill-opacity', 'stroke', 'stroke-width', 'stroke-opacity', 'stroke-dasharray', 'stroke-linecap',
    'stroke-linejoin', 'visibility', 'font-family', 'font-size', 'font-weight', 'font-style', 'text-anchor',
    'font-variant-numeric',
];
const EXPORT_DEFAULTS = { 'opacity': '1', 'dominant-baseline': 'auto' };

// The chart as a standalone SVG document: { text, width, height }.
function buildSvg(container, options) {
    options = json(options);
    const source = container.querySelector('svg.rio-chart-svg');
    if (!source) {
        throw new Error('RioCharts: no chart to export.');
    }

    const vb = source.viewBox.baseVal;
    const width = vb && vb.width ? vb.width : source.clientWidth;
    const height = vb && vb.height ? vb.height : source.clientHeight;
    const copy = source.cloneNode(true);
    const originals = [source, ...source.querySelectorAll('*')];
    const copies = [copy, ...copy.querySelectorAll('*')];
    const computed = new Map();
    const removed = [];
    originals.forEach((el, i) => {
        const target = copies[i];
        for (const attr of [...target.attributes]) {
            if (attr.name === 'class' || attr.name === 'tabindex' || /^b-[a-z0-9]+$/.test(attr.name)) {
                target.removeAttribute(attr.name); // CSS hooks and Blazor's scoped-CSS markers
            }
        }

        const style = getComputedStyle(el);
        computed.set(el, style);
        if (style.display === 'none') {
            removed.push(target);
            return;
        }

        if (el.tagName === 'title' || el.tagName === 'desc') {
            return;
        }

        const parent = el === source ? null : computed.get(el.parentElement);
        for (const name of EXPORT_INHERITED) {
            const value = style.getPropertyValue(name);
            const inherited = parent ? parent.getPropertyValue(name) : null;
            if (value && value !== inherited) {
                target.setAttribute(name, value);
            } else {
                target.removeAttribute(name);
            }
        }

        // The entry fade: keep the element's own opacity, not a frame of the animation.
        const animating = el.getAnimations && el.getAnimations().some(a => a.playState === 'running');
        for (const name in EXPORT_DEFAULTS) {
            if (name === 'opacity' && animating) {
                continue;
            }

            const value = style.getPropertyValue(name);
            if (value && value !== EXPORT_DEFAULTS[name]) {
                target.setAttribute(name, value);
            } else {
                target.removeAttribute(name);
            }
        }
    });
    removed.forEach(el => el.remove());
    const comments = document.createTreeWalker(copy, NodeFilter.SHOW_COMMENT);
    const markers = [];
    while (comments.nextNode()) {
        markers.push(comments.currentNode); // Blazor's render markers
    }

    markers.forEach(n => n.remove());

    copy.setAttribute('xmlns', SVG);
    copy.setAttribute('width', width);
    copy.setAttribute('height', height);
    copy.removeAttribute('style');
    copy.removeAttribute('aria-describedby');

    const background = options && options.background
        ? options.background
        : resolveColor(container, 'var(--rio-surface, var(--_rio-surface))');
    if (background && background !== 'transparent' && background !== 'none') {
        const rect = document.createElementNS(SVG, 'rect');
        rect.setAttribute('width', '100%');
        rect.setAttribute('height', '100%');
        rect.setAttribute('fill', background);
        copy.insertBefore(rect, copy.querySelector(':scope > desc')?.nextSibling || copy.firstChild);
    }

    // The canvas series layer (large data) sits over the SVG: add it as an image on top.
    const canvas = container.querySelector('canvas.rio-canvas');
    if (canvas && canvas.width > 0) {
        const image = document.createElementNS(SVG, 'image');
        image.setAttribute('x', 0);
        image.setAttribute('y', 0);
        image.setAttribute('width', width);
        image.setAttribute('height', height);
        image.setAttribute('preserveAspectRatio', 'none');
        image.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', canvas.toDataURL('image/png'));
        copy.appendChild(image);
    }

    const text = '<?xml version="1.0" encoding="UTF-8"?>\n' + new XMLSerializer().serializeToString(copy);
    return { text, width, height };
}

// A CSS color (e.g. a theme variable) as the browser resolves it inside the chart.
function resolveColor(container, css) {
    const probe = document.createElement('span');
    probe.style.display = 'none';
    probe.style.color = css;
    container.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color === 'rgba(0, 0, 0, 0)' ? 'transparent' : color;
}

async function renderPng(container, options) {
    options = json(options);
    const svg = buildSvg(container, options);
    const scale = options && options.scale > 0 ? options.scale : 2;
    const url = URL.createObjectURL(new Blob([svg.text], { type: 'image/svg+xml;charset=utf-8' }));
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(svg.width * scale));
        canvas.height = Math.max(1, Math.round(svg.height * scale));
        canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
        return await new Promise((resolve, reject) =>
            canvas.toBlob(b => b ? resolve(b) : reject(new Error('RioCharts: PNG encoding failed.')), 'image/png'));
    } finally {
        URL.revokeObjectURL(url);
    }
}

// For .NET (as a stream): the SVG text as UTF-8, and the PNG bytes.
export function exportSvg(container, options) {
    return new TextEncoder().encode(buildSvg(container, options).text);
}

export async function exportPng(container, options) {
    return new Uint8Array(await (await renderPng(container, options)).arrayBuffer());
}

// Saves the chart as a file, without sending the image through .NET.
export async function download(container, kind, fileName, options) {
    const blob = kind === 'png'
        ? await renderPng(container, options)
        : new Blob([buildSvg(container, options).text], { type: 'image/svg+xml;charset=utf-8' });
    saveBlob(blob, fileName);
}

export function downloadText(fileName, text, type) {
    // A byte order mark, so Excel reads UTF-8 CSV correctly.
    saveBlob(new Blob(['﻿', text], { type: `${type};charset=utf-8` }), fileName);
}

function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
