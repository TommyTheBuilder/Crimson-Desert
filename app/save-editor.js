'use strict';
// Safe item insertion through an offline save-game helper. The game process is
// never injected or written to. A complete backup is created before replacement.
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');
const crypto = require('node:crypto');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const runFile = promisify(execFile);

const ROOT = path.dirname(__dirname);
const HELPER = path.join(ROOT, 'runtime', 'PywelSaveEditor.exe');
const HELPER_SOURCE = 'NattKh/CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS@96e7f78fcb00d6cb5615171a7f359f0c2de5b114';

function roots() {
  const local = process.env.LOCALAPPDATA || '';
  if (!local) return [];
  return [
    { platform:'Steam', directory:path.join(local, 'Pearl Abyss', 'CD', 'save') },
    { platform:'Epic', directory:path.join(local, 'Pearl Abyss', 'CD_Epic', 'save') }
  ];
}

async function directoryEntries(directory) {
  try { return await fsp.readdir(directory, { withFileTypes:true }); }
  catch (error) { if (error && error.code === 'ENOENT') return []; throw error; }
}

async function listSaves() {
  const result = [];
  for (const root of roots()) {
    const users = await directoryEntries(root.directory);
    for (const user of users) {
      if (!user.isDirectory() || user.isSymbolicLink()) continue;
      const userDirectory = path.join(root.directory, user.name);
      const slots = await directoryEntries(userDirectory);
      for (const slot of slots) {
        if (!slot.isDirectory() || slot.isSymbolicLink()) continue;
        const file = path.join(userDirectory, slot.name, 'save.save');
        const stat = await fsp.stat(file).catch(()=>null);
        if (!stat || !stat.isFile()) continue;
        result.push({
          path:file,
          platform:root.platform,
          userId:user.name,
          slot:slot.name,
          size:stat.size,
          mtimeMs:stat.mtimeMs,
          modified:stat.mtime.toISOString(),
          display:`${root.platform} · ${user.name}/${slot.name} · ${stat.mtime.toLocaleString('de-DE')}`
        });
      }
    }
  }
  result.sort((a,b)=>b.mtimeMs-a.mtimeMs || a.path.localeCompare(b.path));
  return result;
}

async function helperAvailable() {
  const stat = await fsp.stat(HELPER).catch(()=>null);
  return !!stat && stat.isFile() && stat.size > 0;
}

async function status() {
  const saves = await listSaves();
  const available = await helperAvailable();
  return {
    saveEditorAvailable:available,
    saveCount:saves.length,
    saveTarget:saves.length ? saves[0].path : null,
    saveTargetDisplay:saves.length ? saves[0].display : null,
    saveEditorHelper:HELPER,
    saveEditorSource:HELPER_SOURCE
  };
}

async function runHelper(args, timeout=120000) {
  if (!await helperAvailable()) throw new Error('PywelSaveEditor.exe fehlt im runtime-Ordner. Bitte den vollständigen Trainer-Build verwenden.');
  try {
    return await runFile(HELPER, args, {
      windowsHide:true,
      encoding:'utf8',
      maxBuffer:8*1024*1024,
      timeout
    });
  } catch (error) {
    const detail = String(error?.stderr || error?.stdout || error?.message || '').trim();
    throw new Error('Spielstand-Editor fehlgeschlagen' + (detail ? ': ' + detail.slice(0,4000) : '.'));
  }
}

function samePath(a,b) {
  return path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase();
}

async function chooseSave(requestedPath) {
  const saves = await listSaves();
  if (!saves.length) throw new Error('Kein Crimson-Desert-Spielstand gefunden. Erwartet wird …\\Pearl Abyss\\CD\\save\\<Benutzer>\\<Slot>\\save.save.');
  if (!requestedPath) return saves[0];
  const selected = saves.find(x=>samePath(x.path, requestedPath));
  if (!selected) throw new Error('Der ausgewählte Spielstand gehört nicht zu einem aktuell gefundenen Crimson-Desert-Slot. Bitte die Spielstände neu laden.');
  return selected;
}

async function addItem(args, options={}) {
  if (!Number.isSafeInteger(args.itemKey) || args.itemKey < 1 || args.itemKey > 0xFFFFFFFF) throw new Error('Ungültige Spieldaten-ID.');
  if (!Number.isSafeInteger(args.quantity) || args.quantity < 1 || args.quantity > 999999) throw new Error('Menge muss zwischen 1 und 999999 liegen.');
  if (!await helperAvailable()) throw new Error('Gegenstände hinzufügen ist erst verfügbar, wenn runtime\\PywelSaveEditor.exe gebaut wurde.');
  if (typeof options.isGameRunning === 'function' && await options.isGameRunning())
    throw new Error('Crimson Desert läuft noch. Zum sicheren Hinzufügen das Spiel vollständig schließen und danach erneut versuchen.');

  const save = await chooseSave(args.savePath);
  const before = await fsp.stat(save.path);
  if (!before.isFile() || before.size < 1024) throw new Error('Der ausgewählte Spielstand ist ungültig oder unvollständig.');

  const backupResult = typeof options.backup === 'function' ? await options.backup(path.dirname(save.path)) : null;
  const token = `${process.pid}-${Date.now()}-${crypto.randomBytes(4).toString('hex')}`;
  const temporary = path.join(path.dirname(save.path), `save.save.pywel-${token}.tmp`);
  const rollback = path.join(path.dirname(save.path), `save.save.pywel-${token}.rollback`);
  let originalMoved = false;
  try {
    const craft = await runHelper(['craftitem', save.path, String(args.itemKey), `stack=${args.quantity}`, '-o', temporary], 180000);
    await runHelper(['validate', temporary], 120000);

    const after = await fsp.stat(save.path);
    if (after.size !== before.size || after.mtimeMs !== before.mtimeMs)
      throw new Error('Der Spielstand hat sich während der Bearbeitung geändert. Die Änderung wurde verworfen.');
    const written = await fsp.stat(temporary).catch(()=>null);
    if (!written || !written.isFile() || written.size < 1024) throw new Error('Der bearbeitete Spielstand wurde nicht vollständig erzeugt.');

    await fsp.rename(save.path, rollback);
    originalMoved = true;
    try {
      await fsp.rename(temporary, save.path);
    } catch (error) {
      await fsp.rename(rollback, save.path).catch(()=>{});
      originalMoved = false;
      throw error;
    }
    await fsp.unlink(rollback).catch(()=>{});
    originalMoved = false;

    const helperMessage = String(craft.stderr || craft.stdout || '').trim().split(/\r?\n/).filter(Boolean).slice(-3).join(' · ');
    const message = `Gegenstand ${args.itemKey} × ${args.quantity} wurde in ${save.platform} ${save.userId}/${save.slot} eingefügt. Automatische Sicherung: ${backupResult?.path || 'erstellt'}. Crimson Desert jetzt starten und diesen Spielstand laden.`;
    if (typeof options.log === 'function') options.log(message);
    return { message, savePath:save.path, save:save.display, backupPath:backupResult?.path || null, helperMessage };
  } catch (error) {
    if (originalMoved) await fsp.rename(rollback, save.path).catch(()=>{});
    await fsp.unlink(temporary).catch(()=>{});
    await fsp.unlink(rollback).catch(()=>{});
    throw error;
  }
}

module.exports = { HELPER, HELPER_SOURCE, listSaves, helperAvailable, status, addItem, chooseSave };
