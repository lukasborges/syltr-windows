self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async () => {
    const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    const target = windows[0];
    if (target) {
      await target.focus();
      target.postMessage({
        type: 'notification-clicked',
        profile: event.notification.data?.profile || 'desconhecido'
      });
    }
  })());
});
