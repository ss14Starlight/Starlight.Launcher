window.appTheme = {
    set: (name) => {
        document.documentElement.setAttribute('data-theme', name);
        const isDark = name.endsWith('-dark') || name === 'midnight';
        document.body.classList.toggle('mud-theme-dark', isDark);
    },
    prefersDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches
};