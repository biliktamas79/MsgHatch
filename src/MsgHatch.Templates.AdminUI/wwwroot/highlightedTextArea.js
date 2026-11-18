// Keeps the scroll of the highlighted layer in step with its text area (D252).
export function syncScroll(input, layer) {
    if (!input || !layer) {
        return;
    }

    const sync = () => {
        layer.scrollTop = input.scrollTop;
        layer.scrollLeft = input.scrollLeft;
    };
    input.addEventListener('scroll', sync);
    sync();
}
