// Runs the Schedule's own assembleState() on a schedule folder, reading it the way readFolderState()
// does (missing or unparseable files count as absent). Read-only.
'use strict';
const fs = require('fs');
const path = require('path');

// readJson(): t.trim()?JSON.parse(t):null, and null on any error
function readJson(folder, name) {
  try {
    const t = fs.readFileSync(path.join(folder, name), 'utf8');
    return t.trim() ? JSON.parse(t) : null;
  } catch (e) { return null; }
}

// readFolderState(), without the File System Access API
function readFolderState(folder) {
  const master = readJson(folder, 'pso-master.json');
  if (!master || !Array.isArray(master.pms)) return null;
  const feeds = {};
  for (const pm of master.pms) feeds[pm.id] = readJson(folder, 'pm-' + pm.id + '.json') || { pmId: pm.id, profile: { pinHash: null }, projects: [], assignments: [], notes: [] };
  const requests = readJson(folder, 'pso-requests.json') || [];
  const overrides = readJson(folder, 'pso-overrides.json') || [];
  const progress = readJson(folder, 'pso-progress.json') || [];
  return { master, feeds, requests, overrides, progress };
}

// The app's code with a reader's session: no board loaded yet, no unsaved requests or overrides.
function load(code) {
  return new Function(
    'let state = null; const reqAdded = new Set(), reqRemoved = new Set(); const myOverrides = {};\n' +
    code + '\nreturn assembleState;')();
}

function assembleFolder(code, folder) {
  const fs0 = readFolderState(folder);
  if (!fs0) return null;
  const s = load(code)(fs0);
  return { projects: s.projects, assignments: s.assignments };
}

module.exports = { assembleFolder, readFolderState };
