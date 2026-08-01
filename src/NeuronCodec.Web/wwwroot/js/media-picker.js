/*
 * Media picker. One dialog, reused by the article editor for the featured image, the social
 * image and inline insertion from the markdown toolbar.
 *
 * window.mediaPicker.open({ imagesOnly }) resolves with the chosen file, or null if dismissed.
 */
(function () {
  'use strict';

  const dialog = document.getElementById('media-picker');
  if (!dialog) return;

  const grid = dialog.querySelector('[data-picker-grid]');
  const search = dialog.querySelector('[data-picker-search]');
  const uploadInput = dialog.querySelector('[data-picker-upload]');
  const uploadButton = dialog.querySelector('[data-picker-upload-button]');
  const status = dialog.querySelector('[data-picker-status]');
  const insertButton = dialog.querySelector('[data-picker-insert]');

  let selected = null;
  let resolvePick = null;
  let imagesOnly = false;

  function antiForgeryToken() {
    const field = document.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function setStatus(message, isError) {
    status.textContent = message || '';
    status.className = isError ? 'field-error' : 'muted';
  }

  function select(file, button) {
    selected = file;
    grid.querySelectorAll('.media-item').forEach((el) => el.setAttribute('aria-pressed', 'false'));
    if (button) button.setAttribute('aria-pressed', 'true');
    insertButton.disabled = !file;
  }

  function renderItem(file) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'media-item';
    button.setAttribute('aria-pressed', 'false');

    const thumb = document.createElement('div');
    thumb.className = 'media-thumb';
    if (file.isImage) {
      const img = document.createElement('img');
      img.src = file.url;
      img.alt = file.altText || '';
      img.loading = 'lazy';
      thumb.appendChild(img);
    } else {
      const ext = document.createElement('span');
      ext.className = 'media-ext';
      ext.textContent = (file.originalName.split('.').pop() || 'file').slice(0, 5);
      thumb.appendChild(ext);
    }

    const name = document.createElement('span');
    name.className = 'media-name';
    name.textContent = file.originalName;
    name.title = file.originalName;

    button.append(thumb, name);
    button.addEventListener('click', () => select(file, button));
    button.addEventListener('dblclick', () => {
      select(file, button);
      finish(file);
    });

    return button;
  }

  async function load() {
    setStatus('Loading…');
    grid.innerHTML = '';
    select(null, null);

    const params = new URLSearchParams({ handler: 'List' });
    if (search.value.trim()) params.set('q', search.value.trim());
    if (imagesOnly) params.set('imagesOnly', 'true');

    try {
      const response = await fetch('/admin/media?' + params.toString(), {
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error('Request failed: ' + response.status);

      const data = await response.json();
      // Handler results are camel-cased by the JSON serializer.
      const items = data.items || [];

      if (items.length === 0) {
        setStatus(search.value.trim() ? 'No files match that search.' : 'No files uploaded yet.');
        return;
      }

      setStatus('');
      items.forEach((file) => grid.appendChild(renderItem(file)));
    } catch (error) {
      setStatus('Could not load the media library. ' + error.message, true);
    }
  }

  async function upload() {
    if (!uploadInput.files || uploadInput.files.length === 0) {
      setStatus('Choose a file first.', true);
      return;
    }

    const body = new FormData();
    Array.from(uploadInput.files).forEach((file) => body.append('files', file));

    setStatus('Uploading…');
    uploadButton.disabled = true;

    try {
      const response = await fetch('/admin/media?handler=Upload', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          RequestVerificationToken: antiForgeryToken(),
        },
        body,
      });

      const data = await response.json();
      if (!response.ok) throw new Error(data.error || 'Upload failed.');

      if (data.errors && data.errors.length > 0) setStatus(data.errors.join(' '), true);
      else setStatus('Uploaded.');

      uploadInput.value = '';
      await load();

      // Preselect the first thing that just uploaded so one click inserts it.
      if (data.saved && data.saved.length > 0) {
        const first = grid.querySelector('.media-item');
        if (first) first.click();
      }
    } catch (error) {
      setStatus(error.message, true);
    } finally {
      uploadButton.disabled = false;
    }
  }

  function finish(file) {
    const resolve = resolvePick;
    resolvePick = null;
    dialog.close();
    if (resolve) resolve(file || null);
  }

  insertButton.addEventListener('click', () => finish(selected));
  dialog.querySelectorAll('[data-close-dialog]').forEach((el) =>
    el.addEventListener('click', () => finish(null))
  );

  // Covers Escape and any other native dismissal.
  dialog.addEventListener('close', () => {
    if (resolvePick) {
      const resolve = resolvePick;
      resolvePick = null;
      resolve(null);
    }
  });

  uploadButton.addEventListener('click', upload);

  let searchTimer = null;
  search.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(load, 250);
  });

  window.mediaPicker = {
    open(options) {
      imagesOnly = Boolean(options && options.imagesOnly);
      search.value = '';
      insertButton.disabled = true;
      dialog.showModal();
      load();
      return new Promise((resolve) => {
        resolvePick = resolve;
      });
    },
  };
})();
