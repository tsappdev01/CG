// Puts the cursor in the field a validation message is about. A message naming a field the member
// then has to hunt for -- on a step they may not even be on -- is most of the way to no message at
// all.
window.cgFocusField = (id) => {
    const el = document.getElementById(id);
    if (!el) return false;

    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    // preventScroll: the smooth scroll above is already running, and focus() would jump past it.
    el.focus({ preventScroll: true });
    if (typeof el.select === 'function' && el.tagName === 'INPUT') el.select();
    return true;
};
