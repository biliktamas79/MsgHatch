// Sets the light or dark color mode of Bootstrap before the first paint, from the stored choice or the system setting,
// and switches it on a click of the button in the navigation bar (D266).
window.msghatchTheme = {
    current: function () {
        return document.documentElement.getAttribute('data-bs-theme') || 'light';
    },
    apply: function () {
        var theme = null;
        try { theme = localStorage.getItem('msghatch-theme'); } catch (e) { }
        if (theme !== 'light' && theme !== 'dark') {
            theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        document.documentElement.setAttribute('data-bs-theme', theme);
    },
    toggle: function () {
        var theme = window.msghatchTheme.current() === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem('msghatch-theme', theme); } catch (e) { }
        document.documentElement.setAttribute('data-bs-theme', theme);
    }
};
window.msghatchTheme.apply();
