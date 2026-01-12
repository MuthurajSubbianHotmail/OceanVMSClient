// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
//self.addEventListener('fetch', () => { });

// Lightweight service worker:
// - skipWaiting on install so a newly installed worker can become active quickly
// - clients.claim on activate so it controls pages
// - network-first fetch, fallback to cache if offline
// - respond to SKIP_WAITING messages from the page

// Lightweight SW: install -> skipWaiting, activate -> clients.claim, message handler for SKIP_WAITING
self.addEventListener('install', event => {
  // Make new SW install but wait for explicit activation (skipWaiting can be called from page)
  // Optionally call skipWaiting() here if you want immediate activation upon install:
  // self.skipWaiting();
  console.log('[SW] install');
});

self.addEventListener('activate', event => {
  console.log('[SW] activate');
  event.waitUntil(self.clients.claim());
  // Notify clients that SW is active (optional)
  self.clients.matchAll({ type: 'window' }).then(clients => {
    clients.forEach(c => c.postMessage({ type: 'SW_ACTIVATED' }));
  });
});

self.addEventListener('fetch', event => {
  // Simple network-first, fallback-to-cache approach
  event.respondWith(
    fetch(event.request).catch(() => caches.match(event.request))
  );
});

self.addEventListener('message', event => {
  if (!event.data) return;
  if (event.data.type === 'SKIP_WAITING') {
    console.log('[SW] skip waiting requested');
    self.skipWaiting();
  }
});