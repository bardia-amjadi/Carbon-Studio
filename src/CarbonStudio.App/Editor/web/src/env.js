// Must run before Monaco creates its first worker. Workers are plain classic scripts built by build.mjs
// into ./workers and served from the same virtual host, so no blob: or data: URLs (and no remote hosts) are needed.
const workerFiles = {
  json: 'json.worker.js',
  css: 'css.worker.js',
  scss: 'css.worker.js',
  less: 'css.worker.js',
  html: 'html.worker.js',
  handlebars: 'html.worker.js',
  razor: 'html.worker.js',
  typescript: 'ts.worker.js',
  javascript: 'ts.worker.js',
};

self.MonacoEnvironment = {
  // Returning a Worker directly (instead of a URL) keeps Monaco from wrapping it in a blob: URL.
  getWorker(_moduleId, label) {
    return new Worker('./workers/' + (workerFiles[label] || 'editor.worker.js'));
  },
};
