window.cgtoolIdleLock = {
    _timer: null,
    _dotnetRef: null,
    _timeoutMs: 0,
    _handler: null,

    start: function (dotnetRef, timeoutMs) {
        this._dotnetRef = dotnetRef;
        this._timeoutMs = timeoutMs;
        this._handler = () => this._reset();
        ["mousemove", "keydown", "click", "scroll", "touchstart"].forEach(evt =>
            document.addEventListener(evt, this._handler, { passive: true }));
        this._reset();
    },

    _reset: function () {
        if (this._timer) clearTimeout(this._timer);
        this._timer = setTimeout(() => {
            if (this._dotnetRef) this._dotnetRef.invokeMethodAsync("OnIdleTimeout");
        }, this._timeoutMs);
    },

    stop: function () {
        if (this._timer) clearTimeout(this._timer);
        if (this._handler) {
            ["mousemove", "keydown", "click", "scroll", "touchstart"].forEach(evt =>
                document.removeEventListener(evt, this._handler));
        }
        this._dotnetRef = null;
    }
};
