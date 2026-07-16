(() => {
  const messageToken = '__SYLTR_MESSAGE_TOKEN__';
  const postMessage = window.chrome?.webview?.postMessage?.bind(window.chrome.webview);

  const openClickedLinkExternally = event => {
    if (!event.isTrusted || event.defaultPrevented || (event.button !== 0 && event.button !== 1)) {
      return;
    }

    const path = typeof event.composedPath === 'function' ? event.composedPath() : [];
    const anchor = path.find(node => node?.matches?.('a[href]')) ??
      event.target?.closest?.('a[href]');
    if (!anchor || anchor.download) {
      return;
    }

    const documentTarget = document.querySelector('base[target]')?.target ?? '';
    const target = (anchor.target || documentTarget).trim().toLowerCase();
    const opensNewContext = event.button === 1 || event.ctrlKey || event.metaKey ||
      (target !== '' && target !== '_self' && target !== '_top' && target !== '_parent');
    if (!opensNewContext) {
      return;
    }

    let destination;
    try {
      destination = new URL(anchor.href, document.baseURI);
    } catch {
      return;
    }

    if (destination.protocol !== 'http:' && destination.protocol !== 'https:') {
      return;
    }

    event.preventDefault();
    postMessage?.(`${messageToken}\n${destination.href}`);
  };

  // Capture before web-app handlers can stop propagation. Programmatic
  // window.open calls do not produce either event and therefore remain in-app.
  window.addEventListener('click', openClickedLinkExternally, true);
  window.addEventListener('auxclick', openClickedLinkExternally, true);
})();
