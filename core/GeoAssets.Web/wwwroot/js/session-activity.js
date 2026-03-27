// Tracks user activity (mouse, keyboard, touch) and notifies the .NET
// SessionTimeoutService via JS interop to reset the inactivity timer.

window.sessionActivity = (function () {
    let dotNetHelper = null;
    let debounceTimer = null;
    const DEBOUNCE_MS = 1000; // max one call per second

    const events = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll', 'click'];

    function onActivity() {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(function () {
            if (dotNetHelper) {
                dotNetHelper.invokeMethodAsync('OnUserActivity').catch(() => {});
            }
        }, DEBOUNCE_MS);
    }

    return {
        init: function (helper) {
            dotNetHelper = helper;
            events.forEach(function (evt) {
                document.addEventListener(evt, onActivity, { passive: true });
            });
        },

        dispose: function () {
            dotNetHelper = null;
            clearTimeout(debounceTimer);
            events.forEach(function (evt) {
                document.removeEventListener(evt, onActivity);
            });
        }
    };
})();
