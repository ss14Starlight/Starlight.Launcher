// MIT

const PATH_CACHE = new Map();

function toPath(d) {
    if (!d) return null;
    let p = PATH_CACHE.get(d);
    if (p === undefined) {
        try { p = new Path2D(d); } catch { p = null; }
        PATH_CACHE.set(d, p);
    }
    return p;
}

function makeRng(a) {
    let s = a >>> 0;
    return function () {
        s = (s + 0x6D2B79F5) | 0;
        let t = Math.imul(s ^ (s >>> 15), 1 | s);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

const KINDS = ['a', 'b', 'c'];

registerPaint('sprite-field', class {
    static get inputProperties() {
        const props = [
            '--sprite-count',
            '--sprite-size',
            '--sprite-size-var',
            '--sprite-speed',
            '--sprite-dx',
            '--sprite-dy',
            '--sprite-spin',
            '--sprite-sway',
            '--sprite-sway-speed',
            '--sprite-opacity',
            '--sprite-twinkle',
            '--sprite-twinkle-speed',
            '--sprite-glow',
            '--sprite-seed',
            '--sprite-weights',
            'order'
        ];
        for (const k of KINDS) {
            props.push(
                `--sprite-${k}-path`,
                `--sprite-${k}-fill`,
                `--sprite-${k}-detail`,
                `--sprite-${k}-detail-fill`,
                `--sprite-${k}-stroke`,
                `--sprite-${k}-stroke-width`
            );
        }
        return props;
    }

    paint(ctx, size, properties) {
        const raw = p => properties.get(p)?.toString().trim() ?? '';
        const str = p => raw(p).replace(/^["']|["']$/g, '');
        const num = (p, d) => { const v = parseFloat(raw(p)); return Number.isFinite(v) ? v : d; };
        const int = (p, d) => { const v = parseInt(raw(p), 10); return Number.isFinite(v) ? v : d; };

        const kinds = [];
        for (const k of KINDS) {
            const path = toPath(str(`--sprite-${k}-path`));
            if (!path) continue;
            kinds.push({
                path,
                fill: str(`--sprite-${k}-fill`) || 'gray',
                detail: toPath(str(`--sprite-${k}-detail`)),
                detailFill: str(`--sprite-${k}-detail-fill`) || 'black',
                stroke: str(`--sprite-${k}-stroke`),
                strokeWidth: num(`--sprite-${k}-stroke-width`, 2)
            });
        }
        if (!kinds.length) return;

        const weights = raw('--sprite-weights')
            .split(/[\s,]+/).map(parseFloat).filter(Number.isFinite);
        const wTable = [];
        let wSum = 0;
        for (let i = 0; i < kinds.length; i++) {
            const w = Number.isFinite(weights[i]) ? Math.max(0, weights[i]) : 1;
            wSum += w;
            wTable.push(wSum);
        }
        if (wSum <= 0) return;

        const count = Math.max(0, int('--sprite-count', 24));
        const baseSize = num('--sprite-size', 1);
        const sizeVar = num('--sprite-size-var', 0.5);
        const speed = num('--sprite-speed', 1);
        const dx = num('--sprite-dx', 0);
        const dy = num('--sprite-dy', -0.35);
        const spin = num('--sprite-spin', 0);
        const sway = num('--sprite-sway', 0);
        const swaySpeed = num('--sprite-sway-speed', 1);
        const opacity = Math.max(0, Math.min(1, num('--sprite-opacity', 0.35)));
        const twinkle = Math.max(0, Math.min(1, num('--sprite-twinkle', 0)));
        const twinkleSpeed = num('--sprite-twinkle-speed', 1);
        const glow = Math.max(0, num('--sprite-glow', 0));
        const seed = int('--sprite-seed', 0);
        const frame = int('order', 0) * speed;

        const W = size.width, H = size.height;
        const pad = 80 * baseSize;
        const fieldW = W + pad * 2;
        const fieldH = H + pad * 2;

        const layers = [
            { frac: 0.45, scale: 0.55, speed: 0.45, alpha: 0.55 },
            { frac: 0.35, scale: 1.00, speed: 1.00, alpha: 0.85 },
            { frac: 0.20, scale: 1.60, speed: 1.70, alpha: 1.00 },
        ];

        let pid = 0;

        for (const layer of layers) {
            const n = Math.round(count * layer.frac);

            for (let i = 0; i < n; i++) {
                const rng = makeRng((seed * 2654435761 + (pid++) * 40503 + 1) | 0);

                const pick = rng() * wSum;
                let ki = 0;
                while (ki < wTable.length - 1 && pick > wTable[ki]) ki++;
                const kind = kinds[ki];

                const startX = rng() * fieldW;
                const startY = rng() * fieldH;
                const s = baseSize * layer.scale * (1 - sizeVar / 2 + rng() * sizeVar);
                const vx = (dx + (rng() - 0.5) * 0.25) * layer.speed;
                const vy = (dy + (rng() - 0.5) * 0.25) * layer.speed;

                let x = (startX + vx * frame) % fieldW;
                let y = (startY + vy * frame) % fieldH;
                if (x < 0) x += fieldW;
                if (y < 0) y += fieldH;
                x -= pad;
                y -= pad;

                if (sway) {
                    const phase = rng() * Math.PI * 2;
                    x += Math.sin(frame * 0.02 * swaySpeed + phase) * sway * s;
                }

                const rot = (rng() * Math.PI * 2) +
                    (spin ? frame * 0.004 * spin * (rng() < 0.5 ? -1 : 1) : 0);

                let tw = 1;
                if (twinkle > 0) {
                    const ph = rng() * Math.PI * 2;
                    const rate = 0.04 * twinkleSpeed * (0.6 + rng() * 0.8);
                    tw = (1 - twinkle) + twinkle * (0.5 + 0.5 * Math.sin(frame * rate + ph));
                }

                const alpha = opacity * layer.alpha * tw;
                if (alpha <= 0.004) continue;

                ctx.save();
                ctx.globalAlpha = alpha;
                ctx.translate(x, y);
                ctx.rotate(rot);
                ctx.scale(s, s);

                if (glow > 0) {
                    ctx.shadowColor = kind.fill;
                    ctx.shadowBlur = glow * 12 * tw;
                }

                if (kind.stroke) {
                    ctx.strokeStyle = kind.stroke;
                    ctx.lineWidth = kind.strokeWidth;
                    ctx.lineCap = 'round';
                    ctx.lineJoin = 'round';
                    ctx.stroke(kind.path);
                } else {
                    ctx.fillStyle = kind.fill;
                    ctx.fill(kind.path);
                }

                if (kind.detail) {
                    ctx.shadowBlur = 0;
                    ctx.fillStyle = kind.detailFill;
                    ctx.fill(kind.detail);
                }

                ctx.restore();
            }
        }

        ctx.globalAlpha = 1;
    }
});
