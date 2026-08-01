/*
 * Article editor: EasyMDE over the markdown field, plus the slug, scheduling and media wiring
 * around it. The preview pipeline is marked → DOMPurify → highlight.js, and the preview pane
 * carries the site's own .prose class so what you see matches the published article.
 */
(function () {
  'use strict';

  const textarea = document.getElementById('markdown-editor');
  if (!textarea || typeof EasyMDE === 'undefined') return;

  const form = document.getElementById('article-form');
  const titleInput = document.getElementById('article-title');
  const slugInput = document.getElementById('article-slug');
  const statusSelect = document.getElementById('article-status');
  const publishHint = document.getElementById('publish-hint');
  const readOverride = document.getElementById('read-override');
  const readMinutes = document.getElementById('read-minutes');

  // ── Preview ────────────────────────────────────────────────────────────────
  if (typeof marked !== 'undefined') {
    marked.setOptions({ gfm: true, breaks: false, headerIds: true, mangle: false });
  }

  function renderPreview(plainText) {
    if (typeof marked === 'undefined') return plainText;

    let html = marked.parse(plainText);
    if (typeof DOMPurify !== 'undefined') html = DOMPurify.sanitize(html);

    // Highlighting needs real elements, so parse into a detached node first. The preview pane
    // itself already carries .prose via previewClass.
    const scratch = document.createElement('div');
    scratch.innerHTML = html;

    if (typeof hljs !== 'undefined') {
      scratch.querySelectorAll('pre code').forEach((block) => {
        try {
          hljs.highlightElement(block);
        } catch (error) {
          // A failed highlight should never blank the preview.
        }
      });
    }

    return scratch.innerHTML;
  }

  // ── Media insertion ────────────────────────────────────────────────────────
  async function insertMedia(editor) {
    if (!window.mediaPicker) return;

    const file = await window.mediaPicker.open({ imagesOnly: false });
    if (!file) return;

    const cm = editor.codemirror;
    const alt = file.altText || file.originalName.replace(/\.[^.]+$/, '');
    const snippet = file.isImage
      ? '![' + alt + '](' + file.url + ')'
      : '[' + file.originalName + '](' + file.url + ')';

    cm.replaceSelection(snippet);
    cm.focus();
  }

  // ── Editor ─────────────────────────────────────────────────────────────────
  const easyMDE = new EasyMDE({
    element: textarea,
    autoDownloadFontAwesome: false,
    spellChecker: false,
    // Autosaving to localStorage would silently resurrect old drafts across articles.
    autosave: { enabled: false },
    forceSync: true,
    lineWrapping: true,
    indentWithTabs: false,
    tabSize: 2,
    placeholder: 'Write the article in markdown…',
    status: ['lines', 'words', 'cursor'],
    previewClass: ['editor-preview', 'prose'],
    previewRender: renderPreview,
    renderingConfig: { codeSyntaxHighlighting: false },
    toolbar: [
      'bold', 'italic', 'strikethrough', '|',
      'heading-1', 'heading-2', 'heading-3', '|',
      'quote', 'unordered-list', 'ordered-list', 'code', 'table', '|',
      'link',
      {
        name: 'insert-media',
        action: insertMedia,
        className: 'fa fa-picture-o',
        title: 'Insert from the media library',
        text: '🖼',
      },
      '|',
      'preview', 'side-by-side', 'fullscreen', '|',
      'guide',
    ],
    shortcuts: {
      toggleSideBySide: 'Cmd-Alt-P',
      toggleFullScreen: 'F11',
    },
  });

  // EasyMDE binds Ctrl/Cmd+S to nothing by default; make it save the form.
  easyMDE.codemirror.setOption('extraKeys', Object.assign({}, easyMDE.codemirror.getOption('extraKeys'), {
    'Cmd-S': submitForm,
    'Ctrl-S': submitForm,
  }));

  function submitForm() {
    easyMDE.codemirror.save();
    if (form) form.requestSubmit();
  }

  document.addEventListener('keydown', (event) => {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      submitForm();
    }
  });

  // ── Slug ───────────────────────────────────────────────────────────────────
  function slugify(value) {
    return value
      .normalize('NFD')
      .replace(/[^\x00-\x7F]/g, '') // drop accents and other non-ASCII, matching Slug.From
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  // Only mirror the title while the slug has never been set by hand — an existing article's
  // permalink must not change just because its title was edited.
  let slugIsAuto = slugInput && slugInput.value.trim() === '';

  if (slugInput) {
    slugInput.addEventListener('input', () => {
      slugIsAuto = slugInput.value.trim() === '';
    });
  }

  if (titleInput && slugInput) {
    titleInput.addEventListener('input', () => {
      if (slugIsAuto) slugInput.value = slugify(titleInput.value);
    });
  }

  const regenerate = document.getElementById('slug-from-title');
  if (regenerate && titleInput && slugInput) {
    regenerate.addEventListener('click', () => {
      slugInput.value = slugify(titleInput.value);
      slugIsAuto = false;
    });
  }

  // ── Publish state ──────────────────────────────────────────────────────────
  function syncPublishHint() {
    if (!statusSelect || !publishHint) return;

    switch (statusSelect.value) {
      case 'Scheduled':
        publishHint.textContent = 'The article goes live automatically at this time. Use a future date.';
        break;
      case 'Published':
        publishHint.textContent = 'Live as soon as you save. Shown on the article and used for ordering.';
        break;
      default:
        publishHint.textContent = 'Drafts are private. This date is kept for when you publish.';
    }
  }

  if (statusSelect) {
    statusSelect.addEventListener('change', syncPublishHint);
    syncPublishHint();
  }

  // ── Reading time ───────────────────────────────────────────────────────────
  function syncReadMinutes() {
    if (!readOverride || !readMinutes) return;
    readMinutes.disabled = !readOverride.checked;
  }

  if (readOverride) {
    readOverride.addEventListener('change', syncReadMinutes);
    syncReadMinutes();
  }

  // The featured and social image slots are wired by media-slots.js, which this page also loads.
})();
