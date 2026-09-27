// A pad you sign on, rather than sketch on.
//
// Two things make the difference between the two. Strokes are drawn as quadratic curves through the
// midpoints of consecutive samples, so the line follows the hand instead of showing the polygon the
// browser actually reported; and each segment's width comes from how fast the pointer was moving,
// so the stroke tapers the way a pen does. Pointer Events throughout, so a stylus, a finger and a
// mouse are one code path -- and a stylus that reports real pressure is used in place of the speed
// estimate.
(() => {
    const pads = new Map();

    const lineWidth = (pad, pressure, speed) => {
        // A real pressure reading beats a guess. Chrome reports 0.5 for a mouse and for a pen that
        // is not pressure-sensitive, which is exactly the value that means "no idea", so speed is
        // used for those.
        if (pressure > 0 && pressure !== 0.5) return pad.maxWidth * pressure;

        const fromSpeed = pad.maxWidth - (speed * 0.8);
        return Math.max(pad.minWidth, Math.min(pad.maxWidth, fromSpeed));
    };

    const midpoint = (a, b) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });

    function draw(pad) {
        const ctx = pad.ctx;
        ctx.clearRect(0, 0, pad.canvas.width / pad.scale, pad.canvas.height / pad.scale);
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = pad.colour;

        for (const stroke of pad.strokes) {
            if (stroke.length === 1) {
                // A single tap is a dot, and a zero-length line draws nothing.
                ctx.beginPath();
                ctx.arc(stroke[0].x, stroke[0].y, stroke[0].width / 2, 0, Math.PI * 2);
                ctx.fillStyle = pad.colour;
                ctx.fill();
                continue;
            }

            for (let i = 1; i < stroke.length; i++) {
                const from = i === 1 ? stroke[0] : midpoint(stroke[i - 1], stroke[i]);
                const to = i === stroke.length - 1 ? stroke[i] : midpoint(stroke[i], stroke[i + 1]);

                ctx.beginPath();
                ctx.moveTo(from.x, from.y);
                ctx.quadraticCurveTo(stroke[i].x, stroke[i].y, to.x, to.y);
                ctx.lineWidth = stroke[i].width;
                ctx.stroke();
            }
        }
    }

    // The canvas is sized in CSS pixels but backed at the device's own resolution, or the ink is
    // soft on any screen that is not exactly 1x -- which is most of them.
    function resize(pad) {
        const rect = pad.canvas.getBoundingClientRect();
        if (rect.width === 0) return;

        pad.scale = window.devicePixelRatio || 1;
        pad.canvas.width = rect.width * pad.scale;
        pad.canvas.height = rect.height * pad.scale;
        pad.ctx.setTransform(pad.scale, 0, 0, pad.scale, 0, 0);
        draw(pad);
    }

    window.cgSignaturePad = {
        init(canvasId, dotNetRef) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) return false;

            this.destroy(canvasId);

            const pad = {
                canvas,
                ctx: canvas.getContext('2d'),
                dotNetRef,
                strokes: [],
                current: null,
                last: null,
                lastTime: 0,
                scale: 1,
                minWidth: 0.7,
                maxWidth: 2.6,
                colour: '#0D1B33',
            };

            const point = (e) => {
                const rect = canvas.getBoundingClientRect();
                return { x: e.clientX - rect.left, y: e.clientY - rect.top };
            };

            pad.onDown = (e) => {
                if (canvas.hasAttribute('data-readonly')) return;
                e.preventDefault();
                canvas.setPointerCapture(e.pointerId);

                const p = point(e);
                pad.last = p;
                pad.lastTime = e.timeStamp;
                pad.current = [{ ...p, width: lineWidth(pad, e.pressure, 0) }];
                pad.strokes.push(pad.current);
            };

            pad.onMove = (e) => {
                if (!pad.current) return;
                e.preventDefault();

                // Coalesced events give every sample the device reported, not just the ones that
                // survived the frame -- the difference between a smooth curve and a fast scribble
                // drawn as four straight lines.
                // An empty coalesced list is not "no movement" -- some browsers return one, and
                // trusting it drops the whole stroke. The event itself is always a real sample.
                let events = e.getCoalescedEvents ? e.getCoalescedEvents() : [];
                if (events.length === 0) events = [e];
                for (const sample of events) {
                    const p = point(sample);
                    const dt = Math.max(1, sample.timeStamp - pad.lastTime);
                    const distance = Math.hypot(p.x - pad.last.x, p.y - pad.last.y);
                    if (distance < 0.7) continue;

                    pad.current.push({ ...p, width: lineWidth(pad, sample.pressure, distance / dt * 10) });
                    pad.last = p;
                    pad.lastTime = sample.timeStamp;
                }
                draw(pad);
            };

            pad.onUp = (e) => {
                if (!pad.current) return;
                e.preventDefault();
                pad.current = null;
                draw(pad);
                pad.dotNetRef?.invokeMethodAsync('OnSignatureChanged', true);
            };

            canvas.addEventListener('pointerdown', pad.onDown);
            canvas.addEventListener('pointermove', pad.onMove);
            canvas.addEventListener('pointerup', pad.onUp);
            canvas.addEventListener('pointercancel', pad.onUp);
            canvas.addEventListener('pointerleave', pad.onUp);

            pad.onResize = () => resize(pad);
            window.addEventListener('resize', pad.onResize);

            pads.set(canvasId, pad);
            resize(pad);
            return true;
        },

        clear(canvasId) {
            const pad = pads.get(canvasId);
            if (!pad) return;
            pad.strokes = [];
            pad.current = null;
            draw(pad);
            pad.dotNetRef?.invokeMethodAsync('OnSignatureChanged', false);
        },

        undo(canvasId) {
            const pad = pads.get(canvasId);
            if (!pad) return;
            pad.strokes.pop();
            draw(pad);
            pad.dotNetRef?.invokeMethodAsync('OnSignatureChanged', pad.strokes.length > 0);
        },

        // The ink, cropped to itself on a transparent background, so the signature sits on whatever
        // it is later placed on rather than carrying a white box and the pad's empty margins.
        toPng(canvasId) {
            const pad = pads.get(canvasId);
            if (!pad || pad.strokes.length === 0) return null;

            let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
            for (const stroke of pad.strokes) {
                for (const p of stroke) {
                    minX = Math.min(minX, p.x - p.width);
                    minY = Math.min(minY, p.y - p.width);
                    maxX = Math.max(maxX, p.x + p.width);
                    maxY = Math.max(maxY, p.y + p.width);
                }
            }

            const pad_ = 6;
            minX = Math.max(0, minX - pad_);
            minY = Math.max(0, minY - pad_);
            maxX = maxX + pad_;
            maxY = maxY + pad_;

            const out = document.createElement('canvas');
            out.width = Math.round((maxX - minX) * pad.scale);
            out.height = Math.round((maxY - minY) * pad.scale);
            out.getContext('2d').drawImage(
                pad.canvas,
                Math.round(minX * pad.scale), Math.round(minY * pad.scale),
                out.width, out.height,
                0, 0, out.width, out.height);

            return out.toDataURL('image/png').split(',')[1];
        },

        destroy(canvasId) {
            const pad = pads.get(canvasId);
            if (!pad) return;

            pad.canvas.removeEventListener('pointerdown', pad.onDown);
            pad.canvas.removeEventListener('pointermove', pad.onMove);
            pad.canvas.removeEventListener('pointerup', pad.onUp);
            pad.canvas.removeEventListener('pointercancel', pad.onUp);
            pad.canvas.removeEventListener('pointerleave', pad.onUp);
            window.removeEventListener('resize', pad.onResize);
            pads.delete(canvasId);
        },
    };
})();
