'use strict';

// Read-only decoder of the indexed ItemGroupInfo prefix. The field order was
// verified against all 1,600 installed records and both referenced index tables.
// No inferred category assignments: each member comes from the game's own list.
function parseItemGroups(header, body, itemKeys = null) {
  function requireValue(ok, text) { if (!ok) throw new Error(`ItemGroupInfo: ${text}`); }
  requireValue(header.length >= 2, 'Index fehlt.');
  const count = header.readUInt16LE(0);
  requireValue(header.length === 2 + count * 6, 'Unbekanntes Indexformat.');
  const indexed = [], groupKeys = new Set();
  for (let i = 0; i < count; i++) {
    const key = header.readUInt16LE(2 + i * 6), offset = header.readUInt32LE(4 + i * 6);
    requireValue(!groupKeys.has(key) && offset < body.length, 'Ungültiger oder doppelter Indexeintrag.');
    groupKeys.add(key); indexed.push({ key, offset });
  }
  indexed.sort((a, b) => a.offset - b.offset);
  const groups = [];
  for (let i = 0; i < indexed.length; i++) {
    const entry = indexed[i], end = i + 1 < indexed.length ? indexed[i + 1].offset : body.length;
    let cursor = entry.offset;
    requireValue(end > cursor, 'Überlappende Datensätze.');
    const need = n => requireValue(Number.isSafeInteger(n) && n >= 0 && cursor + n <= end, `Unvollständiger Datensatz ${entry.key}.`);
    const u8 = () => { need(1); return body[cursor++]; };
    const u16 = () => { need(2); const v = body.readUInt16LE(cursor); cursor += 2; return v; };
    const u32 = () => { need(4); const v = body.readUInt32LE(cursor); cursor += 4; return v; };
    const utf8 = (nul) => {
      const length = u32(); need(length + (nul ? 1 : 0));
      const value = body.toString('utf8', cursor, cursor + length); cursor += length;
      if (nul) requireValue(u8() === 0, 'Interner Name ist nicht abgeschlossen.');
      requireValue(!value.includes('\ufffd'), 'Ungültiger UTF-8-Text.');
      return value;
    };
    requireValue(u16() === entry.key, 'Index und Datensatz stimmen nicht überein.');
    const internalName = utf8(true);
    requireValue(u8() === 8, 'Unbekannte Namenskategorie.');
    need(8); const localizationId = body.readBigUInt64LE(cursor).toString(); cursor += 8;
    const defaultName = utf8(false);
    requireValue(defaultName === localizationId || defaultName === '', 'Unbekanntes Lokalisierungsformat.');
    const childCount = u32(); need(childCount * 2);
    const children = [];
    for (let n = 0; n < childCount; n++) children.push(u16());
    const itemCount = u32(); need(itemCount * 4);
    const memberKeys = [];
    for (let n = 0; n < itemCount; n++) memberKeys.push(u32());
    requireValue(children.every(key => groupKeys.has(key)), `Unbekannte Untergruppe in ${entry.key}.`);
    if (itemKeys) requireValue(memberKeys.every(key => itemKeys.has(key)), `Unbekannter Gegenstand in ${entry.key}.`);
    groups.push({ key: entry.key, internalName, localizationId, children, itemKeys: memberKeys, unparsedBytes: end - cursor });
  }
  return groups;
}

function indexItemGroups(groups) {
  const byItem = new Map();
  for (const group of groups) {
    for (const key of new Set(group.itemKeys)) {
      if (!byItem.has(key)) byItem.set(key, []);
      byItem.get(key).push(group);
    }
  }
  return byItem;
}

function applyCategories(items, groups, germanNames = new Map(), englishNames = new Map()) {
  const membership = indexItemGroups(groups);
  const lookup = (source, key) => source instanceof Map ? source.get(key) : source[key];
  const title = group => lookup(germanNames, group.localizationId) || lookup(englishNames, group.localizationId) || group.internalName;
  const categoryFor = group => {
    const name = group.internalName.toLowerCase();
    if (/^itemgroup_subcategory_money$/.test(name)) return 'currency';
    if (/^itemgroup_subcategory_equip_weapon_/.test(name)) return 'weapons';
    if (/^itemgroup_subcategory_equip_(armor_|accessory_|riding$|pet_armor$|backpack$)/.test(name)) return 'armor';
    if (/^itemgroup_category_material$|^itemgroup_subcategory_(material_|metarial_)/.test(name)) return 'materials';
    if (/^itemgroup_category_food$|^itemgroup_subcategory_(koreafood$|potion$|food_horse$)/.test(name)) return 'consumables';
    if (/^itemgroup_subcategory_etc_quest_/.test(name)) return 'quest';
    return null;
  };
  return items.map(item => {
    const key = Number(item.itemKey ?? item.id ?? item.key);
    const nativeGroups = (membership.get(key) || []).filter(g => /^ItemGroup_(?:SubCategory|Category)_/i.test(g.internalName));
    const subgroups = nativeGroups.filter(g => /^ItemGroup_SubCategory_/i.test(g.internalName));
    // ItemGroup stores an explicit ordered taxonomy. Prefer a subgroup over a
    // parent category, preserving that native order when there are overlaps.
    const ordered = [...subgroups, ...nativeGroups.filter(g => !subgroups.includes(g))];
    const categorizingGroup = ordered.find(g => categoryFor(g));
    const category = categorizingGroup ? categoryFor(categorizingGroup) : 'other';
    const displayGroup = subgroups[0] || nativeGroups[0];
    return { ...item, category,
      categorySource: nativeGroups.length ? 'Spieldaten · Gegenstandsgruppe' : 'Spieldaten · keine Kategoriezuordnung',
      type: displayGroup ? title(displayGroup) : 'Nicht zugeordnet',
      gameGroups: nativeGroups.map(g => ({ key: g.key, name: title(g), internalName: g.internalName })) };
  });
}

module.exports = { parseItemGroups, indexItemGroups, applyCategories };
