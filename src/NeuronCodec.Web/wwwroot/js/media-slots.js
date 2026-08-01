/*
 * Wires any [data-pick-media] / [data-clear-media] button pair to a hidden id field and its
 * preview slot. Used by the article editor (featured and social image) and by the SEO page
 * (site-wide default share image).
 */
(function () {
  'use strict';

  function renderSlot(slot, file) {
    if (!slot) return;

    slot.innerHTML = '';

    if (!file) {
      const note = document.createElement('span');
      note.className = 'muted';
      note.textContent = 'No image selected.';
      slot.appendChild(note);
      return;
    }

    const img = document.createElement('img');
    img.src = file.url;
    img.alt = '';
    img.style.borderRadius = 'var(--radius-md)';
    img.style.maxWidth = '100%';

    const caption = document.createElement('span');
    caption.className = 'muted';
    caption.textContent = file.originalName;

    slot.append(img, caption);
  }

  document.querySelectorAll('[data-pick-media]').forEach((button) => {
    button.addEventListener('click', async () => {
      if (!window.mediaPicker) return;

      const file = await window.mediaPicker.open({ imagesOnly: true });
      if (!file) return;

      const field = document.getElementById(button.dataset.pickMedia);
      if (field) field.value = file.id;

      renderSlot(document.getElementById(button.dataset.slot), file);
    });
  });

  document.querySelectorAll('[data-clear-media]').forEach((button) => {
    button.addEventListener('click', () => {
      const field = document.getElementById(button.dataset.clearMedia);
      if (field) field.value = '';

      renderSlot(document.getElementById(button.dataset.slot), null);
    });
  });
})();
