// The rendered width of an element, so a button can hold its size while its label changes to
// "Submitting…". Without it the button resizes mid-click and the row of buttons shifts under the
// cursor -- which is its own way of causing the misclick the busy state exists to prevent.
window.cgMeasureWidth = function (element) {
    return element ? Math.ceil(element.getBoundingClientRect().width) : 0;
};
