/* Copy-to-clipboard for any [data-copy-text] button. */
(function () {
  'use strict';

  document.querySelectorAll('[data-copy-text]').forEach((button) => {
    button.addEventListener('click', async () => {
      const text = button.dataset.copyText;
      const original = button.textContent;

      try {
        await navigator.clipboard.writeText(text);
        button.textContent = 'Copied';
      } catch (error) {
        // Clipboard access needs a secure context; fall back to a selectable prompt.
        window.prompt('Copy this text', text);
        return;
      }

      setTimeout(() => {
        button.textContent = original;
      }, 1400);
    });
  });
})();
