// Keeps the scroll of the highlighted layer in step with the text area of the Liquid editor (D242).
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
