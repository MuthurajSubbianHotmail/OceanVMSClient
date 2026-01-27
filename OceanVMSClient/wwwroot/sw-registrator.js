// Register service worker and expose helpers for Blazor
window.updateAvailable = new Promise((resolve, reject) => {
  if (!('serviceWorker' in navigator)) {
    reject("Service workers not supported");
    return;
  }

  // Prefer published SW if its manifest exists
  const publishedSW = '/service-worker.published.js';
  const liteSW = '/service-worker.js';
  const checkManifest = fetch('/service-worker-assets.js', { method: 'HEAD', cache: 'no-store' })
    .then(r => r.ok)
    .catch(() => false);

  checkManifest.then(hasManifest => {
    const swToRegister = hasManifest ? publishedSW : liteSW;
    navigator.serviceWorker.register(swToRegister)
      .then(reg => {
        console.info(`SW registered (scope: ${reg.scope}, script: ${swToRegister})`);
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

// Diagnostic helper: return registration state
window.getServiceWorkerStatus = async () => {
  if (!('serviceWorker' in navigator)) return { supported: false };
  const reg = await navigator.serviceWorker.getRegistration();
  return {
    supported: true,
    registered: !!reg,
    waiting: !!(reg && reg.waiting),
    installing: !!(reg && reg.installing),
    active: !!(reg && reg.active),
    scope: reg ? reg.scope : null
  };
};

// Diagnostic helper: unregister all SWs and delete caches (use for recovery)
window.clearServiceWorkerAndCaches = async () => {
  if (!('serviceWorker' in navigator)) return false;
  const regs = await navigator.serviceWorker.getRegistrations();
  await Promise.all(regs.map(r => r.unregister()));
  const keys = await caches.keys();
  await Promise.all(keys.map(k => caches.delete(k)));
  return true;
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