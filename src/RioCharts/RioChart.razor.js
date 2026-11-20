// High-frequency interactions for <RioChart>: pan, wheel/pinch zoom and crosshair.
// Pan and zoom are previewed with an SVG transform on the plot content; Blazor is called
// once per gesture (OnJsRangeChanged) and re-renders the chart at the new X range.

const states = new WeakMap();

export function init(svg, dotnet, options) {
    if (!svg || states.has(svg)) {
        return;
    }

    const state = {
        svg,
        dotnet,
        options,
        pending: null,      // [min, max] previewed but not yet committed
        drag: null,         // { id, startX, range, moved }
        pinch: null,        // { dist, midX, range }
        pointers: new Map(),
        commitTimer: 0,
        suppressClick: false,
    };

    state.handlers = {
        pointerdown: e => onPointerDown(state, e),
        pointermove: e => onPointerMove(state, e),
        pointerup: e => onPointerUp(state, e),
        pointercancel: e => onPointerUp(state, e),
        pointerleave: () => {
            state.lastPoint = null;
            hideCrosshair(state);
        },
        wheel: e => onWheel(state, e),
        dblclick: e => onDoubleClick(state, e),
        click: e => {
            // Swallow the click that ends a drag so it does not select a series.
            if (state.suppressClick) {
                state.suppressClick = false;
                e.stopPropagation();
                e.preventDefault();
            }
        },
    };

    for (const [name, handler] of Object.entries(state.handlers)) {
        const opts = name === 'wheel' ? { passive: false } : name === 'click' ? { capture: true } : undefined;
        svg.addEventListener(name, handler, opts);
    }

    states.set(svg, state);
    applyOptions(state);
}

export function update(svg, options) {
    const state = states.get(svg);
    if (!state) {
        return;
    }

    state.options = options;
    state.pending = null;
    clearTimeout(state.commitTimer);
    applyOptions(state);

    // Axis values changed under a still mouse: refresh the crosshair labels.
    if (state.lastPoint) {
        updateCrosshair(state, state.lastPoint);
    }
}

export function dispose(svg) {
    const state = states.get(svg);
    if (!state) {
        return;
    }

    clearTimeout(state.commitTimer);
    for (const [name, handler] of Object.entries(state.handlers)) {
        svg.removeEventListener(name, handler, name === 'click' ? { capture: true } : undefined);
    }

    states.delete(svg);
}

// ---------- Helpers ----------

function applyOptions(state) {
    const { svg, options } = state;
    svg.style.touchAction = options.panZoom ? 'none' : '';
    preview(state);
    if (!options.crosshair) {
        hideCrosshair(state);
    }
}

function toView(state, e) {
    const ctm = state.svg.getScreenCTM();
    if (!ctm) {
        return { x: 0, y: 0 };
    }

    const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(ctm.inverse());
    return { x: p.x, y: p.y };
}

function inside(plot, p) {
    return p.x >= plot.left && p.x <= plot.left + plot.width && p.y >= plot.top && p.y <= plot.top + plot.height;
}

function currentRange(state) {
    return state.pending ?? [state.options.x.min, state.options.x.max];
}

function dataAt(state, px) {
    const { plot } = state.options;
    const [a, b] = currentRange(state);
    return a + (px - plot.left) / plot.width * (b - a);
}

function clamp(range, full) {
    const fullSpan = full[1] - full[0];
    if (!(fullSpan > 0)) {
        return [full[0], full[1]];
    }

    let min = Math.min(range[0], range[1]);
    let max = Math.max(range[0], range[1]);
    const span = Math.min(Math.max(max - min, fullSpan * 1e-6), fullSpan);
    const center = (min + max) / 2;
    min = center - span / 2;
    max = center + span / 2;
    if (min < full[0]) { max += full[0] - min; min = full[0]; }
    if (max > full[1]) { min -= max - full[1]; max = full[1]; }
    return [Math.max(min, full[0]), Math.min(max, full[1])];
}

function zoom(range, factor, anchor) {
    return [anchor - (anchor - range[0]) / factor, anchor + (range[1] - anchor) / factor];
}

function fullRange(state) {
    return [state.options.x.fullMin, state.options.x.fullMax];
}

// Shows the pending range by transforming the already rendered content.
function preview(state) {
    const content = state.svg.querySelector('.rio-plot-content');
    if (!content) {
        return;
    }

    if (!state.pending) {
        content.removeAttribute('transform');
        return;
    }

    const { plot, x } = state.options;
    const [a, b] = state.pending;
    const scale = (x.max - x.min) / (b - a);
    const pa = plot.left + (a - x.min) / (x.max - x.min) * plot.width;
    content.setAttribute('transform', `translate(${plot.left - pa * scale} 0) scale(${scale} 1)`);
}

function scheduleCommit(state, delay) {
    clearTimeout(state.commitTimer);
    state.commitTimer = setTimeout(() => commit(state), delay);
}

function commit(state) {
    clearTimeout(state.commitTimer);
    if (!state.pending) {
        return;
    }

    const [min, max] = state.pending;
    state.dotnet.invokeMethodAsync('OnJsRangeChanged', min, max).catch(() => { });
}

// ---------- Wheel zoom ----------

function onWheel(state, e) {
    const { options } = state;
    if (!options.panZoom) {
        return;
    }

    const p = toView(state, e);
    if (!inside(options.plot, p)) {
        return;
    }

    e.preventDefault();
    const unit = e.deltaMode === 1 ? 0.05 : e.deltaMode === 2 ? 1 : 0.0015;
    const factor = Math.exp(-e.deltaY * unit);
    state.pending = clamp(zoom(currentRange(state), factor, dataAt(state, p.x)), fullRange(state));
    preview(state);
    updateCrosshair(state, p);
    scheduleCommit(state, 250);
}

function onDoubleClick(state, e) {
    const { options } = state;
    if (!options.panZoom || !inside(options.plot, toView(state, e))) {
        return;
    }

    e.preventDefault();
    state.dotnet.invokeMethodAsync('OnJsResetZoom').catch(() => { });
}

// ---------- Drag pan and pinch zoom ----------

function onPointerDown(state, e) {
    const { options } = state;
    if (!options.panZoom) {
        return;
    }

    const p = toView(state, e);
    if (!inside(options.plot, p)) {
        return;
    }

    state.pointers.set(e.pointerId, p);

    if (state.pointers.size === 2) {
        // Second finger: switch from pan to pinch.
        state.drag = null;
        const [p1, p2] = [...state.pointers.values()];
        state.pinch = {
            dist: Math.max(1, Math.abs(p1.x - p2.x)),
            midX: (p1.x + p2.x) / 2,
            range: currentRange(state),
        };
        try { state.svg.setPointerCapture(e.pointerId); } catch { /* ignore */ }
        return;
    }

    if (e.button === 0 && state.pointers.size === 1) {
        state.drag = { id: e.pointerId, startX: p.x, range: currentRange(state), moved: false };
    }
}

function onPointerMove(state, e) {
    const { options } = state;
    const p = toView(state, e);

    if (state.pointers.has(e.pointerId)) {
        state.pointers.set(e.pointerId, p);
    }

    if (state.pinch && state.pointers.size >= 2) {
        const [p1, p2] = [...state.pointers.values()];
        const dist = Math.max(1, Math.abs(p1.x - p2.x));
        const midX = (p1.x + p2.x) / 2;
        const { range, dist: startDist, midX: startMid } = state.pinch;
        const span = range[1] - range[0];
        const anchor = range[0] + (startMid - options.plot.left) / options.plot.width * span;
        let next = zoom(range, dist / startDist, anchor);
        const shift = -(midX - startMid) / options.plot.width * (next[1] - next[0]);
        next = [next[0] + shift, next[1] + shift];
        state.pending = clamp(next, fullRange(state));
        preview(state);
    } else if (state.drag && state.drag.id === e.pointerId) {
        const drag = state.drag;
        const dx = p.x - drag.startX;
        if (!drag.moved && Math.abs(dx) > 3) {
            drag.moved = true;
            state.svg.classList.add('rio-panning');
            try { state.svg.setPointerCapture(e.pointerId); } catch { /* ignore */ }
        }

        if (drag.moved) {
            const span = drag.range[1] - drag.range[0];
            const delta = -dx / options.plot.width * span;
            state.pending = clamp([drag.range[0] + delta, drag.range[1] + delta], fullRange(state));
            preview(state);
        }
    }

    updateCrosshair(state, p);
}

function onPointerUp(state, e) {
    const wasPinching = state.pinch !== null;
    state.pointers.delete(e.pointerId);

    if (wasPinching) {
        if (state.pointers.size < 2) {
            state.pinch = null;
            state.suppressClick = true;
            commit(state);
        }

        return;
    }

    if (state.drag && state.drag.id === e.pointerId) {
        const moved = state.drag.moved;
        state.drag = null;
        state.svg.classList.remove('rio-panning');
        if (moved) {
            state.suppressClick = true;
            commit(state);
        }
    }

    if (e.pointerType !== 'mouse') {
        hideCrosshair(state);
    }
}

// ---------- Crosshair ----------

function hideCrosshair(state) {
    const g = state.svg.querySelector('.rio-crosshair');
    if (g) {
        g.setAttribute('visibility', 'hidden');
    }
}

function updateCrosshair(state, p) {
    const { options } = state;
    const g = state.svg.querySelector('.rio-crosshair');
    if (!g || !options.crosshair) {
        return;
    }

    const { plot } = options;
    state.lastPoint = p;
    if (!inside(plot, p)) {
        hideCrosshair(state);
        return;
    }

    g.setAttribute('visibility', 'visible');
    const vLine = g.querySelector('.rio-crosshair-v');
    const hLine = g.querySelector('.rio-crosshair-h');
    vLine.setAttribute('x1', p.x);
    vLine.setAttribute('x2', p.x);
    vLine.setAttribute('y1', plot.top);
    vLine.setAttribute('y2', plot.top + plot.height);
    hLine.setAttribute('x1', plot.left);
    hLine.setAttribute('x2', plot.left + plot.width);
    hLine.setAttribute('y1', p.y);
    hLine.setAttribute('y2', p.y);

    const xValue = dataAt(state, p.x);
    setLabel(g.querySelector('.rio-crosshair-xlabel'), format(xValue, options.x, options.locale), p.x, plot.top + plot.height + 2, 'top');

    if (options.y) {
        const y = options.y;
        const yValue = y.min + (plot.top + plot.height - p.y) / plot.height * (y.max - y.min);
        setLabel(g.querySelector('.rio-crosshair-ylabel'), format(yValue, y, options.locale), plot.left - 2, p.y, 'left');
    }
}

function setLabel(group, text, x, y, side) {
    if (!group) {
        return;
    }

    const label = group.querySelector('text');
    const box = group.querySelector('rect');
    label.textContent = text;
    const bbox = label.getBBox();
    const padX = 4;
    const padY = 2;
    const width = bbox.width + padX * 2;
    const height = bbox.height + padY * 2;
    const left = side === 'top' ? x - width / 2 : x - width;
    const top = side === 'top' ? y : y - height / 2;
    box.setAttribute('x', left);
    box.setAttribute('y', top);
    box.setAttribute('width', width);
    box.setAttribute('height', height);
    label.setAttribute('x', left + padX);
    label.setAttribute('y', top + height / 2);
}

function format(value, axis, locale) {
    try {
        if (axis.isDate) {
            // OLE Automation date: days since 1899-12-30, local wall-clock time.
            const date = new Date(Math.round((value - 25569) * 86400000));
            const withTime = axis.decimals > 0;
            return date.toLocaleString(locale || undefined, withTime
                ? { timeZone: 'UTC', year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }
                : { timeZone: 'UTC', year: 'numeric', month: 'short', day: 'numeric' });
        }

        return value.toLocaleString(locale || undefined, {
            minimumFractionDigits: axis.decimals,
            maximumFractionDigits: axis.decimals,
        });
    } catch {
        return String(Math.round(value * 100) / 100);
    }
}
