// RioCharts: the small JS side of <Chart>.
// - Measures the container (ResizeObserver) and the inherited font, so C# lays out at the real
//   size with real text widths.
// - Handles hover locally: C# sends the drawn points once per layout; pointer moves find the
//   nearest x by binary search and draw the crosshair and tooltip without a server round trip.
//   Only clicks call back into .NET.

const states = new WeakMap();
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
        current: null,
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
        state.lastEvent = e;
        if (!state.frame) {
            state.frame = requestAnimationFrame(() => {
                state.frame = 0;
                move(state, state.lastEvent);
            });
        }
    };
    state.onLeave = () => hide(state);
    state.onClick = e => click(state, e);
    if (state.svg) {
        state.svg.addEventListener('pointermove', state.onMove);
        state.svg.addEventListener('pointerdown', state.onMove);
        state.svg.addEventListener('pointerleave', state.onLeave);
        state.svg.addEventListener('click', state.onClick);
    }

    states.set(container, state);
    return { width: state.width, widths: measureFont(container) };
}

export function update(container, model) {
    const state = states.get(container);
    if (!state) {
        return;
    }

    state.model = model;
    hide(state);
    if (state.svg) {
        state.svg.classList.toggle('rio-clickable', !!(model && model.click));
    }
}

export function dispose(container) {
    const state = states.get(container);
    if (state) {
        clearTimeout(state.timer);
        cancelAnimationFrame(state.frame);
        state.observer.disconnect();
        if (state.svg) {
            state.svg.removeEventListener('pointermove', state.onMove);
            state.svg.removeEventListener('pointerdown', state.onMove);
            state.svg.removeEventListener('pointerleave', state.onLeave);
            state.svg.removeEventListener('click', state.onClick);
        }

        states.delete(container);
    }
}

// ---------- Hit-testing ----------

// Pointer position in chart (viewBox) units, or null when outside the plot.
function toChart(state, e) {
    const m = state.model;
    if (!m || !state.svg) {
        return null;
    }

    const rect = state.svg.getBoundingClientRect();
    const vb = state.svg.viewBox.baseVal;
    const k = vb && vb.width && rect.width ? vb.width / rect.width : 1;
    const x = (e.clientX - rect.left) * k;
    const y = (e.clientY - rect.top) * k;
    const p = m.plot;
    if (x < p.x - 6 || x > p.x + p.w + 6 || y < p.y - 6 || y > p.y + p.h + 6) {
        return null;
    }

    return { x, y, k, rect };
}

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
        return;
    }

    drawHover(state, rows, m);
    drawTooltip(state, rows, pos);
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
