export function register(dotnet) {
  const onKeyDown = (event) => {
    if (event.altKey || event.shiftKey || event.isComposing) return;

    const target = event.target;
    const editing = target instanceof Element &&
      target.closest('input, textarea, select, button, a, [contenteditable="true"]');

    if (event.key === 'Escape' && document.querySelector('aside[aria-label="Warteschlange"]')) {
      event.preventDefault();
      dotnet.invokeMethodAsync('CloseQueue');
      return;
    }

    if ((event.key === '/' && !editing && !event.ctrlKey && !event.metaKey) ||
        ((event.key === 'k' || event.key === 'K') && (event.ctrlKey || event.metaKey))) {
      event.preventDefault();
      const input = document.querySelector('[data-elyra-search]');
      if (input) input.focus();
      else dotnet.invokeMethodAsync('OpenSearch');
      return;
    }

    if (editing || event.ctrlKey || event.metaKey || event.repeat) return;
    if (event.code === 'Space') {
      if (!document.querySelector('button[aria-label="Pause"]:not([disabled]), button[aria-label="Abspielen"]:not([disabled])')) return;
      event.preventDefault();
      dotnet.invokeMethodAsync('TogglePlayback');
    } else if (event.key === 'ArrowLeft') {
      if (!document.querySelector('input[aria-label="Wiedergabeposition"]:not([disabled])')) return;
      event.preventDefault();
      dotnet.invokeMethodAsync('SeekBy', -5);
    } else if (event.key === 'ArrowRight') {
      if (!document.querySelector('input[aria-label="Wiedergabeposition"]:not([disabled])')) return;
      event.preventDefault();
      dotnet.invokeMethodAsync('SeekBy', 5);
    }
  };

  document.addEventListener('keydown', onKeyDown);
  return { dispose: () => document.removeEventListener('keydown', onKeyDown) };
}
