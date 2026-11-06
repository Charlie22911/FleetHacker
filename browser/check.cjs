const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {checkEmbedded, packEmbedded} = require('./embedded-scripts.cjs');
const html = readFileSync(join(__dirname, '../dist/Media Compliance Reviewer WebAssembly.html'), 'utf8');
assert.match(html, /id="windows-catalog-dialog"/);
assert.match(html, /id="inspect-fullscreen"/);
assert.match(html, /id="inspect-video-rate"/);
assert.match(html, /id="database-worker-source"/);
assert.match(html, /id="video-decoder-wasm"/);
assert.match(html, /id="fh-desktop-template"/);
assert.match(html, /id="fh-preview-size"/);
assert.match(html, /id="fh-quick-video"/);
const desktop = require('./desktop-contract.cjs').loadDesktopContract();
assert.deepEqual(desktop.draft.gemma.criteria.match(/^[A-E](?=:)/gm), ['A','B','C','D','E']);
for (const percent of [desktop.draft.marqo.threshold, desktop.draft.gemma.threshold])
  assert.ok(percent >= 1 && percent <= 99);
for (const file of [desktop.draft.marqo.file, desktop.draft.gemma.weights, desktop.draft.gemma.projector])
  assert.match(file.sha256, /^[a-f0-9]{64}$/);
const maximum = checkEmbedded(html);
const script = 'const exact = "' + '£'.repeat(2048) + '";';
const prefix = '<p>' + 'Ordinary HTML '.repeat(100) + '</p>';
const probe = packEmbedded(prefix + '<script id="sample">' + script + '</script>');
assert.ok(probe.startsWith(prefix));
const encoded = probe.match(/<script type="application\/x-fleethacker-base64" id="sample">([\s\S]*?)<\/script>/);
assert.equal(Buffer.from(encoded[1], 'base64').toString('utf8'), script);
console.log('Longest embedded asset line: ' + maximum + ' characters.');
console.log('Offline package contains the desktop workspace, Inspector controls, backup viewer, and embedded engines.');
