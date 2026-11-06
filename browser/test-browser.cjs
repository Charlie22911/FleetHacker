const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {createHash} = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const {pathToFileURL} = require('node:url');
const videoFixture = process.argv[3] && fs.readFileSync(process.argv[3]).toString('base64');
const html = path.resolve(__dirname, '../dist/Media Compliance Reviewer WebAssembly.html');
const scratch = path.join(__dirname, '.build');
fs.mkdirSync(scratch, {recursive: true});
const profile = fs.mkdtempSync(path.join(scratch, 'chromium-'));
const fixture = process.argv[2] ? path.resolve(process.argv[2]) : createFixture();

function createFixture() {
  const {DatabaseSync} = require('node:sqlite');
  const filename = path.join(profile, 'generated-backup.sqlite');
  const db = new DatabaseSync(filename);
  try {
    db.exec(`PRAGMA application_id=1296257623; PRAGMA user_version=13;
      CREATE TABLE review_groups(id INTEGER PRIMARY KEY,content_id,display_sequence,reviewer,notes,status,retired);
      CREATE TABLE contents(id INTEGER PRIMARY KEY,size);
      CREATE TABLE group_occurrences(group_id,id,root_id,name,extension,path,superseded,modified_ticks,occurrence_order);
      CREATE TABLE source_roots(id INTEGER PRIMARY KEY,path);
      CREATE TABLE previews(content_id INTEGER PRIMARY KEY,state,mime,bytes);
      WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<24)
        INSERT INTO review_groups SELECT id,id,id,'','',CASE id WHEN 23 THEN 'COMPLIANT'
          WHEN 24 THEN 'EVIDENCE' ELSE 'TO_REVIEW' END,0 FROM seq;
      INSERT INTO contents SELECT id,80 FROM review_groups;
      INSERT INTO source_roots VALUES(1,'Generated source');
      INSERT INTO group_occurrences VALUES(23,1,1,'first.png','png','user/first.png',0,1,1),
        (23,2,1,'rescan-copy.png','png','user/rescan-copy.png',0,1,2);`);
    const preview = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGMw7tgNAAJoAXcmSeNVAAAAAElFTkSuQmCC', 'base64');
    db.prepare("INSERT INTO previews VALUES(23,'available','image/png',?)").run(preview);
  } finally { db.close(); }
  return filename;
}
const sha256 = () => createHash('sha256').update(fs.readFileSync(fixture)).digest('hex');
const originalHash = sha256();
const browser = spawn(process.env.MEDIA_REVIEWER_TEST_BROWSER || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], {windowsHide: true, stdio: 'ignore'});
const wait = async (predicate, description) => {
  for (let i = 0; i < 200; i++) {
    const result = await predicate();
    if (result) return result;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error('Timed out waiting for ' + description);
};

let ws;
async function run() {
  const portFile = path.join(profile, 'DevToolsActivePort');
  await wait(() => fs.existsSync(portFile), 'Chromium');
  const [port, endpoint] = fs.readFileSync(portFile, 'utf8').trim().split(/\r?\n/);
  ws = new WebSocket('ws://127.0.0.1:' + port + endpoint);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  const pending = new Map();
  const errors = [];
  const remote = [];
  let sequence = 0;
  ws.onmessage = ({data}) => {
    const message = JSON.parse(data);
    if (message.id) {
      const {resolve, reject} = pending.get(message.id);
      pending.delete(message.id);
      if (message.error) reject(new Error(message.error.message));
      else resolve(message.result);
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails);
    if (message.method === 'Network.requestWillBeSent' && /^https?:/i.test(message.params.request.url)) {
      remote.push(message.params.request.url);
    }
  };
  let sessionId;
  const call = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    pending.set(id, {resolve, reject});
    ws.send(JSON.stringify({id, method, params, sessionId}));
  });
  const {targetId} = await call('Target.createTarget', {url: 'about:blank'});
  ({sessionId} = await call('Target.attachToTarget', {targetId, flatten: true}));
  const js = async (expression, userGesture = false) => {
    const response = await call('Runtime.evaluate', {expression, userGesture, awaitPromise: true, returnByValue: true});
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description || response.exceptionDetails.text);
    return response.result.value;
  };
  await call('Runtime.enable');
  await call('Page.enable');
  await call('Network.enable');
  await call('Network.emulateNetworkConditions', {offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0});
  await call('Page.navigate', {url: pathToFileURL(html).href});
  await wait(() => js("Boolean(document.getElementById('workspace-dialog')?.open)"), 'offline startup');
  await js("document.getElementById('windows-backup-startup').click()");
  const {root} = await call('DOM.getDocument');
  const {nodeId} = await call('DOM.querySelector', {nodeId: root.nodeId, selector: '#windows-backup-file'});
  await call('DOM.setFileInputFiles', {nodeId, files: [fixture]});
  try {
    await wait(() => js("document.getElementById('windows-backup-status').textContent.startsWith('Read-only')"), 'read-only backup');
  } catch (error) {
    throw new Error(error.message + ' (' + await js("document.getElementById('windows-backup-status').textContent") + ')');
  }
  assert.equal(await js("document.querySelectorAll('#windows-backup-cards > li').length"), 20);
  await js("document.getElementById('windows-backup-next').click()");
  await wait(() => js("document.getElementById('windows-backup-page').textContent==='21–22 of 22'"), 'second results page');
  await js(`(() => { const select=document.getElementById('windows-backup-bucket'); select.value='COMPLIANT'; select.dispatchEvent(new Event('change')); })()`);
  await wait(() => js("Boolean(document.querySelector('#windows-backup-cards img')?.naturalWidth)"), 'saved thumbnail');
  await js("document.querySelector('#windows-backup-cards details').open=true");
  await wait(() => js("document.querySelector('#windows-backup-cards details').textContent.includes('rescan-copy.png')"), 'recorded duplicate locations');
  assert.equal(await js(`(async () => {
    const file = document.getElementById('windows-backup-file').files[0];
    const db = await MediaDatabase.Database.open({windowsBackup:file});
    try {
      await db.exec("UPDATE review_groups SET notes='should not be written'");
      return false;
    } catch (error) { return /readonly|read.only/i.test(error.message); }
    finally { await db.close(); }
  })()`), true, 'SQLite must reject writes');
  assert.equal(sha256(), originalHash, 'Viewing a backup must leave it unchanged');
  await js("document.getElementById('windows-catalog-dialog').close(); document.getElementById('workspace-dialog').close(); document.getElementById('inspect-dialog').showModal()");
  await js("document.getElementById('inspect-fullscreen').click()", true);
  await wait(() => js("document.getElementById('inspect-dialog').classList.contains('browser-fullscreen') && !!document.fullscreenElement"), 'Inspector fullscreen');
  await js("document.getElementById('inspect-fullscreen').click()", true);
  await wait(() => js("!document.fullscreenElement"), 'exit fullscreen');
  if (videoFixture) {
    await js(`(() => {
      const video=document.getElementById('inspect-video');
      video.src=URL.createObjectURL(new Blob([Uint8Array.from(atob('${videoFixture}'),c=>c.charCodeAt(0))],{type:'video/mp4'}));
      video.classList.remove('hidden');
    })()`);
    await wait(() => js("document.getElementById('inspect-video').readyState >= 1"), 'browser-native MP4');
    await js(`(() => {
      const rate=document.getElementById('inspect-video-rate');rate.value='1.5';rate.dispatchEvent(new Event('change'));
    })()`);
    assert.equal(await js("document.getElementById('inspect-video').playbackRate"), 1.5);
    await js(`(() => {
      document.getElementById('inspect-dialog').close();
      Object.defineProperty(window, 'showSaveFilePicker', {value:undefined, configurable:true});
      document.getElementById('workspace-dialog').showModal();
      document.getElementById('startup-new').click();
    })()`);
    await wait(() => js("!document.getElementById('workspace-dialog').open && !document.getElementById('scan-button').disabled"), 'browser catalog');
    await js(`(() => {
      const input = document.getElementById('folder-input');
      const file = new File([Uint8Array.from(atob('${videoFixture}'),c=>c.charCodeAt(0))], 'test-video.mp4', {type:'video/mp4'});
      Object.defineProperty(file, 'webkitRelativePath', {value:'Sample media/Sample user/test-video.mp4'});
      const selection = new DataTransfer();
      selection.items.add(file);
      input.files = selection.files;
      input.dispatchEvent(new Event('change', {bubbles:true}));
    })()`);
    await wait(() => js("document.querySelector('#results .fh-list-row')?.textContent.includes('test-video.mp4')"), 'scanned video row');
    await js(`document.getElementById('scan-dialog').close(); document.querySelector('[data-fh-view=quick]').click()`);
    await wait(() => js("!document.getElementById('fh-quick-play').hidden"), 'Quick Review video action');
    await js("document.getElementById('fh-quick-play').click()");
    await wait(() => js("!document.getElementById('fh-quick-video').hidden && document.getElementById('fh-quick-video').videoWidth > 0"), 'Quick Review first decoded frame');
    await js(`(() => {
      const rate = document.getElementById('fh-quick-rate');
      rate.value = '1.25';
      rate.dispatchEvent(new Event('change'));
    })()`);
    assert.equal(await js("document.getElementById('fh-quick-video').playbackRate"), 1.25);
    await js("document.querySelector('[data-fh-view=list]').click()");
    assert.equal(await js("document.getElementById('fh-quick-video').hasAttribute('src')"), false);
  }
  assert.deepEqual(remote, []);
  assert.deepEqual(errors, []);
  console.log('Offline Chromium passed: Windows catalog pages, previews, locations and write protection, and Inspector fullscreen' + (videoFixture ? ', plus Inspect and Quick Review playback.' : '.'));
}
run().catch(error => { console.error(error); process.exitCode = 1; }).finally(() => { ws?.close(); browser.kill(); });
