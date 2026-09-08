'use strict';
// Portable local-only process host. JSON-lines transport; no HTTP listener.
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');
const crypto = require('node:crypto');
const readline = require('node:readline');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { Worker } = require('node:worker_threads');
const gameItems = require('./game-items');
const {ReaderProcess}=require('./reader-process');
const runFile = promisify(execFile);
const APP = __dirname;
const ROOT = path.dirname(APP);
const DATA = path.join(ROOT, 'data');
const DEFAULT_GAME = 'F:\\Steam\\steamapps\\common\\Crimson Desert';
const FALSE_CAPS = Object.freeze({ health:false,stamina:false,spirit:false,inventory:false,catalog:false,addItem:false,setQuantity:false,travel:false });
const RPC_NAMES = { setToggle:'setToggle',inventory:'inventory',catalog:'catalog',addItem:'addItem',setQuantity:'setQuantity',destinations:'destinations',travel:'travel' };
let settings = { gameDirectory: DEFAULT_GAME };
try { settings = Object.assign(settings, JSON.parse(fs.readFileSync(path.join(DATA,'settings.json'),'utf8'))); } catch (_) {}
let state = { connected:false,pid:null,playerReady:false,message:'Starte Crimson Desert und lade einen Spielstand.',
  buildId:'',fileVersion:'',health:null,maxHealth:null,stamina:null,maxStamina:null,spirit:null,maxSpirit:null,
  capabilities:Object.assign({},FALSE_CAPS),toggles:{health:false,stamina:false,spirit:false} };
let reader = null, stopping = false, polling = false, attaching = false;
let serial = Promise.resolve(), lastError = '', installed = null;
let itemCatalog=null,catalogJob=null,catalogWorker=null;
const recentLogs = [];
function emit(value) { if (!stopping) process.stdout.write(JSON.stringify(value) + '\n'); }
function log(message, level = 'info') {
  const item = { time:new Date().toISOString(),level,message:String(message) };
  recentLogs.push(item); if (recentLogs.length > 250) recentLogs.shift();
  emit({ event:'log', data:item });
  try { fs.mkdirSync(DATA,{recursive:true}); const file=path.join(DATA,'trainer.log');
    if(fs.existsSync(file)&&fs.statSync(file).size>2*1024*1024)fs.renameSync(file,path.join(DATA,'trainer.previous.log'));
    fs.appendFileSync(file,`${item.time} [${level}] ${item.message}\n`);
  } catch (_) {}
}
function sendState(patch) {
  state = Object.assign({},state,patch);
  state.capabilities=Object.assign({},FALSE_CAPS,{inventory:!!state.capabilities.inventory,catalog:!!itemCatalog});
  emit({ event:'state',data:state });
  return state;
}
function clearState(message) {
  return sendState({ connected:false,pid:null,playerReady:false,message,
    health:null,maxHealth:null,stamina:null,maxStamina:null,spirit:null,maxSpirit:null,
    capabilities:Object.assign({},FALSE_CAPS),toggles:{health:false,stamina:false,spirit:false} });
}
function psLiteral(value) { return "'" + String(value).replace(/'/g,"''") + "'"; }
async function powershell(code) {
  const encoded=Buffer.from("$ErrorActionPreference='Stop'; " + code,'utf16le').toString('base64');
  const result = await runFile(path.join(process.env.WINDIR || 'C:\\Windows','System32','WindowsPowerShell','v1.0','powershell.exe'),
    ['-NoProfile','-NonInteractive','-EncodedCommand',encoded], { windowsHide:true,encoding:'utf8',maxBuffer:1024*1024,timeout:20000 });
  return result.stdout.replace(/^\uFEFF/,'').trim();
}
async function inspect() {
  const directory = path.resolve(settings.gameDirectory);
  const exe = path.join(directory,'bin64','CrimsonDesert.exe');
  const stat = await fsp.stat(exe).catch(()=>null);
  if(!stat || !stat.isFile())throw new Error('CrimsonDesert.exe wurde im eingestellten Spielordner nicht gefunden.');
  const file = await fsp.open(exe,'r');
  try {
    const header=Buffer.alloc(4096); await file.read(header,0,header.length,0);
    const pe=header.readUInt32LE(0x3c);
    if(header.toString('ascii',0,2)!=='MZ'||pe>4000||header.toString('ascii',pe,pe+4)!=='PE\0\0'||header.readUInt16LE(pe+4)!==0x8664)
      throw new Error('Die Spieldatei ist keine gültige 64-Bit-Windows-Anwendung.');
  } finally { await file.close(); }
  let buildId='unbekannt',fileVersion='unbekannt';
  try { const manifest=await fsp.readFile(path.resolve(directory,'..','..','appmanifest_3321460.acf'),'utf8');
    buildId=manifest.match(/"buildid"\s+"(\d+)"/i)?.[1]||buildId; } catch (_) {}
  try { fileVersion=await powershell('(Get-Item -LiteralPath '+psLiteral(exe)+').VersionInfo.FileVersion'); } catch (_) {}
  installed={directory,exe,buildId,fileVersion,size:stat.size,modified:stat.mtime.toISOString()};
  sendState({buildId,fileVersion});
  const message=`Installation erkannt. Steam-Build ${buildId}, Dateiversion ${fileVersion}. Die Spielfunktionen werden beim Verbinden geprüft.`;
  log(message);
  ensureCatalog().catch(e=>log('Katalog: '+e.message,'error'));
  return Object.assign({message},installed);
}
async function ensureCatalog(force=false) {
  if(catalogJob)return catalogJob;
  if(itemCatalog&&!force)return itemCatalog;
  sendState({catalogBuilding:true,catalogMessage:'Spielarchive werden geprüft …'});
  catalogJob=new Promise((resolve,reject)=>{
    const worker=new Worker(path.join(APP,'catalog-worker.js'),{workerData:{gameDirectory:settings.gameDirectory,dataDirectory:DATA,force}});catalogWorker=worker;
    let finished=false;
    worker.on('message',msg=>{
      if(msg.progress)sendState({catalogMessage:msg.progress});
      if(msg.error){finished=true;reject(new Error(msg.error));}
      if(msg.result){finished=true;resolve(msg.result);}
    });
    worker.on('error',reject);worker.on('exit',code=>{if(!finished)reject(new Error('Das Einlesen der Spieldaten wurde unterbrochen ('+code+').'));});
  }).then(result=>{
    itemCatalog=result;sendState({catalogAvailable:true,catalogCount:result.items.length,catalogSource:result.metadata.source,catalogBuilding:false,catalogMessage:result.items.length+' Gegenstände aus den Spieldateien geladen.'});
    log(result.items.length+' Gegenstände geladen, davon '+result.metadata.germanNames+' mit deutschen Namen.');return result;
  }).catch(e=>{itemCatalog=null;sendState({catalogAvailable:false,catalogCount:0,catalogBuilding:false,catalogMessage:'Katalog konnte nicht gelesen werden: '+e.message});throw e;})
    .finally(()=>{catalogJob=null;catalogWorker=null;});
  return catalogJob;
}
async function catalog(args){
  const data=await ensureCatalog();const result=gameItems.search(data.items,args,data.metadata);
  return result;
}
async function getGameProcess() {
  const json=await powershell("@(Get-Process -Name CrimsonDesert -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{id=$_.Id;path=$_.Path} }) | ConvertTo-Json -Compress");
  const raw=json?JSON.parse(json):[];
  const all=Array.isArray(raw)?raw:[raw];
  if(!all.length)throw new Error('Crimson Desert läuft noch nicht. Starte das Spiel und lade einen Spielstand.');
  const expected=path.resolve(installed.exe).toLowerCase();
  const found=all.filter(p=>p.path&&path.resolve(p.path).toLowerCase()===expected);
  if(found.length!==1)throw new Error('Der passende Spielprozess konnte nicht eindeutig geprüft werden. Spiel und Trainer müssen auf derselben Rechteebene laufen.');
  return found[0];
}
async function attach() {
  if(attaching)throw new Error('Verbindung wird bereits aufgebaut.');
  if(reader)return {message:'Bereits mit dem Spiel verbunden.'};
  attaching=true;
  try {
    await inspect();
    if(installed.buildId!=='25116796'||String(installed.fileVersion).trim()!=='1.0.0.2760')throw new Error('Für diese Spielversion ist noch kein geprüfter externer Inventarleser vorhanden. Der Gegenstandskatalog bleibt verfügbar.');
    const target=await getGameProcess();
    sendState({message:'Verbindung wird hergestellt. Spielversion und Funktionen werden geprüft …'});
    const current=new ReaderProcess(path.join(ROOT,'runtime','PywelReader.exe'),()=>{
      if(reader!==current)return;reader=null;
      clearState('Der externe Inventarleser wurde beendet. Erneut verbinden.');
      log('Externe Leseverbindung beendet.');
    });
    reader=current;
    const snapshot=await current.request('init',{pid:target.id,exe:installed.exe},30000);
    if(reader!==current)throw new Error('Inventarleser wurde während der Verbindung beendet.');
    sendState(snapshot);
    log('Externe Leseverbindung hergestellt. Es wird keine DLL in das Spiel geladen.');
    return {message:snapshot.message};
  } catch(e) {
    lastError=e.message;
    await detach(false);
    clearState(e.message);
    throw e;
  } finally { attaching=false; }
}
async function detach(notify=true) {
  const oldReader=reader;reader=null;
  if(oldReader)await oldReader.close();
  clearState('Nicht verbunden.');
  if(notify)log('Verbindung getrennt.');
  return {message:'Externe Leseverbindung getrennt.'};
}
async function refresh() {
  if(!reader)return state;
  const current=reader;
  const snapshot=await current.request('status');
  if(reader===current){
    if(!snapshot.connected){reader=null;await current.close();}
    sendState(snapshot);
  }
  return state;
}
async function listFiles(root) {
  const files=[];
  async function visit(directory) {
    for(const item of await fsp.readdir(directory,{withFileTypes:true})) {
      const full=path.join(directory,item.name);
      if(item.isSymbolicLink())throw new Error('Verknüpfungen im Sicherungsordner werden nicht verfolgt.');
      if(item.isDirectory())await visit(full);
      else if(item.isFile())files.push(full);
    }
  }
  await visit(root);return files.sort();
}
async function backup(saveOverride, destinationOverride) {
  const source=saveOverride||path.join(process.env.LOCALAPPDATA||'','Pearl Abyss','CD','save');
  if(!path.isAbsolute(source))throw new Error('Spielstandordner konnte nicht bestimmt werden.');
  const files=await listFiles(source).catch(e=>{throw new Error('Spielstände konnten nicht gesichert werden: '+e.message);});
  if(!files.length)throw new Error('Keine Spielstanddateien gefunden. Bitte zuerst im Spiel speichern.');
  const folder=destinationOverride||path.join(DATA,'backups',new Date().toISOString().replace(/[:.]/g,'-')+'-'+crypto.randomBytes(3).toString('hex'));
  await fsp.mkdir(folder,{recursive:true});
  const records=[];
  try {
    for(const original of files) {
      const rel=path.relative(source,original);
      if(rel.startsWith('..')||path.isAbsolute(rel))throw new Error('Ungültiger Sicherungspfad.');
      const dest=path.join(folder,'save',rel);await fsp.mkdir(path.dirname(dest),{recursive:true});
      const before=await fsp.stat(original);
      await fsp.copyFile(original,dest,fs.constants.COPYFILE_EXCL);
      const after=await fsp.stat(original);
      if(before.size!==after.size||before.mtimeMs!==after.mtimeMs)throw new Error('Das Spiel speichert gerade. Bitte nach dem Speichern erneut versuchen.');
      const copied=await fsp.readFile(dest);
      const hash=crypto.createHash('sha256').update(copied).digest('hex');
      records.push({file:rel,size:copied.length,sha256:hash,mtimeMs:before.mtimeMs});
    }
    // Detect files added, removed or modified while the directory was copied.
    const afterFiles=await listFiles(source);
    if(afterFiles.length!==files.length||afterFiles.some((f,i)=>f!==files[i]))throw new Error('Spielstanddateien haben sich während der Sicherung geändert. Bitte erneut versuchen.');
    for(let i=0;i<files.length;i++) {
      const current=await fsp.stat(files[i]);
      if(current.mtimeMs!==records[i].mtimeMs||current.size!==records[i].size)throw new Error('Spielstand wurde während der Sicherung verändert. Bitte erneut versuchen.');
    }
    await fsp.writeFile(path.join(folder,'manifest.json'),JSON.stringify({version:1,created:new Date().toISOString(),source,buildId:state.buildId,files:records},null,2));
    const message=`${records.length} Spielstanddateien gesichert: ${folder}`;log(message);
    return {message,path:folder,count:records.length};
  } catch(e) {
    // Keep failed snapshots identifiable, never offer them as complete backups.
    await fsp.writeFile(path.join(folder,'UNVOLLSTAENDIG.txt'),e.message).catch(()=>{});
    throw e;
  }
}
function validateCommand(cmd,args) {
  if(typeof cmd!=='string'||cmd.length>32||!args||typeof args!=='object'||Array.isArray(args))throw new Error('Ungültiger Auftrag.');
  if(cmd==='catalog') {
    if(args.query!==undefined&&(typeof args.query!=='string'||args.query.length>120))throw new Error('Suchtext ist zu lang.');
    if(args.category!==undefined&&!gameItems.CATEGORIES.some(x=>x[0]===args.category))throw new Error('Unbekannte Gegenstandskategorie.');
    if(args.page!==undefined&&(!Number.isSafeInteger(args.page)||args.page<1))throw new Error('Ungültige Seite.');
    if(args.pageSize!==undefined&&(!Number.isInteger(args.pageSize)||args.pageSize<1||args.pageSize>200))throw new Error('Ungültige Seitengröße.');
    if(args.limit!==undefined&&(!Number.isInteger(args.limit)||args.limit<1||args.limit>1000))throw new Error('Ungültige Ergebnisanzahl.');
  }
  if(cmd==='addItem') {
    if(!Number.isInteger(args.itemId)||args.itemId<1||args.itemId>65535)throw new Error('Ungültige Gegenstandskennung.');
    if(!Number.isSafeInteger(args.quantity)||args.quantity<1||args.quantity>999999)throw new Error('Menge muss zwischen 1 und 999999 liegen.');
    if(!Number.isInteger(args.itemKey)||typeof args.key!=='string'||args.key.length>4096)throw new Error('Bitte einen Gegenstand aus dem Spieldatenkatalog auswählen.');
  }
  if(cmd==='setQuantity') {
    if(typeof args.slot!=='string'||args.slot.length>160)throw new Error('Bitte einen Inventareintrag auswählen.');
    if(!Number.isInteger(args.itemId)||args.itemId<0||args.itemId>65534)throw new Error('Ungültige Gegenstandskennung.');
    if(!Number.isSafeInteger(args.quantity)||args.quantity<1||args.quantity>999999)throw new Error('Ungültige Menge.');
  }
  if(cmd==='travel'&&(!Number.isInteger(args.sceneId)||args.sceneId<0||args.sceneId>65535||!Number.isInteger(args.nodeIndex)||args.nodeIndex<0||args.nodeIndex>65535))throw new Error('Ungültiges Reiseziel.');
  if(cmd==='setToggle'&&(!['health','stamina','spirit'].includes(args.name)||typeof args.value!=='boolean'))throw new Error('Ungültiger Schalter.');
}
async function diagnostics() {
  const runtime=reader?await reader.request('diagnostics'):null;
  const report={created:new Date().toISOString(),trainer:'0.3.0-external-reader',installation:installed,catalog:itemCatalog?itemCatalog.metadata:null,state,runtime,lastError,logs:recentLogs};
  const directory=path.join(DATA,'diagnostics');await fsp.mkdir(directory,{recursive:true});
  const file=path.join(directory,'diagnose-'+new Date().toISOString().replace(/[:.]/g,'-')+'.json');
  await fsp.writeFile(file,JSON.stringify(report,null,2));
  return {message:'Diagnose gespeichert: '+file,path:file};
}
async function handle(cmd,args={}) {
  validateCommand(cmd,args);
  if(cmd==='status')return state;
  if(cmd==='inspect')return inspect();
  if(cmd==='attach')return attach();
  if(cmd==='detach')return detach();
  if(cmd==='refresh')return refresh();
  if(cmd==='backup')return backup();
  if(cmd==='diagnostics')return diagnostics();
  if(cmd==='catalog')return catalog(args);
  if(cmd==='rebuildCatalog'){const data=await ensureCatalog(true);return {count:data.items.length,message:'Spieldaten neu eingelesen.'};}
  const method=RPC_NAMES[cmd];if(!method)throw new Error('Unbekannter Befehl.');
  if(!reader)throw new Error('Bitte zuerst mit dem laufenden Spiel verbinden.');
  if(cmd!=='inventory')throw new Error('Diese Version unterstützt ausschließlich das Lesen des Inventars und der Spielerwerte.');
  await refresh();
  if(!state.playerReady||!state.capabilities.inventory)throw new Error('Das Inventar ist für den aktuellen Spielzustand nicht verfügbar.');
  const current=reader;
  const result=await current.request(cmd,args);
  if(cmd==='inventory' && result && Array.isArray(result.items)){
    const data=await ensureCatalog();const names=new Map(data.items.map(x=>[x.key,x]));
    result.items=result.items.map(row=>{const item=names.get(row.key);return item&&(row.itemKey===undefined||row.itemKey===item.itemKey)?{...row,name:item.name,itemKey:item.itemKey,category:item.category,description:item.description}:row;});
  }
  if(reader===current)await refresh();
  if(result?.message)log(result.message);
  return result;
}
async function shutdown() {
  if(stopping)return;stopping=true;
  if(catalogWorker)await catalogWorker.terminate();
  try{await detach(false);}catch(_){}
  process.exit(0);
}
function main() {
  // Never auto-attach or write when the application is launched.
  fsp.mkdir(DATA,{recursive:true}).then(()=>fsp.writeFile(path.join(DATA,'settings.json'),JSON.stringify(settings,null,2))).catch(()=>{});
  emit({event:'state',data:state});
  const input=readline.createInterface({input:process.stdin,crlfDelay:Infinity});
  input.on('line',line=>{
    if(line.length>32768){log('Zu großer Auftrag verworfen.','error');return;}
    let request;try{request=JSON.parse(line.replace(/^\uFEFF/,''));}catch(_){emit({id:null,ok:false,error:'Ungültige Anfrage.'});return;}
    if(!Number.isSafeInteger(request.id)){emit({id:null,ok:false,error:'Ungültige Auftragskennung.'});return;}
    serial=serial.then(async()=>{
      try{const result=await handle(request.cmd,request.args||{});emit({id:request.id,ok:true,result});}
      catch(e){lastError=e.message;log(e.message,'error');emit({id:request.id,ok:false,error:e.message});}
    });
  });
  input.on('close',shutdown);
  process.on('SIGTERM',shutdown);process.on('SIGINT',shutdown);
  setInterval(async()=>{
    if(polling||attaching||!reader)return;polling=true;
    try{await refresh();}catch(e){log('Status konnte nicht gelesen werden: '+e.message,'error');}finally{polling=false;}
  },1500).unref();
}
module.exports={validateCommand,backup,listFiles,psLiteral,handle,FALSE_CAPS};
if(require.main===module)main();
