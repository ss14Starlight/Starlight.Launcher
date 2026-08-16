export function init(dotnet) {
    document.addEventListener('pointerdown', e => {
        if (e.button !== 0 || e.pointerType === 'touch') return;
        const bar = e.target.closest('.app-titlebar');
        if (!bar || e.target.closest('[data-no-drag]')) return;

        e.preventDefault();
        if (e.detail === 2) dotnet.invokeMethodAsync('ToggleMaximize');
        else dotnet.invokeMethodAsync('BeginDrag');
    }, true);
}
