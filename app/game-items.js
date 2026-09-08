'use strict';
// Parses installed ItemInfo records and Pearl Abyss localization tables.
// Persistent item keys are never treated as runtime table indices.
const CATEGORIES = [
  ['all','Alle Gegenstände'],['weapons','Waffen'],['armor','Rüstung & Zubehör'],
  ['materials','Materialien'],['consumables','Verbrauchsgegenstände'],
  ['currency','Geld & Währungen'],['quest','Questgegenstände'],['other','Sonstiges']
];
const VERSION = 2;
function check(value,message) { if(!value) throw new Error(message); }
function paloc(buffer) {
  let o=0;const result=new Map();
  function need(n){check(Number.isSafeInteger(n)&&n>=0&&o+n<=buffer.length,'Unvollständige Sprachdatei.');}
  function string(max){need(4);const n=buffer.readUInt32LE(o);o+=4;check(n<=max,'Ungültige Textlänge.');need(n);const s=buffer.toString('utf8',o,o+n);o+=n;return s;}
  while(o<buffer.length-4){need(8);o+=8;const key=string(100),value=string(1024*1024);check(/^\d+$/.test(key),'Ungültiger Sprachschlüssel.');check(!result.has(key),'Doppelter Sprachschlüssel.');result.set(key,value);}
  check(o+4===buffer.length&&buffer.readUInt32LE(o)===result.size,'Sprachdatei-Eintragszahl stimmt nicht.');
  return result;
}
function clean(value) {
  return String(value||'').replace(/<br\s*\/?\s*>/gi,'\n').replace(/<[^>]*>/g,'').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/\r\n/g,'\n').trim();
}
function classify(key) {
  if(/^(Quest|Story)_|_Quest(?:_|$)/i.test(key))return 'quest';
  if(/^(Copper|Silver|Gold)(?:$|_Pack$)|^(Heavy|Small)_(Copper|Silver|Gold)_Pack$|Currency|Coin|Money/i.test(key))return 'currency';
  if(/Sword|Bow|Shield|Axe|Spear|Dagger|Mace|Hammer|Cannon|Rifle|Pistol|Musket|Arrow|Kunai|Shuriken|Halberd/i.test(key))return 'weapons';
  if(/Armor|Armour|Helmet|Helm_|Gloves|Boots|Shoes|Ring_|Necklace|Earring|Cloak|Cape_|Belt_|Pauldrons|Greaves|Gauntlet|Outfit/i.test(key))return 'armor';
  if(/Ore|Ingot|Timber|Wood|Leather|Fur_|Hide_|Feather|Cloth|Fabric|Thread|Cotton|Wool|Material|Resource|Stone|Gem|Crystal/i.test(key))return 'materials';
  if(/Potion|Elixir|Food_|Cooking|Cooked|Meal|Soup|Stew|Bread|Grilled|Roasted|Meat|Fish|Fruit|Vegetable|Mushroom|Herb|Drink|Beer|Wine/i.test(key))return 'consumables';
  return 'other';
}
function parseItems(header,body,german,english=new Map()) {
  check(header.length>=2,'Gegenstandstabelle fehlt.');
  const count=header.readUInt16LE(0);check(count>0&&header.length===2+count*8,'Ungültiges Gegenstandsverzeichnis.');
  const entries=Array.from({length:count},(_,i)=>({itemKey:header.readUInt32LE(2+i*8),offset:header.readUInt32LE(6+i*8)})).sort((a,b)=>a.offset-b.offset);
  const keys=new Set(),internal=new Set(),items=[];
  for(let i=0;i<count;i++){
    const {itemKey,offset}=entries[i],end=i+1<count?entries[i+1].offset:body.length;let o=offset;
    function need(n){check(n>=0&&o+n<=end,'Beschädigter Gegenstand '+itemKey+'.');}
    need(8);check(!keys.has(itemKey)&&body.readUInt32LE(o)===itemKey,'Uneindeutiger Gegenstandsschlüssel.');keys.add(itemKey);o+=4;
    const len=body.readUInt32LE(o);o+=4;check(len>0&&len<4096,'Ungültiger interner Gegenstandsname.');need(len+1+8+1+8+4);
    const key=body.toString('utf8',o,o+len);o+=len;check(body[o++]===0&&!key.includes('\0'),'Ungültiger Gegenstandsname.');
    check(!internal.has(key),'Doppelter interner Gegenstandsname: '+key);internal.add(key);
    const stack=body.readBigUInt64LE(o);o+=8;
    check(body[o++]===7,'Unbekanntes Namensformat.');const nameId=body.readBigUInt64LE(o);o+=8;
    check(nameId===(BigInt(itemKey)<<32n|0x70n),'Namenskennung stimmt nicht.');
    const nameLen=body.readUInt32LE(o);o+=4;need(nameLen);check(body.toString('utf8',o,o+nameLen)===nameId.toString(),'Namensverweis stimmt nicht.');
    const ger=clean(german.get(nameId.toString())),eng=clean(english.get(nameId.toString()));
    const descId=(BigInt(itemKey)<<32n|0x71n).toString();
    const description=clean(german.get(descId)||english.get(descId));
    const category=classify(key);
    items.push({itemKey,key,itemId:null,name:ger||eng||key,description,category,
      type:CATEGORIES.find(x=>x[0]===category)[1],categorySource:'Aus internem Spielnamen zugeordnet',
      maxStack:stack<=BigInt(Number.MAX_SAFE_INTEGER)?Number(stack):stack.toString(),rarity:'',icon:null,nameSource:ger?'Deutsch':eng?'Englisch':'Interner Spielname',
      gameIds:{'Spieldaten-ID':itemKey,'Interner Name':key},addable:false,reason:'Zum Hinzufügen muss der Gegenstand im laufenden Spiel eindeutig erkannt sein.'});
  }
  items.sort((a,b)=>a.name.localeCompare(b.name,'de')||a.itemKey-b.itemKey);
  return items;
}
function fold(value){return String(value).normalize('NFKD').replace(/[\u0300-\u036f]/g,'').replace(/ß/g,'ss').toLowerCase();}
function search(items,args={},metadata={}){
  const query=fold(args.query||'').trim(),category=args.category||'all',pageSize=args.pageSize||75;
  check(CATEGORIES.some(x=>x[0]===category),'Unbekannte Kategorie.');
  check(Number.isInteger(pageSize)&&pageSize>=1&&pageSize<=200,'Ungültige Seitengröße.');
  check(args.page===undefined||Number.isInteger(args.page)&&args.page>=1,'Ungültige Seite.');
  const words=query.split(/\s+/).filter(Boolean);
  const matches=items.filter(x=>(category==='all'||x.category===category)&&(!words.length||words.every(w=>fold([x.name,x.key,x.itemKey,x.description].join(' ')).includes(w))));
  const page=Math.min(args.page||1,Math.max(1,Math.ceil(matches.length/pageSize)));
  return {items:matches.slice((page-1)*pageSize,page*pageSize),total:matches.length,totalAll:items.length,page,pageSize,source:metadata.source||'Spieldateien · Deutsch',offline:true,
    categories:CATEGORIES.map(([id,label])=>({id,label,count:id==='all'?items.length:items.filter(x=>x.category===id).length}))};
}
module.exports={VERSION,CATEGORIES,paloc,parseItems,search,classify,clean};
