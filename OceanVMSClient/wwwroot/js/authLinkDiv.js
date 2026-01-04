// Simple utility to close the panel when clicking/tapping outside it.
// Include this file in wwwroot/index.html: <script src="js/authLinkDiv.js"></script>

window.authLinkDiv = (function () {
    // store handlers on the element so multiple components won't conflict
    function registerOutsideClick(element, dotNetHelper) {
        if (!element) return;

        // make sure we don't add another handler if one already exists
        if (element.__authLinkDivHandler) {
            return;
        }

        const handler = function (e) {
            try {
                if (!element.contains(e.target)) {
                    dotNetHelper?.invokeMethodAsync('NotifyClickOutside');
                }
            } catch (err) {
                // swallow errors
            }
        };

        // Listen to both click and touchstart for mobile
        document.addEventListener('click', handler, true);
        document.addEventListener('touchstart', handler, true);

        element.__authLinkDivHandler = handler;
    }

    function unregisterOutsideClick(element) {
        if (!element) return;
        const handler = element.__authLinkDivHandler;
        if (handler) {
            document.removeEventListener('click', handler, true);
            document.removeEventListener('touchstart', handler, true);
            delete element.__authLinkDivHandler;
        }
    }

    return {
        registerOutsideClick: registerOutsideClick,
        unregisterOutsideClick: unregisterOutsideClick
    };
})();
