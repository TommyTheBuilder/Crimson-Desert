'use strict';

// Read-only PAZ/PAMT archive reader. Format work credited to XeTrinityz/Trinity
// (pak.cpp), lazorr410/crimson-desert-unpacker (paz_crypto.py) and
// LukeFZ/pycrimson (PAPGT metadata and checksums), all MIT licensed.
// Their notices accompany this file in archive-third-party-notices.txt.
const fs = require('fs/promises');
const path = require('path');
const crypto = require('crypto');
const MAX_MANIFEST_BYTES = 64 * 1024 * 1024;
const MAX_EXTRACT_BYTES = 256 * 1024 * 1024;

function ensure(ok, message) { if (!ok) throw new Error(message); }
function rot(v, k) { return ((v << k) | (v >>> (32 - k))) >>> 0; }
function hashlittle(data, init = 0) {
  let a = (0xdeadbeef + data.length + init) >>> 0, b = a, c = a, o = 0, n = data.length;
  while (n > 12) {
    a = (a + data.readUInt32LE(o)) >>> 0;
    b = (b + data.readUInt32LE(o + 4)) >>> 0;
    c = (c + data.readUInt32LE(o + 8)) >>> 0;
    a = ((a - c) ^ rot(c, 4)) >>> 0; c = (c + b) >>> 0;
    b = ((b - a) ^ rot(a, 6)) >>> 0; a = (a + c) >>> 0;
    c = ((c - b) ^ rot(b, 8)) >>> 0; b = (b + a) >>> 0;
    a = ((a - c) ^ rot(c, 16)) >>> 0; c = (c + b) >>> 0;
    b = ((b - a) ^ rot(a, 19)) >>> 0; a = (a + c) >>> 0;
    c = ((c - b) ^ rot(b, 4)) >>> 0; b = (b + a) >>> 0;
    o += 12; n -= 12;
  }
  if (!n) return c;
  const tail = Buffer.alloc(12); data.copy(tail, 0, o);
  a = (a + tail.readUInt32LE(0)) >>> 0;
  b = (b + tail.readUInt32LE(4)) >>> 0;
  c = (c + tail.readUInt32LE(8)) >>> 0;
  c = ((c ^ b) - rot(b, 14)) >>> 0;
  a = ((a ^ c) - rot(c, 11)) >>> 0;
  b = ((b ^ a) - rot(a, 25)) >>> 0;
  c = ((c ^ b) - rot(b, 16)) >>> 0;
  a = ((a ^ c) - rot(c, 4)) >>> 0;
  b = ((b ^ a) - rot(a, 14)) >>> 0;
  c = ((c ^ b) - rot(b, 24)) >>> 0;
  return c;
}

function decrypt(data, filename) {
  const seed = hashlittle(Buffer.from(path.posix.basename(filename).toLowerCase(), 'utf8'), 0xc5ede);
  const iv = Buffer.alloc(16), key = Buffer.alloc(32);
  const deltas = [0, 0x0a0a0a0a, 0x0c0c0c0c, 0x06060606, 0x0e0e0e0e, 0x0a0a0a0a, 0x06060606, 0x02020202];
  for (let i = 0; i < 4; i++) iv.writeUInt32LE(seed, i * 4);
  deltas.forEach((d, i) => key.writeUInt32LE((seed ^ 0x60616263 ^ d) >>> 0, i * 4));
  const decoder = crypto.createDecipheriv('chacha20', key, iv);
  return Buffer.concat([decoder.update(data), decoder.final()]);
}

function lz4Block(src, size) {
  ensure(Number.isInteger(size) && size >= 0 && size <= MAX_EXTRACT_BYTES, 'Unzulässige Archiv-Dateigröße.');
  const dst = Buffer.alloc(size); let s = 0, d = 0;
  function length(n) {
    if (n !== 15) return n;
    let b; do { ensure(s < src.length, 'Unvollständiger LZ4-Längenwert.'); b = src[s++]; n += b; } while (b === 255);
    return n;
  }
  while (s < src.length) {
    const token = src[s++]; const lit = length(token >>> 4);
    ensure(s + lit <= src.length && d + lit <= size, 'Ungültiger LZ4-Literalbereich.');
    src.copy(dst, d, s, s + lit); s += lit; d += lit;
    if (s === src.length) break;
    ensure(s + 2 <= src.length, 'Unvollständiger LZ4-Verweis.');
    const offset = src.readUInt16LE(s); s += 2;
    ensure(offset > 0 && offset <= d, 'Ungültiger LZ4-Abstand.');
    const match = length(token & 15) + 4;
    ensure(d + match <= size, 'LZ4-Ausgabebereich überschritten.');
    for (let i = 0; i < match; i++) dst[d + i] = dst[d - offset + i];
    d += match;
  }
  ensure(d === size, `LZ4-Größe stimmt nicht: ${d}/${size}.`);
  return dst;
}

function nameFromBlob(blob, offset) {
  const parts = [], seen = new Set();
  while (offset !== 0xffffffff) {
    ensure(parts.length < 256 && !seen.has(offset), 'Ungültige Archiv-Pfadverknüpfung.');
    seen.add(offset);
    ensure(offset + 5 <= blob.length, 'Ungültiger Archiv-Pfadbereich.');
    const count = blob[offset + 4];
    ensure(offset + 5 + count <= blob.length, 'Unvollständiger Archiv-Pfad.');
    parts.push(blob.toString('utf8', offset + 5, offset + 5 + count));
    offset = blob.readUInt32LE(offset);
  }
  return parts.reverse().join('');
}

function normalize(logicalPath) {
  const p = String(logicalPath).replace(/\\/g, '/').replace(/^\/+|\/+$/g, '').toLowerCase();
  ensure(p && !p.includes('\0') && !p.split('/').some(x => x === '..' || x === '.'), 'Ungültiger Archiv-Dateiname.');
  return p;
}

function parseGroupTree(data) {
  ensure(data.length >= 16, 'Unvollständige Spielarchiv-Metadaten.');
  const checksum = data.readUInt32LE(4), count = data[8], namesAt = 12 + count * 12;
  ensure(namesAt + 4 <= data.length, 'Ungültige Spielarchiv-Metadaten.');
  ensure(hashlittle(data.subarray(12), 0xc5ede) === checksum, 'Prüfsumme der Spielarchiv-Metadaten stimmt nicht.');
  const namesSize = data.readUInt32LE(namesAt), names = data.subarray(namesAt + 4);
  ensure(names.length === namesSize, 'Ungültige Gruppenliste im Spielarchiv.');
  const groups = []; const seen = new Set();
  for (let i = 0; i < count; i++) {
    const at = 12 + i * 12, nameOff = data.readUInt32LE(at + 4);
    ensure(nameOff < names.length, 'Ungültiger Gruppenname im Spielarchiv.');
    const end = names.indexOf(0, nameOff);
    ensure(end >= nameOff, 'Unvollständiger Gruppenname im Spielarchiv.');
    const group = names.toString('ascii', nameOff, end);
    ensure(/^\d{4}$/.test(group) && !seen.has(group), 'Ungültige oder doppelte Archivgruppe.'); seen.add(group);
    groups.push({ group, optional: data[at] !== 0, languageMask: data.readUInt16LE(at + 1), checksum: data.readUInt32LE(at + 8) });
  }
  return { checksum, versionTag: data.readUInt32LE(0), groups };
}

function parseManifest(data, group, manifestName, options = {}) {
  let o = 0;
  const need = (n) => ensure(Number.isSafeInteger(n) && n >= 0 && o + n <= data.length, `Beschädigtes Archivverzeichnis ${group}/${manifestName}.`);
  const u32 = () => { need(4); const v = data.readUInt32LE(o); o += 4; return v; };
  const checksum = u32(); need(8);
  const pazCount = data.readUInt16LE(o), reserved = data.readUInt16LE(o + 2); o += 4;
  const unknown = u32();
  ensure(reserved === 0, 'Unbekannte Version des Archivverzeichnisses.');
  ensure(hashlittle(data.subarray(12), 0xc5ede) === checksum, `Prüfsumme des Archivverzeichnisses ${group}/${manifestName} stimmt nicht.`);
  need(pazCount * 12);
  const chunks = new Map();
  for (let i = 0; i < pazCount; i++) {
    const id = u32(), crc = u32(), size = u32();
    ensure(!chunks.has(id), 'Doppelte PAZ-Archivkennung.'); chunks.set(id, { id, checksum: crc, size });
  }
  const dirSize = u32(); need(dirSize); const dirBlob = data.subarray(o, o + dirSize); o += dirSize;
  const fileSize = u32(); need(fileSize); const fileBlob = data.subarray(o, o + fileSize); o += fileSize;
  const dirCount = u32(); need(dirCount * 16); const dirsOffset = o; o += dirCount * 16;
  const fileCount = u32(); need(fileCount * 20); const filesOffset = o; o += fileCount * 20;
  ensure(o === data.length, `Unerwartetes Archivverzeichnis-Format ${group}/${manifestName}.`);
  const entries = [];
  const assigned = new Uint8Array(fileCount); let assignedCount = 0;
  for (let d = 0; d < dirCount; d++) {
    const at = dirsOffset + d * 16;
    const dirname = nameFromBlob(dirBlob, data.readUInt32LE(at + 4));
    ensure(hashlittle(Buffer.from(dirname), 0xc5ede) === data.readUInt32LE(at), 'Prüfsumme eines Archivpfads stimmt nicht.');
    const includeDirectory = options.allFiles || /(^|\/)(binarystaticinfo__|stringtable|bin|gamedata|staticinfo)(\/|$)/i.test(dirname);
    const first = data.readUInt32LE(at + 8), count = data.readUInt32LE(at + 12);
    ensure(first + count <= fileCount, 'Ungültige Dateiliste im Archiv.');
    for (let i = first; i < first + count; i++) {
      ensure(!assigned[i], 'Überlappende Dateilisten im Archiv.'); assigned[i] = 1; assignedCount++;
      const f = filesOffset + i * 20;
      const pazIndex = data.readUInt16LE(f + 16), chunk = chunks.get(pazIndex);
      ensure(chunk && data.readUInt32LE(f + 4) + data.readUInt32LE(f + 8) <= chunk.size, 'Ungültiger PAZ-Verweis im Archivverzeichnis.');
      if (!includeDirectory) continue;
      const name = nameFromBlob(fileBlob, data.readUInt32LE(f));
      if (!options.allFiles && !/\.(?:staticinfobody|staticinfoheader|pabgb|pabgh|paloc)$/i.test(name)) continue;
      const logicalPath = normalize(`${dirname}/${name}`);
      const entry = {
        path: logicalPath, name: path.posix.basename(logicalPath), group, manifest: manifestName, checksum,
        offset: data.readUInt32LE(f + 4), compressedSize: data.readUInt32LE(f + 8),
        originalSize: data.readUInt32LE(f + 12), pazIndex,
        compression: data[f + 18], flags: data[f + 19]
      };
      entries.push(entry);
    }
  }
  ensure(assignedCount === fileCount, 'Nicht zugeordnete Dateien im Archivverzeichnis.');
  return { checksum, pazCount, unknown, dirCount, fileCount, entries };
}

class GameArchives {
  constructor(gameDir, options = {}) {
    this.gameDir = path.resolve(gameDir); this.options = options;
    this.entries = new Map(); this.duplicates = new Map(); this.manifests = [];
    this.ready = false; this.meta = null;
  }

  async index() {
    this.ready = false; this.entries.clear(); this.duplicates.clear(); this.manifests = [];
    const existingGroups = (await fs.readdir(this.gameDir, { withFileTypes: true }))
      .filter(e => e.isDirectory() && /^\d{4}$/.test(e.name)).map(e => e.name).sort();
    ensure(existingGroups.length, 'Keine Crimson-Desert-Archive im Spielordner gefunden.');
    const metaPath = path.join(this.gameDir, 'meta', '0.papgt');
    const bytes = await fs.readFile(metaPath);
    const groupTree = parseGroupTree(bytes);
    this.meta = { path: 'meta/0.papgt', bytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex'), ...groupTree };
    this.unlistedGroups = existingGroups.filter(g => !groupTree.groups.some(e => e.group === g));
    for (const groupEntry of groupTree.groups) {
      const group = groupEntry.group;
      if (!existingGroups.includes(group)) {
        ensure(groupEntry.optional || groupEntry.languageMask !== 0x7fff, `Erforderliche Spielarchivgruppe ${group} fehlt.`);
        continue;
      }
      const names = (await fs.readdir(path.join(this.gameDir, group)))
        .filter(n => /^\d+\.pamt$/.test(n)).sort((a, b) => Number.parseInt(a) - Number.parseInt(b));
      ensure(names.includes('0.pamt'), `Archivverzeichnis der Gruppe ${group} fehlt.`);
      for (const manifestName of names) {
        // PAPGT selects 0.pamt by checksum. Other PAMT files are not a documented
        // patch-priority mechanism and must not replace the selected manifest.
        if (manifestName !== '0.pamt') continue;
        const filename = path.join(this.gameDir, group, manifestName);
        const stat = await fs.stat(filename);
        ensure(stat.size <= MAX_MANIFEST_BYTES, 'Archivverzeichnis ist ungewöhnlich groß.');
        const data = await fs.readFile(filename);
        const manifest = parseManifest(data, group, manifestName, this.options);
        ensure(manifest.checksum === groupEntry.checksum, `Archiv ${group} passt nicht zu den installierten Spielmetadaten.`);
        this.manifests.push({ group, name: manifestName, checksum: manifest.checksum, size: stat.size, modified: stat.mtimeMs,
          sha256: crypto.createHash('sha256').update(data).digest('hex'), fileCount: manifest.fileCount });
        for (const entry of manifest.entries) {
          const prior = this.entries.get(entry.path);
          if (prior) {
            const versions = this.duplicates.get(entry.path) || [prior]; versions.push(entry); this.duplicates.set(entry.path, versions);
          } else this.entries.set(entry.path, entry);
        }
      }
      if (this.options.onProgress) this.options.onProgress({ group, files: this.entries.size });
    }
    this.ready = true;
    return this.info();
  }

  listFiles(pattern) {
    ensure(this.ready, 'Archivverzeichnis ist noch nicht geladen.');
    const all = Array.from(this.entries.values());
    if (!pattern) return all;
    if (pattern instanceof RegExp) return all.filter(e => { pattern.lastIndex = 0; return pattern.test(e.path); });
    const search = String(pattern).toLowerCase(); return all.filter(e => e.path.includes(search));
  }

  entry(logicalPath) {
    ensure(this.ready, 'Archivverzeichnis ist noch nicht geladen.');
    const key = normalize(logicalPath);
    let entry = this.entries.get(key);
    if (!entry && !key.includes('/')) {
      const matches = this.listFiles().filter(e => e.name === key);
      ensure(matches.length <= 1, `Mehrdeutiger Archiv-Dateiname: ${key}. Vollständigen Pfad verwenden.`);
      entry = matches[0];
    }
    ensure(entry, `Datei nicht in den installierten Spielarchiven gefunden: ${key}.`);
    ensure(!this.duplicates.has(entry.path), `Mehrere Archivversionen für ${entry.path}; keine eindeutige Auswahl möglich.`);
    return { ...entry };
  }

  async readFile(logicalPath) {
    const entry = this.entry(logicalPath);
    ensure(entry.compressedSize <= MAX_EXTRACT_BYTES && entry.originalSize <= MAX_EXTRACT_BYTES, 'Archivdatei überschreitet die Extraktionsgrenze.');
    const filename = path.join(this.gameDir, entry.group, `${entry.pazIndex}.paz`);
    const handle = await fs.open(filename, 'r');
    let raw;
    try {
      const stat = await handle.stat();
      ensure(entry.offset + entry.compressedSize <= stat.size, `Archivbereich ist unvollständig: ${entry.path}.`);
      raw = Buffer.alloc(entry.compressedSize);
      let done = 0;
      while (done < raw.length) {
        const { bytesRead } = await handle.read(raw, done, raw.length - done, entry.offset + done);
        ensure(bytesRead > 0, 'Spielarchiv konnte nicht vollständig gelesen werden.'); done += bytesRead;
      }
    } finally { await handle.close(); }
    const encryption = entry.compression >>> 4;
    ensure(encryption === 0 || encryption === 3, `Unbekannte Archivverschlüsselung ${encryption}.`);
    if (encryption === 3) raw = decrypt(raw, entry.name);
    const method = entry.compression & 15;
    if (method === 0) {
      ensure(raw.length === entry.originalSize, 'Größe einer unkomprimierten Archivdatei stimmt nicht.'); return raw;
    }
    if (method === 2) return lz4Block(raw, entry.originalSize);
    if (method === 1) {
      if (raw.length === entry.originalSize) return raw;
      ensure(raw.length >= 128 && entry.originalSize >= 128, 'Unvollständiger DDS-Header.');
      return Buffer.concat([raw.subarray(0, 128), lz4Block(raw.subarray(128), entry.originalSize - 128)]);
    }
    throw new Error(`Unbekannte Archivkomprimierung ${method} für ${entry.path}.`);
  }

  info() {
    return { gameDir: this.gameDir, ready: this.ready, fileCount: this.entries.size, manifests: this.manifests.slice(),
      duplicateCount: this.duplicates.size, scannedFileCount: this.manifests.reduce((n, m) => n + m.fileCount, 0),
      unlistedGroups: this.unlistedGroups || [], readOrder: 'PAPGT selects 0.pamt by verified checksum; duplicate logical paths are refused', meta: this.meta,
      fingerprint: crypto.createHash('sha256').update(JSON.stringify({ meta: this.meta, manifests: this.manifests })).digest('hex') };
  }
}

module.exports = { GameArchives, lz4Block, hashlittle, decrypt, parseManifest, parseGroupTree };
