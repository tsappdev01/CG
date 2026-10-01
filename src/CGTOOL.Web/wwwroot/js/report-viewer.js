// The report viewer's two browser-only jobs: going full screen, and printing.
//
// Full screen is requested on the viewer element rather than the document, so the report keeps the
// page's own stylesheet and the toolbar stays reachable. The request must come from a user
// gesture; Blazor Server round-trips the click to the server and back, which in some browsers
// spends that gesture -- so the button calls this directly through a plain DOM listener attached
// here rather than through an @onclick handler.
window.cgReportViewer = {
    toggleFullscreen: (element) => {
        if (document.fullscreenElement) {
            document.exitFullscreen();
            return false;
        }
        if (element && element.requestFullscreen) {
            element.requestFullscreen();
            return true;
        }
        return false;
    },

    // Printing waits a frame so that any zoom transform the viewer applied is laid out before the
    // print stylesheet takes over -- printing mid-transform produced a first page scaled to
    // whatever the screen zoom happened to be.
    //
    // The landscape @page rule is inserted here rather than living in app.css, because @page is
    // global and cannot be scoped by a selector: a landscape rule in the stylesheet would turn the
    // declaration printouts landscape too, and they are laid out for portrait. A named page
    // (`@page name` + `page: name`) is the textbook answer and was ignored by the browser. So the
    // rule exists only for as long as the print takes.
    print: () => {
        // Remove any rule a previous print left behind before adding this one, so repeated
        // clicks cannot stack them.
        document.getElementById('cg-report-page-rule')?.remove();

        const style = document.createElement('style');
        style.id = 'cg-report-page-rule';
        style.textContent = '@page { size: A4 landscape; margin: 8mm; }';
        document.head.appendChild(style);

        const cleanup = () => {
            style.remove();
            window.removeEventListener('afterprint', cleanup);
        };

        // Cleaned up two ways, because leaving the rule behind would silently turn the next
        // declaration printout landscape. window.print() blocks until the dialog closes in most
        // browsers, so the finally runs as soon as printing is done; afterprint covers the
        // browsers where print() returns immediately. Both are idempotent.
        window.addEventListener('afterprint', cleanup);

        requestAnimationFrame(() => requestAnimationFrame(() => {
            try {
                window.print();
            } finally {
                cleanup();
            }
        }));
    },
};
