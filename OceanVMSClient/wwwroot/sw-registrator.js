// Register service worker and expose helpers for Blazor
window.updateAvailable = new Promise((resolve, reject) => {
  if (!('serviceWorker' in navigator)) {
    reject("Service workers not supported");
    return;
  }

  navigator.serviceWorker.register('/service-worker.js')
    .then(reg => {
      console.info(`SW registered (scope: ${reg.scope})`);
      // If a waiting worker already exists, treat as update available
      if (reg.waiting) {
        resolve(true);
        return;
      }
      reg.onupdatefound = () => {
        const installing = reg.installing;
        installing.onstatechange = () => {
          if (installing.state === 'installed') {
            // If page is already controlled, this is an update
            resolve(!!navigator.serviceWorker.controller);
          }
        };
      };
    })
    .catch(err => {
      console.error('SW registration failed', err);
      reject(err);
    });
});

window.registerForUpdateAvailableNotification = (dotNetObjRef, methodName) => {
  window.updateAvailable
    .then(isUpdate => {
      if (isUpdate) {
        dotNetObjRef.invokeMethodAsync(methodName).catch(() => {});
      }
    })
    .catch(() => {});
};

window.activateUpdate = () => {
  return navigator.serviceWorker.getRegistration().then(reg => {
    if (!reg || !reg.waiting) return false;
    reg.waiting.postMessage({ type: 'SKIP_WAITING' });
    return true;
  });
};

window.reloadPage = () => {
  location.reload(true);
};

// When the controlling SW changes, reload once so page loads new assets
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!window.__sw_reloading) {
      window.__sw_reloading = true;
      window.location.reload();
    }
  });
}