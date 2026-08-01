/* Media library page: copy-to-clipboard, the details dialogs, and drag-and-drop upload. */
(function () {
  'use strict';

  // ── Copy URL ───────────────────────────────────────────────────────────────
  document.querySelectorAll('[data-copy-url]').forEach((button) => {
    button.addEventListener('click', async () => {
      const url = new URL(button.dataset.copyUrl, window.location.origin).href;
      const original = button.textContent;

      try {
        await navigator.clipboard.writeText(url);
        button.textContent = 'Copied';
      } catch (error) {
        // Clipboard access is blocked outside a secure context; select the text instead.
        window.prompt('Copy this URL', url);
        return;
      }

      setTimeout(() => {
        button.textContent = original;
      }, 1400);
    });
  });

  // ── Details dialogs ────────────────────────────────────────────────────────
  document.querySelectorAll('[data-open-details]').forEach((button) => {
    button.addEventListener('click', () => {
      const dialog = document.getElementById(button.dataset.openDetails);
      if (dialog) dialog.showModal();
    });
  });

  document.querySelectorAll('[data-close-dialog]').forEach((button) => {
    button.addEventListener('click', () => {
      const dialog = button.closest('dialog');
      if (dialog) dialog.close();
    });
  });

  // ── Drag and drop ──────────────────────────────────────────────────────────
  const dropzone = document.getElementById('dropzone');
  const fileInput = document.getElementById('file-input');
  const uploadForm = document.getElementById('upload-form');
  if (!dropzone || !fileInput || !uploadForm) return;

  ['dragenter', 'dragover'].forEach((name) =>
    dropzone.addEventListener(name, (event) => {
      event.preventDefault();
      dropzone.classList.add('dragover');
    })
  );

  ['dragleave', 'drop'].forEach((name) =>
    dropzone.addEventListener(name, (event) => {
      event.preventDefault();
      dropzone.classList.remove('dragover');
    })
  );

  dropzone.addEventListener('drop', (event) => {
    if (!event.dataTransfer || event.dataTransfer.files.length === 0) return;

    fileInput.files = event.dataTransfer.files;
    uploadForm.requestSubmit();
  });
})();
