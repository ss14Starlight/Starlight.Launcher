// MIT - darkrell
registerPaint('animated-lines', class {
    static get inputProperties() {
        return [
            '--circle',
            '--dot-color',
            '--dot-size',
            '--num-points',
            '--line-color',
            '--line-opacity',
            '--dot-speed',
            '--distance',
            '--seed',
            '--x',
            '--y',
            '--twinkle',
            '--twinkle-speed',
            '--glow',
            'order'
        ];
    }

    paint(ctx, size, properties) {
        const str = p => properties.get(p)?.toString().trim() ?? '';
        const num = (p, d) => { const v = parseFloat(str(p)); return Number.isFinite(v) ? v : d; };
        const int = (p, d) => { const v = parseInt(str(p), 10); return Number.isFinite(v) ? v : d; };

        const circle = /^(1|true|yes|on)$/i.test(str('--circle'));
        const dotColor = str('--dot-color') || 'gray';
        const lineColor = str('--line-color') || 'gray';
        const lineOpacity = num('--line-opacity', 0);
        const timeScale = num('--dot-speed', 1);
        const xoff = num('--x', 0);
        const yoff = num('--y', 0);
        const dotSize = int('--dot-size', 1);
        const numPoints = int('--num-points', 50);
        const distance = int('--distance', 100);
        const seed = int('--seed', 0);
        const twinkle = Math.max(0, Math.min(1, num('--twinkle', 0)));
        const twinkleSpeed = num('--twinkle-speed', 1);
        const glow = Math.max(0, num('--glow', 0));
        const frame = int('order', 0) * timeScale;

        const W = size.width, H = size.height;
        const cx = W / 2, cy = H / 2;

        const layers = [
            { frac: 0.55, sizeScale: 0.6, speedScale: 0.5 },
            { frac: 0.30, sizeScale: 1.0, speedScale: 1.0 },
            { frac: 0.15, sizeScale: 1.8, speedScale: 1.8 },
        ];

        let pid = 0;
        const pts = [];

        for (const layer of layers) {
            const count = Math.max(0, Math.round(numPoints * layer.frac));
            const layerPts = [];

            for (let i = 0; i < count; i++) {
                const rng = makeRng((seed * 2654435761 + (pid++) * 40503 + 1) | 0);

                const startX = rng() * W;
                const startY = rng() * H;
                const dirX = (rng() * 2 - 1) + xoff;
                const dirY = (rng() * 2 - 1) + yoff;
                const r = dotSize * layer.sizeScale * (0.6 + rng() * 0.8);

                let x, y;
                if (circle) {
                    const radius = Math.hypot(cx - startX, cy - startY);
                    const theta0 = rng() * Math.PI * 2;
                    const spin = (0.5 + rng()) * layer.speedScale * 0.02;
                    const theta = theta0 + frame * spin;
                    x = radius * Math.cos(theta) + cx;
                    y = radius * Math.sin(theta) + cy;
                } else {
                    x = (startX + dirX * frame * layer.speedScale) % W;
                    y = (startY + dirY * frame * layer.speedScale) % H;
                    if (x < 0) x += W;
                    if (y < 0) y += H;
                }

                const twPhase = rng() * Math.PI * 2;
                const twRate = 0.045 * twinkleSpeed * (0.6 + rng() * 0.9);
                let tw = 1;
                if (twinkle > 0) {
                    const osc = 0.5 + 0.5 * Math.sin(frame * twRate + twPhase);
                    tw = (1 - twinkle) + twinkle * osc;
                }

                const p = { x, y, r, tw };
                layerPts.push(p);
                pts.push(p);
            }

            ctx.fillStyle = dotColor;
            if (glow > 0) ctx.shadowColor = dotColor;
            for (const p of layerPts) {
                ctx.globalAlpha = p.tw;
                if (glow > 0) ctx.shadowBlur = glow * p.r * 4 * p.tw;
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.r * (0.9 + 0.1 * p.tw), 0, 2 * Math.PI);
                ctx.fill();
            }
            ctx.shadowBlur = 0;
        }
        ctx.globalAlpha = 1;

        if (lineOpacity > 0 && distance > 0 && pts.length > 1) {
            const linkDist = distance;
            const linkDistSq = linkDist * linkDist;
            ctx.strokeStyle = lineColor;

            const cell = linkDist;
            const grid = new Map();
            const key = (a, b) => a + ',' + b;

            for (let i = 0; i < pts.length; i++) {
                const k = key(Math.floor(pts[i].x / cell), Math.floor(pts[i].y / cell));
                let bucket = grid.get(k);
                if (!bucket) grid.set(k, bucket = []);
                bucket.push(i);
            }

            for (let i = 0; i < pts.length; i++) {
                const a = pts[i];
                const gx = Math.floor(a.x / cell);
                const gy = Math.floor(a.y / cell);
                for (let ox = -1; ox <= 1; ox++) {
                    for (let oy = -1; oy <= 1; oy++) {
                        const bucket = grid.get(key(gx + ox, gy + oy));
                        if (!bucket) continue;
                        for (const j of bucket) {
                            if (j <= i) continue;
                            const b = pts[j];
                            const dx = a.x - b.x, dy = a.y - b.y;
                            const d2 = dx * dx + dy * dy;
                            if (d2 >= linkDistSq) continue;
                            const t = 1 - Math.sqrt(d2) / linkDist;
                            const twAB = 0.65 + 0.35 * ((a.tw + b.tw) * 0.5);
                            ctx.globalAlpha = lineOpacity * (0.35 + 0.65 * t) * twAB;
                            ctx.lineWidth = Math.max(0.75, t * 2);
                            ctx.beginPath();
                            ctx.moveTo(a.x, a.y);
                            ctx.lineTo(b.x, b.y);
                            ctx.stroke();
                        }
                    }
                }
            }
        }
        ctx.globalAlpha = 1;
    }
});

function makeRng(a) {
    let s = a >>> 0;
    return function () {
        s = (s + 0x6D2B79F5) | 0;
        let t = Math.imul(s ^ (s >>> 15), 1 | s);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}
