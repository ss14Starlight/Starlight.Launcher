// MIT - darkrell
registerPaint('smooth-corners', class {
    static get inputProperties() {
        return [
            '--smooth-corners'
        ]
    }
    paint(ctx, geom, properties) {
        const c = properties.get('--smooth-corners').toString()

        ctx.fillStyle = 'black'

        const n = c
        let m = n
        if (n > 100) m = 100
        if (n < 0.00000000001) m = 0.00000000001
        const w = geom.width / 2
        const h = geom.height / 2
        const r = Math.min(w, h)
        const vo = Math.max(h - w, 0)//vertical offset
        const ho = Math.max(w - h, 0)//horizontal offset
        ctx.beginPath();

        ctx.moveTo(w - ho - r, (Math.pow(Math.abs(Math.pow(r, m) - Math.pow(r, m)), 1 / m)) + h + vo);


        for (let i = 1; i < (r + 1); i++) {
            const x = (i - r) + w - ho
            const y = (Math.pow(Math.abs(Math.pow(r, m) - Math.pow(Math.abs(i - r), m)), 1 / m)) + h + vo
            ctx.lineTo(x, y)
        }
        for (let i = r; i < (2 * r + 1); i++) {
            const x = (i - r) + w + ho
            const y = (Math.pow(Math.abs(Math.pow(r, m) - Math.pow(Math.abs(i - r), m)), 1 / m)) + h + vo
            ctx.lineTo(x, y)
        }
        for (let i = (2 * r); i < (3 * r + 1); i++) {
            const x = (3 * r - i) + w + ho
            const y = (-Math.pow(Math.abs(Math.pow(r, m) - Math.pow(Math.abs(3 * r - i), m)), 1 / m)) + h - vo
            ctx.lineTo(x, y)
        }
        for (let i = (3 * r); i < (4 * r + 1); i++) {
            const x = (3 * r - i) + w - ho
            const y = (-Math.pow(Math.abs(Math.pow(r, m) - Math.pow(Math.abs(3 * r - i), m)), 1 / m)) + h - vo
            ctx.lineTo(x, y)
        }

        ctx.closePath()
        ctx.fill()
    }
})
