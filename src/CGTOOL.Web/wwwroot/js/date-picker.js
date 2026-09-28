// Opens the browser's own date picker for the hidden <input type="date"> that UaeDateInput keeps
// beside its text box, given the calendar button that was clicked.
//
// Wired as a plain DOM onclick rather than a Blazor handler on purpose: showPicker() is only allowed
// while the click still counts as user activation, and a Blazor Server handler would spend that on a
// round trip to the server first.
//
// showPicker() is the only way to raise a picker for an input that is not the visible control. Where
// the browser doesn't have it, or refuses the call, the text box still takes a typed date -- so this
// fails quietly rather than reporting anything.
window.cgShowDatePicker = function (button) {
    const field = button && button.closest('.cg-date-field');
    const picker = field && field.querySelector('.cg-date-field-picker');
    if (!picker || typeof picker.showPicker !== 'function') return false;
    try {
        picker.showPicker();
        return true;
    } catch {
        return false;
    }
};
