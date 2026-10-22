const DRAG_THRESHOLD = 5;
const AUTOSCROLL_EDGE = 40;
const AUTOSCROLL_SPEED = 12;

export function attach(container, dotnet) {
    detach(container);

    let pending = null;
    let drag = null;

    const items = () => [...container.querySelectorAll('[data-reorder-key]')];

    const contentY = clientY => clientY - container.getBoundingClientRect().top + container.scrollTop;

    function onPointerDown(e) {
        if (e.button !== 0 || drag) return;
        const handle = e.target.closest('[data-reorder-handle]');
        const item = e.target.closest('[data-reorder-key]');
        if (!handle || !item || !container.contains(item)) return;
        if (e.target.closest('button, a, input, textarea, [contenteditable]')) return;

        pending = { item, pointerId: e.pointerId, startX: e.clientX, startY: e.clientY };
    }

    function start(e) {
        const list = items();
        const from = list.indexOf(pending.item);
        if (from < 0) { pending = null; return; }

        const scroll = container.scrollTop;
        const top = container.getBoundingClientRect().top;
        const slots = list.map(el => {
            const r = el.getBoundingClientRect();
            return { el, top: r.top - top + scroll, height: r.height };
        });

        drag = {
            item: pending.item,
            pointerId: pending.pointerId,
            from,
            to: from,
            slots,
            grabOffset: contentY(pending.startY) - slots[from].top,
            lastClientY: e.clientY,
            frame: 0
        };
        pending = null;

        container.setPointerCapture(drag.pointerId);
        container.classList.add('reorder-active');
        drag.item.classList.add('reorder-dragging');
        document.body.classList.add('reorder-grabbing');

        drag.frame = requestAnimationFrame(autoScroll);
        update();
    }

    function update() {
        const { slots, from } = drag;
        const dragged = slots[from];
        const newTop = contentY(drag.lastClientY) - drag.grabOffset;
        const center = newTop + dragged.height / 2;

        const last = slots[slots.length - 1];
        const shownTop = Math.min(Math.max(newTop, slots[0].top), last.top + last.height - dragged.height);
        dragged.el.style.transform = `translateY(${shownTop - dragged.top}px)`;

        let to = from;
        for (let i = 0; i < slots.length; i++) {
            if (i === from) continue;
            const s = slots[i];
            const mid = s.top + s.height / 2;
            let shift = 0;
            if (i < from && center < mid) { shift = dragged.height; to--; }
            else if (i > from && center > mid) { shift = -dragged.height; to++; }
            s.el.style.transform = shift ? `translateY(${shift}px)` : '';
        }
        drag.to = to;
    }

    function autoScroll() {
        if (!drag) return;
        const r = container.getBoundingClientRect();
        let delta = 0;
        if (drag.lastClientY < r.top + AUTOSCROLL_EDGE) delta = -AUTOSCROLL_SPEED;
        else if (drag.lastClientY > r.bottom - AUTOSCROLL_EDGE) delta = AUTOSCROLL_SPEED;

        if (delta) {
            const before = container.scrollTop;
            container.scrollTop += delta;
            if (container.scrollTop !== before) update();
        }
        drag.frame = requestAnimationFrame(autoScroll);
    }

    function onPointerMove(e) {
        if (drag) {
            if (e.pointerId !== drag.pointerId) return;
            drag.lastClientY = e.clientY;
            update();
            return;
        }
        if (!pending || e.pointerId !== pending.pointerId) return;
        if (Math.abs(e.clientX - pending.startX) < DRAG_THRESHOLD && Math.abs(e.clientY - pending.startY) < DRAG_THRESHOLD) return;
        start(e);
    }

    function reset(state) {
        cancelAnimationFrame(state.frame);
        container.classList.remove('reorder-active');
        state.item.classList.remove('reorder-dragging');
        document.body.classList.remove('reorder-grabbing');
        for (const s of state.slots) s.el.style.transform = '';
    }

    async function finish(e, commit) {
        pending = null;
        if (!drag || e.pointerId !== drag.pointerId) return;

        if (commit) {
            drag.lastClientY = e.clientY;
            update();
        }

        const state = drag;
        drag = null;
        if (container.hasPointerCapture(state.pointerId))
            container.releasePointerCapture(state.pointerId);

        window.addEventListener('click', swallowClick, { capture: true, once: true });
        setTimeout(() => window.removeEventListener('click', swallowClick, { capture: true }), 0);

        cancelAnimationFrame(state.frame);
        if (commit && state.to !== state.from) {
            try {
                // Resolves once Blazor has rendered the new order, so the items don't jump back in between.
                await dotnet.invokeMethodAsync('MoveItem', state.item.dataset.reorderKey, state.to);
            } catch (err) {
                console.error(err);
            }
        }
        reset(state);
    }

    function swallowClick(e) {
        e.stopPropagation();
        e.preventDefault();
    }

    const onPointerUp = e => finish(e, true);
    const onPointerCancel = e => finish(e, false);
    const onKeyDown = e => {
        if (e.key === 'Escape' && drag) finish({ pointerId: drag.pointerId }, false);
    };

    container.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', onPointerCancel);
    window.addEventListener('keydown', onKeyDown);

    container._listReorder = () => {
        container.removeEventListener('pointerdown', onPointerDown);
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
        window.removeEventListener('pointercancel', onPointerCancel);
        window.removeEventListener('keydown', onKeyDown);
        if (drag) reset(drag);
        drag = pending = null;
    };
}

export function detach(container) {
    container?._listReorder?.();
    if (container) delete container._listReorder;
}
