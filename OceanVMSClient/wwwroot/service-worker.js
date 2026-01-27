// Kill-switch service worker: auto-unregister and clear caches if origin is disabled.
// Deploy this temporarily (with no-cache headers for service-worker.js and index.html),
// wait for clients to pick it up, then stop the App Service.

const ORIGIN = self.location.origin;

// helper: clear all caches and unregister
async function doCleanupAndUnregister() {
  try {
    const keys = await caches.keys();
    await Promise.all(keys.map(k => caches.delete(k)));
  } catch (e) { /* ignore */ }

  try {
    await self.registration.unregister();
  } catch (e) { /* ignore */ }

  try {
    const clients = await self.clients.matchAll({ type: 'window' });
    clients.forEach(c => {
      try { c.postMessage({ type: 'KILL_SWITCH_TRIGGERED' }); } catch (e) {}
    });
  } catch (e) { /* ignore */ }
}

self.addEventListener('install', event => {
  // Activate fast so clients pick it up
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    try { await self.clients.claim(); } catch (e) {}
  })());
});

// Network-first. If we get a 403 or server error from same-origin, clean up and unregister.
self.addEventListener('fetch', event => {
  // Only handle same-origin requests for application navigation and assets
  const req = event.request;
  if (!req.url.startsWith(ORIGIN)) {
    return; // don't interfere with cross-origin requests
  }

  // Only intercept GETs to avoid breaking non-idempotent calls
  if req.method !== 'GET') return;

  event.respondWith((async () => {
    try {
      // Force fresh network response where possible (avoid serving stale cached responses)
      const networkResponse = await fetch(req.clone(), { cache: 'no-store', credentials: 'same-origin' });

      // If origin responded with disabled/forbidden (App Service stopped) or server error,
      // trigger cleanup so clients stop using cached UI.
      if (networkResponse && (networkResponse.status === 403 || networkResponse.status >= 500)) {
        // Run cleanup asynchronously but wait for it to run to avoid race conditions
        await doCleanupAndUnregister();
        // return the real (403/5xx) response so browser shows server error
        return networkResponse;
      }

      // Normal case: return network response
      return networkResponse;
    } catch (err) {
      // Network failed (e.g., offline). Clear caches/unregister to avoid stale UI if you prefer:
      // If you don't want to clear on network failure, comment next line.
      await doCleanupAndUnregister().catch(()=>{});
      // Let browser handle offline (fallback) or rethrow
      throw err;
    }
  })());
});

// Optional: respond to page messages asking for forced cleanup
self.addEventListener('message', (evt) => {
  if (!evt.data) return;
  if (evt.data.type === 'FORCE_KILL_SWITCH') {
    evt.waitUntil(doCleanupAndUnregister());
  }
});