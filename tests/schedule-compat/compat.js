// Compatibility check: does Crew Upload's schedule reader (CrewUpload.Schedule.dll) assemble a
// schedule folder exactly as the Survey Schedule's own assembleState() does?
//
//   node compat.js [--html "Survey Schedule.html" | --js assembleState.reference.js] --folder <dir> --csharp <assembled.json> [--folder ... --csharp ...]
//
// <assembled.json> comes from:  ScheduleCheck <dir> --assembled <assembled.json>
// With --html the Schedule's current code is extracted from its HTML, so a change to assembleState()
// in the Schedule is tested, not just the copy kept here. Exit code 0 = identical for every folder.
'use strict';
const fs = require('fs');
const path = require('path');
const { assembleFolder } = require('./harness');
const { extract } = require('./extract');

// Differences that are only how each side writes JSON: C# leaves out nulls; a missing withIds is [].
function norm(v) {
  if (Array.isArray(v)) return v.map(norm);
  if (v && typeof v === 'object') {
    const o = {};
    for (const k of Object.keys(v).sort()) if (v[k] !== null && v[k] !== undefined) o[k] = norm(v[k]);
    return o;
  }
  return v;
}
function normRecords(list, isAssignment) {
  return (list || []).map(r => {
    const n = norm(r);
    if (isAssignment && !n.withIds) n.withIds = [];
    return n;
  }).map(r => JSON.stringify(r)).sort();
}
function diff(label, a, b) {
  const sa = new Set(a), sb = new Set(b), out = [];
  for (const x of a) if (!sb.has(x)) out.push('  only in Schedule ' + label + ': ' + x);
  for (const x of b) if (!sa.has(x)) out.push('  only in Crew Upload ' + label + ': ' + x);
  return out;
}
function compare(js, cs) {
  if (js === null || cs === null) return js === cs ? [] : ['  one side could not read the folder (Schedule: ' + (js ? 'read' : 'none') + ', Crew Upload: ' + (cs ? 'read' : 'none') + ')'];
  return diff('project', normRecords(js.projects, false), normRecords(cs.projects, false))
    .concat(diff('entry', normRecords(js.assignments, true), normRecords(cs.assignments, true)));
}

module.exports = { compare };

if (require.main === module) {
  const args = process.argv.slice(2);
  const get = n => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : null; };
  let code, source;
  if (get('--html')) { const e = extract(fs.readFileSync(get('--html'), 'utf8')); code = e.code; source = get('--html') + ' (' + e.version + ', sha256 ' + e.sha256.slice(0, 12) + ')'; }
  else { source = get('--js') || path.join(__dirname, 'assembleState.reference.js'); code = fs.readFileSync(source, 'utf8'); }
  const folders = [], csharp = [];
  args.forEach((a, i) => { if (a === '--folder') folders.push(args[i + 1]); if (a === '--csharp') csharp.push(args[i + 1]); });
  if (!folders.length || folders.length !== csharp.length) { console.error('give --folder and --csharp in pairs'); process.exit(1); }
  console.log('Schedule code: ' + source);
  let bad = 0;
  folders.forEach((f, i) => {
    const js = assembleFolder(code, f);
    const cs = fs.existsSync(csharp[i]) && fs.readFileSync(csharp[i], 'utf8').trim() ? JSON.parse(fs.readFileSync(csharp[i], 'utf8')) : null;
    const d = compare(js, cs);
    console.log((d.length ? 'DIFFERENT ' : 'same      ') + f + (js ? '  (' + js.projects.length + ' projects, ' + js.assignments.length + ' entries)' : ''));
    d.slice(0, 20).forEach(x => console.log(x));
    if (d.length > 20) console.log('  ... ' + (d.length - 20) + ' more');
    if (d.length) bad++;
  });
  console.log(bad ? bad + ' folder(s) differ.' : 'Crew Upload reads every folder exactly as the Schedule does.');
  process.exit(bad ? 2 : 0);
}
