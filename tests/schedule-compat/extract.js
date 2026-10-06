// Pulls the Survey Schedule's own merge code -- emptyFeed() and assembleState() -- out of its HTML,
// character for character, so it can be run outside the browser.
//   node extract.js <Survey Schedule.html>            prints the functions and their SHA-256
//   node extract.js <Survey Schedule.html> --write    (re)writes assembleState.reference.js
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

function extractFunction(src, name) {
  const start = src.indexOf('function ' + name + '(');
  if (start < 0) throw new Error('function ' + name + ' not found');
  let i = src.indexOf('{', start), depth = 0, quote = null;
  for (; i < src.length; i++) {
    const c = src[i];
    if (quote) {
      if (c === '\\') { i++; continue; }
      if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '/' && src[i + 1] === '/') { i = src.indexOf('\n', i); continue; }
    if (c === '{') depth++;
    else if (c === '}' && --depth === 0) return src.slice(start, i + 1);
  }
  throw new Error('function ' + name + ' is not closed');
}

function extract(html) {
  const code = extractFunction(html, 'emptyFeed') + '\n' + extractFunction(html, 'assembleState') + '\n';
  const version = (html.match(/\bv(\d{2,3})\b/) || [])[0] || 'unknown';
  return { code, version, sha256: crypto.createHash('sha256').update(code).digest('hex') };
}

module.exports = { extract };

if (require.main === module) {
  const html = fs.readFileSync(process.argv[2], 'utf8');
  const e = extract(html);
  if (process.argv.includes('--write')) {
    const out = path.join(__dirname, 'assembleState.reference.js');
    fs.writeFileSync(out,
      '// Extracted verbatim from the Survey Schedule (' + path.basename(process.argv[2]) + ', ' + e.version + ') by extract.js.\n' +
      '// sha256 ' + e.sha256 + '\n' +
      '// Do not edit by hand: re-extract when the Schedule changes. Crew Upload\'s ScheduleAssembler.cs must give\n' +
      '// the same result as this code; the compatibility test checks that.\n' + e.code);
    console.log('wrote ' + out + '  sha256 ' + e.sha256);
  } else {
    process.stdout.write(e.code);
    console.error('sha256 ' + e.sha256 + '  (' + e.version + ')');
  }
}
