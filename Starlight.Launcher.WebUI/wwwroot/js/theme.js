window.appTheme = {
    set: (name) => {
        document.documentElement.setAttribute('data-theme', name);
        const isDark = getComputedStyle(document.documentElement).colorScheme === 'dark';
        document.body.classList.toggle('mud-theme-dark', isDark);
    },
    prefersDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches,
    setAnimatedBackground: (on) => document.documentElement.classList.toggle('sl-bg-static', !on)
};