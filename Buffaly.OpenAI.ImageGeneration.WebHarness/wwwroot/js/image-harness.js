(function () {
  const apiBaseUrl = window.location.origin + '/api/buffaly.openai.imagegeneration.webharness/image-generation-harness-json-ws-service';
  const service = createHarnessService(apiBaseUrl);
  const launchParams = new URLSearchParams(window.location.search);

  function createHarnessService(baseUrl) {
    async function invoke(kebab, methodName, request) {
      const response = await fetch(baseUrl + '/' + kebab, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'kcs-IsAjax': 'true',
          'kcs-Serialize': '{}'
        },
        body: JSON.stringify({ Method: methodName, request })
      });
      const text = await response.text();
      if (!response.ok) throw new Error(text || ('HTTP ' + response.status));
      try { return JSON.parse(text); }
      catch { return text; }
    }

    return {
      GetConfigAsync: request => invoke('get-config', 'GetConfig', request),
      GenerateImageAsync: request => invoke('generate-image', 'GenerateImage', request),
      EditImageAsync: request => invoke('edit-image', 'EditImage', request),
      ListOutputsAsync: request => invoke('list-outputs', 'ListOutputs', request),
      DeleteOutputAsync: request => invoke('delete-output', 'DeleteOutput', request)
    };
  }
  const state = {
    activeImageUrl: '',
    activeOutputFilePath: '',
    activeFile: null,
    activeName: '',
    activeKind: '',
    drawing: false,
    maskHasPaint: false,
    maskMode: 'paint',
    maskUndo: [],
    maskDiagnosticsTimer: 0,
    lastDebugSourceUrl: '',
    lastDebugMaskUrl: '',
    debugViewMode: 'final',
    debugViews: {},
    rootDirectory: launchParams.get('rootDirectory') || '',
    fileName: launchParams.get('fileName') || ''
  };

  const el = {
    apiStatus: document.getElementById('apiStatus'),
    model: document.getElementById('modelSelect'),
    size: document.getElementById('sizeSelect'),
    quality: document.getElementById('qualitySelect'),
    format: document.getElementById('formatSelect'),
    compression: document.getElementById('compressionInput'),
    generatePrompt: document.getElementById('generatePrompt'),
    editPrompt: document.getElementById('editPrompt'),
    sourceImages: document.getElementById('sourceImages'),
    dropZone: document.getElementById('dropZone'),
    imageStage: document.getElementById('imageStage'),
    activeImage: document.getElementById('activeImage'),
    activeTitle: document.getElementById('activeTitle'),
    activeMeta: document.getElementById('activeMeta'),
    maskCanvas: document.getElementById('maskCanvas'),
    brushSize: document.getElementById('brushSize'),
    paintMaskButton: document.getElementById('paintMaskButton'),
    eraseMaskButton: document.getElementById('eraseMaskButton'),
    undoMaskButton: document.getElementById('undoMaskButton'),
    clearMaskButton: document.getElementById('clearMaskButton'),
    generateButton: document.getElementById('generateButton'),
    editButton: document.getElementById('editButton'),
    downloadResultLink: document.getElementById('downloadResultLink'),
    copyPathButton: document.getElementById('copyPathButton'),
    refreshGalleryButton: document.getElementById('refreshGalleryButton'),
    galleryGrid: document.getElementById('galleryGrid'),
    generateMessage: document.getElementById('generateMessage'),
    editMessage: document.getElementById('editMessage'),
    lastResult: document.getElementById('lastResult'),
    copyResultButton: document.getElementById('copyResultButton'),
    maskHint: document.getElementById('maskHint'),
    maskDiagnostics: document.getElementById('maskDiagnostics'),
    downloadDebugSourceLink: document.getElementById('downloadDebugSourceLink'),
    downloadDebugMaskLink: document.getElementById('downloadDebugMaskLink'),
    debugTabs: document.getElementById('debugViewTabs'),
    debugCanvasStatus: document.getElementById('debugSizingDiagnostics')
  };

  function sharedRequest() {
    return {
      Model: el.model.value,
      Size: el.size.value,
      Quality: el.quality.value,
      Background: 'opaque',
      OutputFormat: el.format.value,
      OutputCompression: el.compression.value.trim(),
      RootDirectory: state.rootDirectory,
      FileName: state.fileName
    };
  }

  function setStatus(text, kind) {
    el.apiStatus.textContent = text;
    el.apiStatus.className = 'status-pill ' + (kind || '');
  }

  function setMessage(target, text, kind) {
    target.textContent = text || '';
    target.className = 'message ' + (kind || '');
  }

  function setBusy(button, busy, text) {
    if (busy) {
      button.dataset.originalText = button.textContent;
      button.textContent = text;
      button.disabled = true;
      return;
    }
    button.disabled = false;
    button.textContent = button.dataset.originalText || button.textContent;
  }

  function setAllBusy(isBusy) {
    setBusy(el.generateButton, isBusy, 'Working...');
    setBusy(el.editButton, isBusy, 'Working...');
  }

  function normalizeResult(result) {
    const normalized = {
      success: result.Success === true || result.success === true,
      error: result.Error || result.error || '',
      outputFilePath: result.OutputFilePath || result.outputFilePath || '',
      imageUrl: result.ImageUrl || result.imageUrl || '',
      raw: {}
    };

    const rawJson = result.RawJson || result.rawJson || '';
    if (rawJson) {
      try { normalized.raw = JSON.parse(rawJson); }
      catch { normalized.raw = { rawJson }; }
    }
    return normalized;
  }

  async function promoteResult(result, label) {
    el.lastResult.textContent = JSON.stringify(result, null, 2);
    if (!result.success || !result.imageUrl) return;

    const imageUrl = cacheBust(result.imageUrl);
    const name = fileNameFromPath(result.outputFilePath) || label || 'image.png';
    const file = await urlToFile(imageUrl, name);
    setActiveImage({
      url: imageUrl,
      file,
      name,
      outputFilePath: result.outputFilePath || '',
      kind: label || 'Generated image',
      bytes: result.raw && result.raw.bytes ? result.raw.bytes : file.size,
      model: result.raw && result.raw.model ? result.raw.model : el.model.value
    });
    refreshGallery().catch(() => {});
  }

  function setActiveImage({ url, file, name, outputFilePath, kind, bytes, model }) {
    state.activeImageUrl = url || '';
    state.activeOutputFilePath = outputFilePath || '';
    state.activeFile = file || null;
    state.activeName = name || fileNameFromPath(outputFilePath) || 'image.png';
    state.activeKind = kind || 'Active image';
    clearMask();
    if (!state.activeImageUrl) {
      el.activeImage.removeAttribute('src');
      el.imageStage.classList.remove('has-image');
      el.activeTitle.textContent = 'No image loaded';
      el.activeMeta.textContent = 'Generate an image, load one from disk, or choose a historical output.';
      el.downloadResultLink.href = '#';
      el.downloadResultLink.classList.add('disabled');
      return;
    }

    el.activeImage.src = state.activeImageUrl;
    el.activeTitle.textContent = state.activeName;
    el.activeMeta.textContent = [state.activeKind, model, formatBytes(bytes)].filter(Boolean).join(' / ');
    el.downloadResultLink.href = state.activeImageUrl;
    el.downloadResultLink.download = state.activeName;
    el.downloadResultLink.classList.remove('disabled');
    updateHistorySelection();
  }

  async function generateImage() {
    const prompt = el.generatePrompt.value.trim();
    if (!prompt) {
      setMessage(el.generateMessage, 'Enter a prompt first.', 'error');
      return;
    }

    const request = { ...sharedRequest(), Prompt: prompt };
    setAllBusy(true);
    setMessage(el.generateMessage, 'Generating image...', '');
    try {
      const result = normalizeResult(await service.GenerateImageAsync(request));
      await promoteResult(result, 'Generated image');
      setMessage(el.generateMessage, result.success ? 'Generated image is now on the canvas.' : result.error || 'Generation failed.', result.success ? 'ready' : 'error');
    } catch (error) {
      setMessage(el.generateMessage, String(error), 'error');
    } finally {
      setAllBusy(false);
    }
  }

  async function editImage() {
    const prompt = el.editPrompt.value.trim();
    if (!prompt) {
      setMessage(el.editMessage, 'Enter an edit instruction first.', 'error');
      return;
    }
    if (!state.activeFile) {
      setMessage(el.editMessage, 'Load, generate, or select one active image first.', 'error');
      return;
    }

    setAllBusy(true);
    setMessage(el.editMessage, 'Preparing active canvas image...', '');
    try {
      const sourceFile = state.activeFile;
      let mask = null;
      let maskDiagnostics = null;
      clearDebugLinks();
      if (state.maskHasPaint) {
        const maskBlob = await createApiMaskBlob();
        maskDiagnostics = await analyzeMaskBlob(maskBlob);
        setDebugDownload(el.downloadDebugSourceLink, sourceFile, 'debug-source.png');
        setDebugDownload(el.downloadDebugMaskLink, maskBlob, 'debug-edit-mask.png');
        renderMaskDiagnostics(maskDiagnostics);
        mask = await fileToPayload(new File([maskBlob], 'edit-mask.png', { type: 'image/png' }));
      } else {
        renderMaskDiagnostics(null);
      }
      const images = [await fileToPayload(sourceFile)];

      const requestPrompt = mask
        ? 'Inpaint only inside the transparent mask area. Preserve the original image outside the mask. Match the original scale, perspective, lighting, line style, and camera angle. Fit the requested subject completely inside the masked area. Edit request: ' + prompt
        : prompt;
      const request = { ...sharedRequest(), Prompt: requestPrompt, Images: images, Mask: mask };
      setMessage(el.editMessage, mask ? 'Editing with the painted mask...' : 'Editing the whole image...', '');
      const result = normalizeResult(await service.EditImageAsync(request));
      if (maskDiagnostics) {
        result.maskDiagnostics = maskDiagnostics;
        if (result.raw && result.raw.debugUploads) {
          result.maskDiagnostics.serverDebugUploads = result.raw.debugUploads;
        }
        el.lastResult.textContent = JSON.stringify(result, null, 2);
        updateDebugViews(result, maskDiagnostics);
      }
      await promoteResult(result, 'Edited image');
      setMessage(el.editMessage, result.success ? 'Edited result replaced the canvas. Your previous image remains in History.' : result.error || 'Edit failed.', result.success ? 'ready' : 'error');
    } catch (error) {
      setMessage(el.editMessage, String(error), 'error');
    } finally {
      setAllBusy(false);
    }
  }
  async function refreshGallery() {
    const result = await service.ListOutputsAsync({ RootDirectory: state.rootDirectory, FileName: state.fileName });
    const outputs = result.Outputs || result.outputs || [];
    renderGallery(outputs);
  }

  function renderGallery(outputs) {
    if (!outputs.length) {
      el.galleryGrid.innerHTML = '<div class="empty-state">No saved outputs found yet.</div>';
      return;
    }

    el.galleryGrid.innerHTML = outputs.map(output => {
      const url = output.ImageUrl || output.imageUrl || '';
      const filePath = output.FilePath || output.filePath || '';
      const fileName = output.FileName || output.fileName || fileNameFromPath(filePath) || 'image';
      const bytes = formatBytes(output.Bytes || output.bytes || 0);
      return '<article class="history-item" data-history-url="' + escapeAttr(url) + '" data-history-name="' + escapeAttr(fileName) + '" data-history-path="' + escapeAttr(filePath) + '">' +
        '<img src="' + escapeAttr(cacheBust(url, 'thumb')) + '" alt="' + escapeAttr(fileName) + '">' +
        '<div>' +
          '<div class="history-name" title="' + escapeAttr(fileName) + '">' + escapeHtml(fileName) + '</div>' +
          '<div class="history-meta">' + escapeHtml(bytes || 'Saved output') + '</div>' +
          '<div class="history-actions">' +
            '<a class="button-link" href="' + escapeAttr(url) + '" download="' + escapeAttr(fileName) + '" data-ignore-history="true">Download</a>' +
            '<button class="secondary" type="button" data-gallery-action="copy" data-path="' + escapeAttr(filePath) + '">Copy path</button>' +
            '<button class="secondary danger" type="button" data-gallery-action="delete" data-file-name="' + escapeAttr(fileName) + '" data-path="' + escapeAttr(filePath) + '">Delete</button>' +
          '</div>' +
        '</div>' +
      '</article>';
    }).join('');
    updateHistorySelection();
  }

  async function deleteHistoryItem(button) {
    const fileName = button.dataset.fileName || fileNameFromPath(button.dataset.path || '');
    if (!fileName) return;
    if (!window.confirm('Delete ' + fileName + '? This removes the saved output image from the harness history.')) return;

    button.disabled = true;
    button.textContent = 'Deleting...';
    try {
      const result = await service.DeleteOutputAsync({ RootDirectory: state.rootDirectory, FileName: fileName, FilePath: button.dataset.path || '' });
      const success = result.Success === true || result.success === true;
      if (!success) throw new Error(result.Error || result.error || 'Delete failed.');

      if (state.activeOutputFilePath && fileNameFromPath(state.activeOutputFilePath) === fileName) {
        setActiveImage({ url: '', file: null, name: '', outputFilePath: '', kind: '', bytes: 0 });
      }
      setMessage(el.editMessage, 'Deleted saved output: ' + fileName, 'ready');
      await refreshGallery();
      await loadLaunchFileFromHistory();
    } catch (error) {
      button.disabled = false;
      button.textContent = 'Delete';
      setMessage(el.editMessage, String(error), 'error');
    }
  }

  async function activateHistoryItem(item) {
    const url = item.dataset.historyUrl || '';
    const name = item.dataset.historyName || 'image.png';
    const path = item.dataset.historyPath || '';
    if (!url) return;
    const imageUrl = cacheBust(url);
    const file = await urlToFile(imageUrl, name);
    setActiveImage({
      url: imageUrl,
      file,
      name,
      outputFilePath: path,
      kind: 'Historical image',
      bytes: file.size,
      model: 'saved output'
    });
    setMessage(el.editMessage, 'Historical image loaded onto the canvas.', 'ready');
  }

  async function loadLaunchFileFromHistory() {
    const launchFileName = String(state.fileName || '').toLowerCase();
    if (!launchFileName) return;
    for (const item of el.galleryGrid.querySelectorAll('.history-item')) {
      const rowName = String(item.dataset.historyName || '').toLowerCase();
      const rowPath = String(item.dataset.historyPath || '').replace(/\\/g, '/').toLowerCase();
      if (rowName === launchFileName || rowPath.endsWith('/' + launchFileName)) {
        await activateHistoryItem(item);
        return;
      }
    }
    setMessage(el.editMessage, 'Requested launch image was not found in this directory: ' + state.fileName, 'error');
  }

  function updateHistorySelection() {
    const activePath = state.activeOutputFilePath || '';
    for (const item of el.galleryGrid.querySelectorAll('.history-item')) {
      item.classList.toggle('active', !!activePath && item.dataset.historyPath === activePath);
    }
  }

  function loadSourceFiles(files) {
    const file = Array.from(files || []).find(candidate => candidate && candidate.type && candidate.type.startsWith('image/'));
    if (!file) {
      setMessage(el.editMessage, 'Choose an image file first.', 'error');
      return;
    }

    const url = URL.createObjectURL(file);
    setActiveImage({ url, file, name: file.name, outputFilePath: '', kind: 'Loaded image', bytes: file.size });
    setMessage(el.editMessage, 'Image loaded onto the canvas.', 'ready');
  }

  async function urlToFile(url, name) {
    const response = await fetch(url, { cache: 'no-store' });
    if (!response.ok) throw new Error('Could not load image: ' + response.status);
    const blob = await response.blob();
    return new File([blob], name || 'image.png', { type: blob.type || 'image/png' });
  }

  function syncMaskCanvas({ restoreSubmittedMask = false } = {}) {
    const rect = el.activeImage.getBoundingClientRect();
    const stageRect = el.imageStage.getBoundingClientRect();
    el.maskCanvas.width = Math.max(1, Math.round(rect.width));
    el.maskCanvas.height = Math.max(1, Math.round(rect.height));
    el.maskCanvas.style.width = rect.width + 'px';
    el.maskCanvas.style.height = rect.height + 'px';
    el.maskCanvas.style.left = (rect.left - stageRect.left) + 'px';
    el.maskCanvas.style.top = (rect.top - stageRect.top) + 'px';
    clearMask();
    if (restoreSubmittedMask) restoreSubmittedMaskOverlay();
  }

  function saveMaskUndo() {
    const ctx = el.maskCanvas.getContext('2d');
    if (el.maskCanvas.width && el.maskCanvas.height) {
      state.maskUndo.push(ctx.getImageData(0, 0, el.maskCanvas.width, el.maskCanvas.height));
      if (state.maskUndo.length > 12) state.maskUndo.shift();
    }
  }

  function undoMask() {
    const imageData = state.maskUndo.pop();
    if (!imageData) return;
    const ctx = el.maskCanvas.getContext('2d');
    ctx.clearRect(0, 0, el.maskCanvas.width, el.maskCanvas.height);
    ctx.putImageData(imageData, 0, 0);
    state.maskHasPaint = hasCanvasPaint();
    updateMaskHint();
    scheduleMaskDiagnosticsPreview();
  }

  function clearMask() {
    const ctx = el.maskCanvas.getContext('2d');
    ctx.clearRect(0, 0, el.maskCanvas.width || 1, el.maskCanvas.height || 1);
    state.maskHasPaint = false;
    state.maskUndo = [];
    updateMaskHint();
  }

  function updateMaskHint(message) {
    if (!el.maskHint) return;
    if (typeof message === 'string' && message.trim()) {
      el.maskHint.textContent = message;
      el.maskHint.className = 'mask-hint warn';
      return;
    }
    if (state.maskHasPaint) {
      el.maskHint.textContent = 'Mask ready. The blue overlay is visible on the canvas and will be sent automatically when you edit.';
      el.maskHint.className = 'mask-hint ready';
      return;
    }
    el.maskHint.textContent = 'No mask painted. The edit prompt will apply to the whole active image unless you paint blue over the area to change.';
    el.maskHint.className = 'mask-hint';
  }

  function hasCanvasPaint() {
    const ctx = el.maskCanvas.getContext('2d');
    const data = ctx.getImageData(0, 0, el.maskCanvas.width || 1, el.maskCanvas.height || 1).data;
    for (let i = 3; i < data.length; i += 4) if (data[i] > 0) return true;
    return false;
  }

  function setMaskMode(mode) {
    state.maskMode = mode;
    el.paintMaskButton.classList.toggle('active', mode === 'paint');
    el.eraseMaskButton.classList.toggle('active', mode === 'erase');
  }

  function pointerPosition(event) {
    const rect = el.maskCanvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    if (x < 0 || y < 0 || x > rect.width || y > rect.height) return null;
    return {
      x: Math.max(0, Math.min(el.maskCanvas.width, x)),
      y: Math.max(0, Math.min(el.maskCanvas.height, y))
    };
  }

  function paintAt(event) {
    if (!state.drawing) return;
    const pos = pointerPosition(event);
    if (!pos) return;
    const ctx = el.maskCanvas.getContext('2d');
    ctx.globalCompositeOperation = state.maskMode === 'erase' ? 'destination-out' : 'source-over';
    ctx.fillStyle = 'rgba(59, 130, 246, 0.42)';
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, Number(el.brushSize.value), 0, Math.PI * 2);
    ctx.fill();
    state.maskHasPaint = hasCanvasPaint();
    updateMaskHint();
    scheduleMaskDiagnosticsPreview();
  }

  function scheduleMaskDiagnosticsPreview() {
    if (state.maskDiagnosticsTimer) window.clearTimeout(state.maskDiagnosticsTimer);
    state.maskDiagnosticsTimer = window.setTimeout(() => {
      updateMaskDiagnosticsPreview().catch(error => {
        if (el.maskDiagnostics) el.maskDiagnostics.textContent = 'Mask diagnostics failed: ' + String(error);
      });
    }, 120);
  }

  async function updateMaskDiagnosticsPreview() {
    if (!state.activeFile || !state.maskHasPaint) {
      renderMaskDiagnostics(null);
      clearDebugLinks();
      return;
    }
    const maskBlob = await createApiMaskBlob();
    const diagnostics = await analyzeMaskBlob(maskBlob);
    setDebugDownload(el.downloadDebugSourceLink, state.activeFile, 'debug-source.png');
    setDebugDownload(el.downloadDebugMaskLink, maskBlob, 'debug-edit-mask.png');
    renderMaskDiagnostics(diagnostics);
  }

  function clearSubmittedMaskSnapshot() {
    state.lastSubmittedMaskCanvas = null;
    state.lastSubmittedMaskDiagnostics = null;
    state.restoreSubmittedMaskAfterNextLoad = false;
  }

  function rememberSubmittedMaskDiagnostics(diagnostics) {
    const snapshot = document.createElement('canvas');
    snapshot.width = el.maskCanvas.width;
    snapshot.height = el.maskCanvas.height;
    snapshot.getContext('2d').drawImage(el.maskCanvas, 0, 0);
    state.lastSubmittedMaskCanvas = snapshot;
    state.lastSubmittedMaskDiagnostics = diagnostics;
    state.restoreSubmittedMaskAfterNextLoad = false;
  }

  function restoreSubmittedMaskOverlay() {
    if (!state.lastSubmittedMaskCanvas || !el.maskCanvas.width || !el.maskCanvas.height) return;
    const ctx = el.maskCanvas.getContext('2d');
    ctx.clearRect(0, 0, el.maskCanvas.width, el.maskCanvas.height);
    ctx.drawImage(state.lastSubmittedMaskCanvas, 0, 0, el.maskCanvas.width, el.maskCanvas.height);
    state.maskHasPaint = hasCanvasPaint();
    if (state.lastSubmittedMaskDiagnostics) {
      renderMaskDiagnostics({ ...state.lastSubmittedMaskDiagnostics, status: 'Last submitted mask' });
    }
    updateMaskHint();
  }

  function updateDebugViews(result, diagnostics) {
    const uploads = result && result.raw ? result.raw.debugUploads : null;
    const debugViewer = el.debugTabs ? el.debugTabs.closest('.debug-viewer') : null;
    if (debugViewer) debugViewer.classList.add('active');
    const sourceUpload = uploads && uploads.Images && uploads.Images.length ? uploads.Images[0] : null;
    const maskUpload = uploads ? uploads.Mask : null;
    state.debugViews = {
      source: sourceUpload && sourceUpload.Url ? sourceUpload.Url : state.activeImageUrl,
      mask: maskUpload && maskUpload.Url ? maskUpload.Url : '',
      sourceMask: sourceUpload && sourceUpload.Url ? sourceUpload.Url : state.activeImageUrl,
      final: result && result.imageUrl ? result.imageUrl : state.activeImageUrl
    };
    state.debugViewMode = 'final';
    renderDebugTabs(diagnostics);
    renderCanvasSizeDiagnostics(diagnostics);
  }

  function renderDebugTabs(diagnostics) {
    if (!el.debugTabs) return;
    const tabs = [
      ['source', 'Source'],
      ['mask', 'Mask preview'],
      ['sourceMask', 'Source + mask'],
      ['final', 'OpenAI result']
    ];
    el.debugTabs.innerHTML = tabs.map(([mode, label]) => {
      const available = !!state.debugViews[mode] || mode === 'sourceMask';
      return '<button type="button" class="secondary debug-tab' + (state.debugViewMode === mode ? ' active' : '') + '" data-debug-view="' + mode + '"' + (available ? '' : ' disabled') + '>' + escapeHtml(label) + '</button>';
    }).join('');
    showDebugView(state.debugViewMode, diagnostics);
  }

  function updateDebugTabSelection() {
    if (!el.debugTabs) return;
    for (const button of el.debugTabs.querySelectorAll('[data-debug-view]')) {
      button.classList.toggle('active', button.dataset.debugView === state.debugViewMode);
    }
  }
  function showDebugView(mode, diagnostics) {
    state.debugViewMode = mode;
    updateDebugTabSelection();
    if (mode === 'final') {
      setDebugImageSource(state.debugViews.final, false, diagnostics);
      return;
    }
    if (mode === 'source') {
      setDebugImageSource(state.debugViews.source, false, diagnostics);
      return;
    }
    if (mode === 'mask' && state.debugViews.mask) {
      renderMaskPreviewToImage(state.debugViews.mask, diagnostics).catch(error => setMessage(el.editMessage, String(error), 'error'));
      return;
    }
    if (mode === 'sourceMask') {
      setDebugImageSource(state.debugViews.sourceMask || state.debugViews.source, true, diagnostics);
    }
  }

  function setDebugImageSource(url, restoreOverlay, diagnostics) {
    if (!url) return;
    el.activeImage.addEventListener('load', () => {
      window.requestAnimationFrame(() => {
        syncMaskCanvas({ restoreSubmittedMask: restoreOverlay });
        renderCanvasSizeDiagnostics(diagnostics || state.lastSubmittedMaskDiagnostics);
      });
    }, { once: true });
    el.activeImage.src = url;
  }

  function renderMaskPreviewToImage(maskUrl, diagnostics) {
    return new Promise((resolve, reject) => {
      const image = new Image();
      image.onload = () => {
        try {
          const canvas = document.createElement('canvas');
          canvas.width = image.naturalWidth || 1024;
          canvas.height = image.naturalHeight || 1024;
          const maskCanvas = document.createElement('canvas');
          maskCanvas.width = canvas.width;
          maskCanvas.height = canvas.height;
          const maskCtx = maskCanvas.getContext('2d');
          maskCtx.drawImage(image, 0, 0, canvas.width, canvas.height);
          const maskPixels = maskCtx.getImageData(0, 0, canvas.width, canvas.height);
          const ctx = canvas.getContext('2d');
          const preview = ctx.createImageData(canvas.width, canvas.height);
          for (let i = 0; i < preview.data.length; i += 4) {
            const alpha = maskPixels.data[i + 3];
            if (alpha < 128) {
              preview.data[i] = 59;
              preview.data[i + 1] = 130;
              preview.data[i + 2] = 246;
              preview.data[i + 3] = 230;
            } else {
              preview.data[i] = 248;
              preview.data[i + 1] = 250;
              preview.data[i + 2] = 252;
              preview.data[i + 3] = 255;
            }
          }
          ctx.putImageData(preview, 0, 0);
          if (diagnostics && diagnostics.boundingBox) {
            ctx.strokeStyle = '#f59e0b';
            ctx.lineWidth = Math.max(3, Math.round(canvas.width / 256));
            ctx.strokeRect(diagnostics.boundingBox.x, diagnostics.boundingBox.y, diagnostics.boundingBox.width, diagnostics.boundingBox.height);
          }
          setDebugImageSource(canvas.toDataURL('image/png'), false, diagnostics);
          resolve();
        } catch (error) { reject(error); }
      };
      image.onerror = () => reject(new Error('Could not load mask preview image.'));
      image.src = maskUrl;
    });
  }

  function renderCanvasSizeDiagnostics(diagnostics) {
    if (!el.debugCanvasStatus) return;
    const imageRect = el.activeImage.getBoundingClientRect();
    const canvasRect = el.maskCanvas.getBoundingClientRect();
    const warnings = [];
    if (Math.abs(imageRect.width - canvasRect.width) > 1 || Math.abs(imageRect.height - canvasRect.height) > 1) warnings.push('display image/mask canvas size mismatch');
    if (diagnostics && diagnostics.boundingBox && (diagnostics.boundingBox.x === 0 || diagnostics.boundingBox.y === 0)) warnings.push('mask touches top/left edge');
    const natural = (el.activeImage.naturalWidth || 0) + 'x' + (el.activeImage.naturalHeight || 0);
    const display = Math.round(imageRect.width) + 'x' + Math.round(imageRect.height);
    const canvas = el.maskCanvas.width + 'x' + el.maskCanvas.height + ' backing / ' + Math.round(canvasRect.width) + 'x' + Math.round(canvasRect.height) + ' css';
    el.debugCanvasStatus.innerHTML = '<strong>Canvas/image sizing</strong> natural ' + escapeHtml(natural) + ' / displayed ' + escapeHtml(display) + ' / mask canvas ' + escapeHtml(canvas) + (warnings.length ? '<br><span class="warn-text">Warning: ' + escapeHtml(warnings.join('; ')) + '</span>' : '');
  }
  function renderMaskDiagnostics(diagnostics) {
    if (!el.maskDiagnostics) return;
    if (!diagnostics) {
      el.maskDiagnostics.innerHTML = 'No mask prepared yet.';
      return;
    }
    const bbox = diagnostics.boundingBox
      ? diagnostics.boundingBox.x + ',' + diagnostics.boundingBox.y + ' ' + diagnostics.boundingBox.width + 'x' + diagnostics.boundingBox.height
      : 'none';
    el.maskDiagnostics.innerHTML = '<strong>' + escapeHtml(diagnostics.status) + '</strong>' +
      'Source ' + escapeHtml(diagnostics.sourceWidth + 'x' + diagnostics.sourceHeight) + ' / mask ' + escapeHtml(diagnostics.maskWidth + 'x' + diagnostics.maskHeight) + '<br>' +
      'Editable transparent pixels: ' + escapeHtml(String(diagnostics.transparentPixels)) + ' (' + escapeHtml(diagnostics.transparentPercent) + '%)<br>' +
      'Opaque preserved pixels: ' + escapeHtml(String(diagnostics.opaquePixels)) + '<br>' +
      'Bounds: ' + escapeHtml(bbox);
  }

  function clearDebugLinks() {
    revokeDebugUrl('lastDebugSourceUrl');
    revokeDebugUrl('lastDebugMaskUrl');
    disableDebugLink(el.downloadDebugSourceLink);
    disableDebugLink(el.downloadDebugMaskLink);
  }

  function revokeDebugUrl(key) {
    if (state[key]) {
      URL.revokeObjectURL(state[key]);
      state[key] = '';
    }
  }

  function disableDebugLink(link) {
    if (!link) return;
    link.href = '#';
    link.classList.add('disabled');
  }

  function setDebugDownload(link, blob, fileName) {
    if (!link || !blob) return;
    const key = link === el.downloadDebugSourceLink ? 'lastDebugSourceUrl' : 'lastDebugMaskUrl';
    revokeDebugUrl(key);
    state[key] = URL.createObjectURL(blob);
    link.href = state[key];
    link.download = fileName;
    link.classList.remove('disabled');
  }

  function analyzeMaskBlob(maskBlob) {
    return new Promise((resolve, reject) => {
      const width = Math.max(1, el.activeImage.naturalWidth || el.maskCanvas.width || 1);
      const height = Math.max(1, el.activeImage.naturalHeight || el.maskCanvas.height || 1);
      const maskImage = new Image();
      const maskUrl = URL.createObjectURL(maskBlob);
      maskImage.onload = () => {
        try {
          const canvas = document.createElement('canvas');
          canvas.width = width;
          canvas.height = height;
          const ctx = canvas.getContext('2d');
          ctx.drawImage(maskImage, 0, 0, width, height);
          const data = ctx.getImageData(0, 0, width, height).data;
          let transparentPixels = 0;
          let opaquePixels = 0;
          let partialAlphaPixels = 0;
          let minX = width;
          let minY = height;
          let maxX = -1;
          let maxY = -1;
          for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
              const alpha = data[(y * width + x) * 4 + 3];
              if (alpha === 0) {
                transparentPixels++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
              } else {
                opaquePixels++;
                if (alpha < 255) partialAlphaPixels++;
              }
            }
          }
          const totalPixels = width * height;
          const boundingBox = transparentPixels > 0
            ? { x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1 }
            : null;
          const status = transparentPixels === 0
            ? 'Invalid: mask has no editable transparent pixels'
            : transparentPixels === totalPixels
              ? 'Invalid: whole image is editable'
              : partialAlphaPixels > 0
                ? 'Warning: mask has partial alpha outside the edit area'
                : 'Mask ready';
          URL.revokeObjectURL(maskUrl);
          resolve({
            status,
            sourceWidth: width,
            sourceHeight: height,
            maskWidth: maskImage.naturalWidth || width,
            maskHeight: maskImage.naturalHeight || height,
            transparentPixels,
            opaquePixels,
            partialAlphaPixels,
            transparentPercent: ((transparentPixels / totalPixels) * 100).toFixed(2),
            dimensionsMatch: (maskImage.naturalWidth || width) === width && (maskImage.naturalHeight || height) === height,
            boundingBox
          });
        } catch (error) {
          URL.revokeObjectURL(maskUrl);
          reject(error);
        }
      };
      maskImage.onerror = () => {
        URL.revokeObjectURL(maskUrl);
        reject(new Error('Could not load generated mask for diagnostics.'));
      };
      maskImage.src = maskUrl;
    });
  }
  function createApiMaskBlob() {
    return new Promise((resolve, reject) => {
      const width = Math.max(1, el.activeImage.naturalWidth || el.maskCanvas.width || 1);
      const height = Math.max(1, el.activeImage.naturalHeight || el.maskCanvas.height || 1);
      const scaledMaskCanvas = document.createElement('canvas');
      scaledMaskCanvas.width = width;
      scaledMaskCanvas.height = height;
      const scaledMaskCtx = scaledMaskCanvas.getContext('2d');
      scaledMaskCtx.drawImage(el.maskCanvas, 0, 0, width, height);
      const paintedPixels = scaledMaskCtx.getImageData(0, 0, width, height);
      const apiCanvas = document.createElement('canvas');
      apiCanvas.width = width;
      apiCanvas.height = height;
      const apiCtx = apiCanvas.getContext('2d');
      const apiPixels = apiCtx.createImageData(width, height);
      for (let source = 0; source < paintedPixels.data.length; source += 4) {
        apiPixels.data[source] = 255;
        apiPixels.data[source + 1] = 255;
        apiPixels.data[source + 2] = 255;
        apiPixels.data[source + 3] = paintedPixels.data[source + 3] > 0 ? 0 : 255;
      }
      apiCtx.putImageData(apiPixels, 0, 0);
      apiCanvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('Could not create edit mask.')), 'image/png');
    });
  }
  function fileToPayload(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onerror = () => reject(new Error('Could not read ' + (file.name || 'image') + '.'));
      reader.onload = () => resolve({
        FileName: file.name || 'image.png',
        ContentType: file.type || 'image/png',
        Base64Data: String(reader.result || '')
      });
      reader.readAsDataURL(file);
    });
  }

  function updateModelOptions() {
    const isImage2 = el.model.value === 'gpt-image-2';
    for (const option of el.size.options) {
      const isLarge = option.value === '2048x2048' || option.value === '2048x1152' || option.value === '3840x2160';
      option.disabled = isLarge && !isImage2;
    }
    if (!isImage2 && el.size.selectedOptions[0] && el.size.selectedOptions[0].disabled) el.size.value = '1024x1024';
  }

  function cacheBust(url, key) {
    if (!url) return '';
    return url + (url.includes('?') ? '&' : '?') + (key || 'v') + '=' + Date.now();
  }

  function formatBytes(bytes) {
    const value = Number(bytes || 0);
    if (!value) return '';
    if (value < 1024 * 1024) return Math.round(value / 1024) + ' KB';
    return (value / 1024 / 1024).toFixed(2) + ' MB';
  }

  function fileNameFromPath(path) {
    return String(path || '').split(/[\\/]/).pop();
  }

  function escapeHtml(value) {
    return String(value || '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
  }

  function escapeAttr(value) { return escapeHtml(value); }

  async function init() {
    updateModelOptions();
    setMaskMode('paint');
    updateMaskHint();
    try {
      if (!state.rootDirectory) {
        const response = await fetch('/api/image-harness/default-root', { headers: { 'kcs-IsAjax': 'true' } });
        if (response.ok) {
          const defaultRoot = await response.json();
          state.rootDirectory = defaultRoot.rootDirectory || defaultRoot.RootDirectory || '';
        }
      }
      const config = await service.GetConfigAsync({ RootDirectory: state.rootDirectory, FileName: state.fileName });
      const hasApiKey = config.HasApiKey === true || config.hasApiKey === true;
      setStatus(hasApiKey ? 'API key loaded' : 'Missing API key', hasApiKey ? 'ready' : 'error');
      await refreshGallery();
      await loadLaunchFileFromHistory();
    } catch (error) {
      setStatus('Config failed', 'error');
      el.lastResult.textContent = String(error);
    }
  }

  if (el.debugTabs) {
    el.debugTabs.addEventListener('click', event => {
      const button = event.target.closest('[data-debug-view]');
      if (!button || button.disabled) return;
      showDebugView(button.dataset.debugView, state.lastSubmittedMaskDiagnostics);
    });
  }

  el.generateButton.addEventListener('click', generateImage);
  el.editButton.addEventListener('click', editImage);
  el.model.addEventListener('change', updateModelOptions);
  el.sourceImages.addEventListener('change', event => loadSourceFiles(event.target.files));
  el.clearMaskButton.addEventListener('click', clearMask);
  el.undoMaskButton.addEventListener('click', undoMask);
  el.paintMaskButton.addEventListener('click', () => setMaskMode('paint'));
  el.eraseMaskButton.addEventListener('click', () => setMaskMode('erase'));
  el.copyPathButton.addEventListener('click', () => navigator.clipboard.writeText(state.activeOutputFilePath || ''));
  el.copyResultButton.addEventListener('click', () => navigator.clipboard.writeText(el.lastResult.textContent || ''));
  el.refreshGalleryButton.addEventListener('click', () => refreshGallery().catch(error => el.lastResult.textContent = String(error)));
  el.galleryGrid.addEventListener('click', event => {
    const copyButton = event.target.closest('[data-gallery-action="copy"]');
    if (copyButton) {
      event.stopPropagation();
      navigator.clipboard.writeText(copyButton.dataset.path || '');
      return;
    }
    const deleteButton = event.target.closest('[data-gallery-action="delete"]');
    if (deleteButton) {
      event.stopPropagation();
      deleteHistoryItem(deleteButton).catch(error => setMessage(el.editMessage, String(error), 'error'));
      return;
    }
    if (event.target.closest('[data-ignore-history]')) return;
    const item = event.target.closest('.history-item');
    if (item) activateHistoryItem(item).catch(error => el.lastResult.textContent = String(error));
  });

  ['dragenter', 'dragover'].forEach(name => el.dropZone.addEventListener(name, event => {
    event.preventDefault();
    el.dropZone.classList.add('dragging');
  }));
  ['dragleave', 'drop'].forEach(name => el.dropZone.addEventListener(name, event => {
    event.preventDefault();
    el.dropZone.classList.remove('dragging');
  }));
  el.dropZone.addEventListener('drop', event => loadSourceFiles(event.dataTransfer.files));

  el.activeImage.addEventListener('load', () => {
    el.imageStage.classList.add('has-image');
    window.requestAnimationFrame(() => {
      syncMaskCanvas();
      scheduleMaskDiagnosticsPreview();
    });
  });
  window.addEventListener('resize', () => {
    if (el.imageStage.classList.contains('has-image')) window.requestAnimationFrame(() => { syncMaskCanvas(); scheduleMaskDiagnosticsPreview(); });
  });

  el.maskCanvas.addEventListener('pointerdown', event => {
    state.drawing = true;
    saveMaskUndo();
    el.maskCanvas.setPointerCapture(event.pointerId);
    paintAt(event);
  });
  el.maskCanvas.addEventListener('pointermove', paintAt);
  el.maskCanvas.addEventListener('pointerup', event => {
    state.drawing = false;
    el.maskCanvas.releasePointerCapture(event.pointerId);
  });
  el.maskCanvas.addEventListener('pointercancel', () => { state.drawing = false; });

  init();
})();



