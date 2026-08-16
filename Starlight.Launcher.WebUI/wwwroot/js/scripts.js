const paintApi =
    globalThis.CSS?.paintWorklet ??
    globalThis.paintWorklet;

if (paintApi) {
    paintApi.addModule('js/animated-lines.js');
    paintApi.addModule('js/holiday-sprites.js');
    paintApi.addModule('js/smooth-corners.js');
}
