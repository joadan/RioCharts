// RioCharts: the small JS side of <Chart>. Measures the container (ResizeObserver) and the
// inherited font (character widths), so C# can lay out at the real size with real text widths.

const states = new WeakMap();

export function init(container, dotnet) {
    if (!container) {
        return { width: 0, widths: null };
    }

    const state = { width: Math.round(container.clientWidth), timer: 0 };
    state.observer = new ResizeObserver(() => {
        const width = Math.round(container.clientWidth);
        if (width === state.width || width <= 0) {
            return;
        }

        state.width = width;
        clearTimeout(state.timer);
        state.timer = setTimeout(() => dotnet.invokeMethodAsync('OnResize', width).catch(() => { }), 60);
    });
    state.observer.observe(container);
    states.set(container, state);

    return { width: state.width, widths: measureFont(container) };
}

export function dispose(container) {
    const state = states.get(container);
    if (state) {
        clearTimeout(state.timer);
        state.observer.disconnect();
        states.delete(container);
    }
}

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
