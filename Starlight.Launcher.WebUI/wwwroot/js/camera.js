(() => {
    const PAGES = ['/', '/servers', '/hub', '/local-server', '/settings'];

    const ZOOM = { '/auth': 2.4 };

    const STEP = 0.55;

    const FPS = 30;
    const STEP_MS = 420;
    const EXTRA_STEP_MS = 160;
    const ZOOM_MS = 700;

    const TRANSITION_TIMEOUT_MS = 1500;

    const root = document.documentElement;

    let camX = 0;
    let zoom = 1;
    let raf = 0;
    let finishTransition = null;
    let pendingFlight = null;

    const background = () => document.getElementById('app-bg');

    const moving = () =>
        !root.classList.contains('sl-bg-static') &&
        !matchMedia('(prefers-reduced-motion: reduce)').matches;

    const pathOf = url => new URL(url, location.href).pathname.replace(/\/+$/, '') || '/';

    const zoomOf = path => ZOOM[path] ?? 1;

    function steps(from, to) {
        const a = PAGES.indexOf(from), b = PAGES.indexOf(to);
        if (a < 0 || b < 0)
            return 0;

        const d = b - a;
        return Math.abs(d) === PAGES.length - 1 ? -Math.sign(d) : d;
    }

    const ease = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;

    function apply() {
        const el = background();
        if (!el)
            return;

        el.style.setProperty('--cam-x', camX.toFixed(1));
        el.style.setProperty('--cam-zoom', zoom.toFixed(3));
    }

    function fly({ x, z, duration }) {
        cancelAnimationFrame(raf);

        const fromX = camX, fromZ = zoom;
        const start = performance.now();
        let last = -Infinity;

        const tick = now => {
            const t = Math.min(1, (now - start) / duration);
            if (t < 1 && now - last < 1000 / FPS) {
                raf = requestAnimationFrame(tick);
                return;
            }

            last = now;
            const e = ease(t);
            camX = fromX + (x - fromX) * e;
            zoom = fromZ + (z - fromZ) * e;
            apply();

            if (t < 1)
                raf = requestAnimationFrame(tick);
        };

        raf = requestAnimationFrame(tick);
    }

    function startPendingFlight() {
        const flight = pendingFlight;
        pendingFlight = null;
        if (flight)
            fly(flight);
    }

    window.slNav = {
        init(url) {
            cancelAnimationFrame(raf);
            zoom = zoomOf(pathOf(url));
            apply();
        },

        async start(fromUrl, toUrl) {
            const from = pathOf(fromUrl), to = pathOf(toUrl);
            if (from === to)
                return false;

            const d = steps(from, to);
            const zFrom = zoomOf(from), zTo = zoomOf(to);
            const animate = moving();

            let kind = 'fade';
            let duration = 300;
            if (animate && zTo !== zFrom) {
                kind = zTo > zFrom ? 'zoom-in' : 'zoom-out';
                duration = ZOOM_MS;
            } else if (animate && d !== 0) {
                kind = d > 0 ? 'right' : 'left';
                duration = STEP_MS + EXTRA_STEP_MS * (Math.abs(d) - 1);
            }

            root.dataset.nav = kind;
            root.style.setProperty('--nav-duration', `${duration}ms`);

            const width = background()?.clientWidth || innerWidth;
            pendingFlight = animate
                ? { x: camX - d * width * STEP, z: zTo, duration }
                : null;
            if (!animate)
                zoom = zTo;

            if (!document.startViewTransition) {
                startPendingFlight();
                return false;
            }

            finishTransition?.();

            let captured;
            const capturedPromise = new Promise(resolve => captured = resolve);
            const done = new Promise(resolve => {
                finishTransition = resolve;
                setTimeout(resolve, TRANSITION_TIMEOUT_MS);
            });

            const transition = document.startViewTransition(() => {
                captured();
                return done;
            });

            transition.ready.then(startPendingFlight, startPendingFlight);
            transition.finished.finally(() => {
                if (root.dataset.nav === kind)
                    delete root.dataset.nav;
            });

            await capturedPromise;
            return true;
        },

        end() {
            const finish = finishTransition;
            finishTransition = null;

            if (finish)
                setTimeout(finish, 80);
            else
                startPendingFlight();

            if (!moving())
                apply();
        }
    };
})();
