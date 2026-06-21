// Service worker — NETWORK-FIRST for the app shell so every deploy reaches installed PWAs.
//
// The previous version was (a) written with TypeScript type annotations, which is invalid JS when
// served raw — so it could never install, leaving an older SW stuck — and (b) cache-first with a
// static cache name, which froze PWAs on whatever build they first cached. This version is plain JS,
// serves navigations/HTML from the network first (cache only as an offline fallback), and bumps the
// cache name so the stale caches are cleared on activate.
const CACHE_NAME = 'thosedays-shell-v2';

self.addEventListener('install', function () {
  self.skipWaiting(); // take over as soon as this (valid) SW is parsed
});

self.addEventListener('activate', function (event) {
  event.waitUntil(
    caches.keys()
      .then(function (names) {
        return Promise.all(names.map(function (n) { return n === CACHE_NAME ? null : caches.delete(n); }));
      })
      .then(function () { return self.clients.claim(); })
  );
});

self.addEventListener('fetch', function (event) {
  var request = event.request;
  if (request.method !== 'GET') return;

  var url;
  try { url = new URL(request.url); } catch (e) { return; }

  // App shell (navigations / HTML): NETWORK-FIRST — always pick up the latest deploy; fall back to
  // cache only when offline. (Cache-first here is exactly what froze PWAs on stale builds.)
  if (request.mode === 'navigate' || url.pathname === '/' || url.pathname.endsWith('.html')) {
    event.respondWith(
      fetch(request)
        .then(function (res) {
          var copy = res.clone();
          caches.open(CACHE_NAME).then(function (c) { c.put(request, copy); });
          return res;
        })
        .catch(function () {
          return caches.match(request).then(function (r) { return r || caches.match('/index.html'); });
        })
    );
    return;
  }

  // API GETs: network-first with cache fallback (last-seen data when offline).
  if (url.pathname.indexOf('/api/') !== -1) {
    event.respondWith(
      fetch(request)
        .then(function (res) {
          if (res && res.status === 200 && res.type !== 'error') {
            var copy = res.clone();
            caches.open(CACHE_NAME).then(function (c) { c.put(request, copy); });
          }
          return res;
        })
        .catch(function () { return caches.match(request); })
    );
    return;
  }

  // Content-hashed static assets (immutable per build): cache-first, network fallback.
  event.respondWith(
    caches.match(request).then(function (r) {
      return r || fetch(request).then(function (res) {
        if (res && res.status === 200 && res.type === 'basic') {
          var copy = res.clone();
          caches.open(CACHE_NAME).then(function (c) { c.put(request, copy); });
        }
        return res;
      });
    })
  );
});
