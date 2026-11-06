'use strict';

const limit = 1024;
const embedded = /(<(script|style)\b([^>]*)>)([\s\S]*?)(<\/\2\s*>)/gi;
const carrierType = 'application/x-fleethacker-base64';
const originalType = 'data-fleethacker-script-type';

function longestLine(text) {
  let maximum = 0;
  for (const line of text.split(/\r?\n/)) maximum = Math.max(maximum, line.length);
  return maximum;
}

function wrapBase64(text) {
  return '\n' + text.match(new RegExp('.{1,' + limit + '}', 'g')).join('\n') + '\n';
}

const restore = [
  '<script>',
  '{',
  '  const loader = document.currentScript;',
  '  const packed = loader.previousElementSibling;',
  '  const script = document.createElement("script");',
  '  for (const {name, value} of packed.attributes) {',
  '    if (name === "type") continue;',
  '    script.setAttribute(name === "' + originalType + '" ? "type" : name, value);',
  '  }',
  '  const binary = atob(packed.textContent);',
  '  const bytes = new Uint8Array(binary.length);',
  '  for (let i = 0; i < bytes.length; i++) bytes[i] = binary.charCodeAt(i);',
  '  script.textContent = new TextDecoder().decode(bytes);',
  '  packed.replaceWith(script);',
  '  loader.remove();',
  '}',
  '</script>',
].join('\n');

function packEmbedded(html) {
  const packed = html.replace(embedded, (block, opening, tag, attrs, source, closing) => {
    if (longestLine(source) <= limit) return block;
    if (tag.toLowerCase() !== 'script') {
      throw new Error('Embedded styles exceed ' + limit + ' characters per line.');
    }
    if (/\bid="video-decoder-wasm"/.test(attrs) && /^[A-Za-z0-9+/=\s]+$/.test(source)) {
      // The decoder uses atob, which ignores base64 whitespace.
      return opening + wrapBase64(source.replace(/\s/g, '')) + closing;
    }
    if (new RegExp('\\b(?:src|' + originalType + ')\\s*=', 'i').test(attrs)) {
      throw new Error('Cannot pack a script with external source or reserved attributes.');
    }
    const attributes = attrs.replace(/(\s)type\s*=/i, '$1' + originalType + '=');
    return '<script type="' + carrierType + '"' + attributes + '>' +
      wrapBase64(Buffer.from(source, 'utf8').toString('base64')) + closing + '\n' + restore;
  });
  checkEmbedded(packed);
  return packed;
}

function checkEmbedded(html) {
  let maximum = 0;
  for (const match of html.matchAll(embedded)) {
    maximum = Math.max(maximum, longestLine(match[0]));
  }
  if (maximum > limit) {
    throw new Error('Embedded HTML asset has a ' + maximum + '-character line. Maximum is ' + limit + '.');
  }
  return maximum;
}

module.exports = {checkEmbedded, packEmbedded};
