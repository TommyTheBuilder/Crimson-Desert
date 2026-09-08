'use strict';
const {parentPort,workerData}=require('node:worker_threads');
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto');
const {GameArchives}=require('./game-archives');
const {VERSION,paloc,parseItems}=require('./game-items');
async function build(){
  const archives=new GameArchives(workerData.gameDirectory);const info=await archives.index();
  const files=['iteminfo.staticinfoheader','iteminfo.staticinfobody','gamedata/stringtable/binary__/ger/item.paloc','gamedata/stringtable/binary__/eng/item.paloc',
    'itemgroupinfo.staticinfoheader','itemgroupinfo.staticinfobody','gamedata/stringtable/binary__/ger/itemgroup.paloc'];
  const entries=files.map(x=>archives.entry(x));
  const stats=await Promise.all(entries.map(e=>fs.stat(path.join(workerData.gameDirectory,e.group,e.pazIndex+'.paz')).then(s=>({path:e.path,size:s.size,modified:s.mtimeMs}))));
  const fingerprint=crypto.createHash('sha256').update(JSON.stringify({version:VERSION,archives:info.fingerprint,stats})).digest('hex');
  const cache=path.join(workerData.dataDirectory,'catalog-v2.json');
  if(!workerData.force){try{const cached=JSON.parse(await fs.readFile(cache,'utf8'));if(cached.version===VERSION&&cached.fingerprint===fingerprint&&Array.isArray(cached.items)&&cached.items.length===cached.metadata.count){return {...cached,cached:true};}}catch(_) {}}
  parentPort.postMessage({progress:'Gegenstände und deutsche Texte werden aus den Spielarchiven gelesen …'});
  const buffers=await Promise.all(files.map(x=>archives.readFile(x)));
  const german=paloc(buffers[2]),english=paloc(buffers[3]);
  let items=parseItems(buffers[0],buffers[1],german,english);
  // Group mapping is supplied by the bounded binary parser, never runtime IDs.
  const groups=require('./game-item-groups');
  items=groups.applyCategories(items,groups.parseItemGroups(buffers[4],buffers[5],new Set(items.map(x=>x.itemKey))),paloc(buffers[6]),new Map());
  const metadata={source:'Spieldateien · Deutsch',count:items.length,germanNames:items.filter(x=>x.nameSource==='Deutsch').length,
    internalNames:items.filter(x=>x.nameSource==='Interner Spielname').length,created:new Date().toISOString(),
    gameDirectory:workerData.gameDirectory,archives:info.manifests.length,archiveFiles:info.scannedFileCount,
    files:entries.map((e,i)=>({path:e.path,archive:e.group+'/'+e.pazIndex+'.paz',bytes:buffers[i].length,sha256:crypto.createHash('sha256').update(buffers[i]).digest('hex')}))};
  const result={version:VERSION,fingerprint,metadata,items};
  await fs.mkdir(workerData.dataDirectory,{recursive:true});
  const temporary=cache+'.'+process.pid+'.tmp';await fs.writeFile(temporary,JSON.stringify(result));await fs.rename(temporary,cache);
  return result;
}
build().then(result=>parentPort.postMessage({result})).catch(e=>parentPort.postMessage({error:e.message}));
