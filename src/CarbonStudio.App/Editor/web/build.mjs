// Bundles the Monaco host into ../../Assets/editor (git-ignored). Run by CI and by developers: `npm ci && npm run build`.
import { build } from 'esbuild';
import { copyFile, cp, mkdir, readFile, rm, readdir, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const outdir = path.resolve(here, '../../Assets/editor');
const monaco = path.join(here, 'node_modules/monaco-editor/esm/vs');

await rm(outdir, { recursive: true, force: true });
await mkdir(path.join(outdir, 'workers'), { recursive: true });

const common = { bundle: true, minify: true, target: 'es2022', logLevel: 'warning', legalComments: 'none' };

// Editor host: ES modules with code splitting, so each language grammar is a lazily loaded chunk.
await build({
  ...common,
  entryPoints: { main: path.join(here, 'src/main.js') },
  outdir,
  format: 'esm',
  splitting: true,
  loader: { '.ttf': 'file' },
});

// Web workers are classic scripts (no module-worker support needed) with fixed names that env.js refers to.
await build({
  ...common,
  entryPoints: {
    'editor.worker': path.join(monaco, 'editor/editor.worker.js'),
    'json.worker': path.join(monaco, 'language/json/json.worker.js'),
    'css.worker': path.join(monaco, 'language/css/css.worker.js'),
    'html.worker': path.join(monaco, 'language/html/html.worker.js'),
    'ts.worker': path.join(monaco, 'language/typescript/ts.worker.js'),
  },
  outdir: path.join(outdir, 'workers'),
  format: 'iife',
});

await copyFile(path.join(here, 'index.html'), path.join(outdir, 'index.html'));
await copyFile(path.join(here, 'node_modules/monaco-editor/LICENSE'), path.join(outdir, 'LICENSE-monaco-editor.txt'));

const pkg = JSON.parse(await readFile(path.join(here, 'node_modules/monaco-editor/package.json'), 'utf8'));
let bytes = 0;
let files = 0;
async function walk(dir) {
  for (const name of await readdir(dir)) {
    const full = path.join(dir, name);
    const info = await stat(full);
    if (info.isDirectory()) await walk(full);
    else { files += 1; bytes += info.size; }
  }
}
await walk(outdir);
console.log(`Built editor host with monaco-editor ${pkg.version}: ${files} files, ${(bytes / 1048576).toFixed(1)} MB -> ${outdir}`);
