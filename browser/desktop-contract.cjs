'use strict';

const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const snapshotPath = path.join(__dirname, 'desktop-contract.json');
const adapterSchema = 13;
function desktopContract(root) {
  const read = filename => fs.readFileSync(path.join(root, filename), 'utf8');
  const match = (text, pattern, label) => {
    const value = pattern.exec(text);
    if (!value) throw new Error('Could not read desktop ' + label);
    return value[1];
  };
  const product = read('Core/Product.cs');
  const catalog = read('Core/Database.cs');
  const layout = read('App/ReviewWorkspace.cs');
  const draftReview = read('Core/DraftReview.cs');
  const modelWorker = read('Core/DraftModelWorker.cs');
  const criteria = read('Core/GemmaCriteria.cs');
  const decoder = read('Core/MediaDecode.cs');
  const string = (source, name) => match(source, new RegExp('\\b' + name + '="([^"]+)"'), name);
  const integer = (source, name) => Number(match(source, new RegExp('\\b' + name + '=(\\d+)'), name));
  const defaultCriteria = match(criteria, /string Default=([\s\S]+?);\r?\n/, 'Gemma criteria');
  const criteriaLines = [...defaultCriteria.matchAll(/"(?:\\.|[^"\\])*"/g)].map(([literal]) => JSON.parse(literal));
  const motionSamples = /int\[\] positions=type=="gif" \? new\[\]\{([\d,]+)\} : new\[\]\{([\d,]+)\}/.exec(decoder);
  if (!criteriaLines.length || !motionSamples) throw new Error('Could not read desktop draft review rules.');
  const themes = {};
  for (const [, id, label, dark, colors] of layout.matchAll(
    /Id="([^"]+)",Label="([^"]+)",IsDark=(true|false),\s*Colors=new\[\]\{([^}]+)\}/g)) {
    const palette = [...colors.matchAll(/"(#[0-9A-F]{6})"/gi)].map(color => color[1]);
    if (palette.length !== 19) throw new Error('Unexpected desktop palette size for ' + id);
    themes[id] = {label, dark:dark === 'true', colors:palette};
  }
  if (!themes['warm-light']) throw new Error('Desktop warm-light theme is missing.');
  return {
    version:match(product, /DotNetVersion="([^"]+)"/, 'version'),
    applicationId:Number(match(catalog, /ApplicationId=(0x[0-9a-f]+);/i, 'catalog application ID')),
    schema:Number(match(catalog, /SchemaVersion=(\d+);/, 'catalog schema')),
    themes,
    draft: {
      bucket:string(draftReview, 'Bucket'),
      marqo:{
        id:string(draftReview, 'Model'),
        threshold:integer(draftReview, 'DefaultMarqoThreshold'),
        file:{name:string(modelWorker, 'FileName'), sha256:string(modelWorker, 'Sha256')},
      },
      gemma:{
        id:string(draftReview, 'Gemma'),
        threshold:integer(draftReview, 'DefaultGemmaThreshold'),
        weights:{name:string(modelWorker, 'GemmaFileName'), sha256:string(modelWorker, 'GemmaSha')},
        projector:{name:string(modelWorker, 'ProjectorFileName'), sha256:string(modelWorker, 'ProjectorSha')},
        criteria:criteriaLines.join(''),
        criteriaLimit:integer(criteria, 'Limit'),
      },
      motion:{gif:motionSamples[1].split(',').map(Number), video:motionSamples[2].split(',').map(Number)},
    },
  };
}
function loadDesktopContract() {
  const saved = JSON.parse(fs.readFileSync(snapshotPath, 'utf8'));
  const root = path.dirname(__dirname);
  if (fs.existsSync(path.join(root, 'Core/Product.cs'))) {
    assert.deepEqual(saved, desktopContract(root),
      'Desktop contract changed. Run npm run sync:desktop in the browser source folder and verify both builds.');
  }
  if (saved.schema !== adapterSchema) throw new Error(
    'Update and test the Windows backup adapter for schema ' + saved.schema + ' before building.');
  return saved;
}
if (require.main === module) {
  const contract = desktopContract(path.dirname(__dirname));
  if (contract.schema !== adapterSchema) throw new Error('Update the browser backup queries before accepting a new schema.');
  fs.writeFileSync(snapshotPath, JSON.stringify(contract, null, 2) + '\n');
}
module.exports = {loadDesktopContract};
