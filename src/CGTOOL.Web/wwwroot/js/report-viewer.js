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
    print: () => requestAnimationFrame(() => requestAnimationFrame(() => window.print())),
};
