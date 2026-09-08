'use strict';
// Supervisor around the existing backend.js. It fixes game-path discovery and
// adds a live item-spawn transport without weakening the read-only reader.
const fs=require('node:fs');
const fsp=fs.promises;
const path=require('node:path');
const readline=require('node:readline');
const {spawn,execFile}=require('node:child_process');
const {promisify}=require('node:util');
const liveClient=require('./live-client');
const runFile=promisify(execFile);
const APP=__dirname,ROOT=path.dirname(APP),DATA=path.join(ROOT,'data'),SETTINGS=path.join(DATA,'settings.json');
let stopping=false,lastDetected='';

function emit(value){if(!stopping)process.stdout.write(JSON.stringify(value)+'\n');}
function directLog(message,level='info'){emit({event:'log',data:{time:new Date().toISOString(),level,message:String(message)}});}
function psLiteral(value){return "'"+String(value).replace(/'/g,"''")+"'";}
async function powershell(code){
  const encoded=Buffer.from("$ErrorActionPreference='Stop'; "+code,'utf16le').toString('base64');
  const result=await runFile(path.join(process.env.WINDIR||'C:\\Windows','System32','WindowsPowerShell','v1.0','powershell.exe'),
    ['-NoProfile','-NonInteractive','-EncodedCommand',encoded],{windowsHide:true,encoding:'utf8',maxBuffer:1024*1024,timeout:20000});
  return result.stdout.replace(/^\uFEFF/,'').trim();
}
async function gameProcesses(){
  const json=await powershell("@(Get-Process -Name CrimsonDesert -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{id=$_.Id;path=$_.Path} }) | ConvertTo-Json -Compress");
  if(!json)return [];
  const raw=JSON.parse(json),all=Array.isArray(raw)?raw:[raw];
  return all.filter(p=>p&&Number.isInteger(p.id)&&typeof p.path==='string'&&p.path.length>0);
}
function addCandidate(list,value){
  if(typeof value!=='string'||!value.trim())return;
  let resolved=path.resolve(value.trim().replace(/^"|"$/g,''));
  const base=path.basename(resolved).toLowerCase();
  // Accept any of the forms users naturally paste:
  //   ...\\Crimson Desert
  //   ...\\Crimson Desert\\bin64
  //   ...\\Crimson Desert\\bin64\\CrimsonDesert.exe
  if(base==='crimsondesert.exe')resolved=path.dirname(path.dirname(resolved));
  else if(base==='bin64')resolved=path.dirname(resolved);
  list.push(resolved);
}
async function gameDirValid(dir){const s=dir?await fsp.stat(path.join(dir,'bin64','CrimsonDesert.exe')).catch(()=>null):null;return !!s&&s.isFile();}
async function steamRoots(){
  const roots=[];
  try{
    const raw=await powershell("$v=@(); try{$v+=(Get-ItemProperty -LiteralPath 'HKCU:\\Software\\Valve\\Steam' -ErrorAction Stop).SteamPath}catch{}; try{$v+=(Get-ItemProperty -LiteralPath 'HKLM:\\SOFTWARE\\WOW6432Node\\Valve\\Steam' -ErrorAction Stop).InstallPath}catch{}; $v|Where-Object{$_}|Select-Object -Unique|ConvertTo-Json -Compress");
    if(raw){const parsed=JSON.parse(raw);for(const v of (Array.isArray(parsed)?parsed:[parsed]))addCandidate(roots,v);}
  }catch(_){ }
  addCandidate(roots,path.join(process.env['ProgramFiles(x86)']||'C:\\Program Files (x86)','Steam'));
  const seen=new Set();return roots.filter(x=>{const k=x.toLowerCase();if(seen.has(k))return false;seen.add(k);return true;});
}
async function detectGameDirectory(){
  const candidates=[];
  try{const cfg=JSON.parse(await fsp.readFile(SETTINGS,'utf8'));addCandidate(candidates,cfg.gameDirectory);}catch(_){ }
  try{for(const p of await gameProcesses())addCandidate(candidates,p.path);}catch(_){ }
  for(const steam of await steamRoots()){
    addCandidate(candidates,path.join(steam,'steamapps','common','Crimson Desert'));
    try{
      const text=await fsp.readFile(path.join(steam,'steamapps','libraryfolders.vdf'),'utf8');
      const re=/"path"\s+"([^"]+)"/g;let m;
      while((m=re.exec(text))){const lib=m[1].replace(/\\\\/g,'\\');addCandidate(candidates,path.join(lib,'steamapps','common','Crimson Desert'));}
    }catch(_){ }
  }
  const manifests=path.join(process.env.PROGRAMDATA||'C:\\ProgramData','Epic','EpicGamesLauncher','Data','Manifests');
  try{
    for(const e of await fsp.readdir(manifests,{withFileTypes:true})){
      if(!e.isFile()||!e.name.toLowerCase().endsWith('.item'))continue;
      try{const d=JSON.parse(await fsp.readFile(path.join(manifests,e.name),'utf8'));if(/crimson\s*desert/i.test(String(d.DisplayName||d.AppName||'')))addCandidate(candidates,d.InstallLocation);}catch(_){ }
    }
  }catch(_){ }
  const seen=new Set();
  for(const dir of candidates){const k=dir.toLowerCase();if(seen.has(k))continue;seen.add(k);if(await gameDirValid(dir))return dir;}
  throw new Error('CrimsonDesert.exe wurde nicht automatisch gefunden. Erwartet wird <Spielordner>\\bin64\\CrimsonDesert.exe. Akzeptiert werden Spielordner, bin64-Ordner oder der vollständige EXE-Pfad.');
}
async function persistDetectedDirectory(){
  const dir=await detectGameDirectory();
  let cfg={};try{cfg=JSON.parse(await fsp.readFile(SETTINGS,'utf8'));}catch(_){ }
  if(cfg.gameDirectory!==dir){cfg.gameDirectory=dir;await fsp.mkdir(DATA,{recursive:true});await fsp.writeFile(SETTINGS,JSON.stringify(cfg,null,2));}
  lastDetected=dir;return dir;
}

class ChildBackend{
  constructor(){this.child=null;this.next=0;this.pending=new Map();this.loadedDirectory='';}
  async start(){
    if(this.child)return;
    try{this.loadedDirectory=await persistDetectedDirectory();directLog('Crimson Desert automatisch gefunden: '+this.loadedDirectory);}catch(e){directLog(e.message,'warn');}
    const node=path.join(ROOT,'runtime','node.exe'),backend=path.join(APP,'backend.js');
    this.child=spawn(node,[backend],{cwd:ROOT,windowsHide:true,stdio:['pipe','pipe','pipe']});
    const lines=readline.createInterface({input:this.child.stdout,crlfDelay:Infinity});
    lines.on('line',line=>this.onLine(line));
    this.child.stderr.on('data',data=>directLog(String(data).trim(),'info'));
    this.child.on('exit',()=>{const err=new Error('Der Trainer-Hintergrundprozess wurde beendet.');for(const p of this.pending.values())p.reject(err);this.pending.clear();this.child=null;});
  }
  onLine(line){
    let value;try{value=JSON.parse(line.replace(/^\uFEFF/,''));}catch(_){directLog('Ungültige Antwort des Basis-Backends.','error');return;}
    if(Number.isSafeInteger(value.id)&&this.pending.has(value.id)){const p=this.pending.get(value.id);this.pending.delete(value.id);value.ok?p.resolve(value.result):p.reject(new Error(String(value.error||'Backend-Fehler.')));}
    else emit(value);
  }
  async request(cmd,args={},timeout=120000){
    await this.start();const id=++this.next;
    return new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('Basis-Backend antwortet nicht.'));},timeout);
      this.pending.set(id,{resolve:v=>{clearTimeout(timer);resolve(v);},reject:e=>{clearTimeout(timer);reject(e);}});
      this.child.stdin.write(JSON.stringify({id,cmd,args})+'\n',error=>{if(error){clearTimeout(timer);this.pending.delete(id);reject(error);}});
    });
  }
  async restartFor(dir){
    if(!this.child){this.loadedDirectory=dir;return this.start();}
    if(this.loadedDirectory&&path.resolve(this.loadedDirectory).toLowerCase()===path.resolve(dir).toLowerCase())return;
    try{this.child.stdin.end();}catch(_){ }
    await new Promise(r=>setTimeout(r,250));
    try{if(this.child&&!this.child.killed)this.child.kill();}catch(_){ }
    this.child=null;this.loadedDirectory=dir;await this.start();
  }
  close(){try{if(this.child)this.child.stdin.end();}catch(_){ }try{if(this.child)this.child.kill();}catch(_){ }this.child=null;}
}
const base=new ChildBackend();

async function matchedProcess(){
  const dir=await persistDetectedDirectory();await base.restartFor(dir);
  const exe=path.join(dir,'bin64','CrimsonDesert.exe'),want=path.resolve(exe).toLowerCase();
  const all=await gameProcesses();const hit=all.filter(p=>path.resolve(p.path).toLowerCase()===want);
  if(hit.length!==1)throw new Error(all.length?'Der passende CrimsonDesert.exe-Prozess wurde nicht eindeutig gefunden. Trainer und Spiel müssen mit derselben Rechteebene laufen.':'Crimson Desert läuft noch nicht.');
  return {process:hit[0],exe,dir};
}
async function liveSummary(running){
  if(!running){const available=await liveClient.available();return {liveSpawnerAvailable:available,liveSpawnerLoaded:false,liveSpawnerReady:false,liveSpawnerMessage:available?'Live-Komponente bereit; sie wird beim laufenden Spiel automatisch geladen.':'Live-Komponente fehlt im runtime-Ordner.'};}
  return liveClient.status();
}
async function augment(state){
  const procs=await gameProcesses().catch(()=>[]),running=procs.length>0,live=await liveSummary(running).catch(e=>({liveSpawnerAvailable:false,liveSpawnerLoaded:false,liveSpawnerReady:false,liveSpawnerMessage:e.message}));
  const out=Object.assign({},state,{gameRunning:running},live);
  out.capabilities=Object.assign({},state&&state.capabilities||{});
  out.capabilities.addItem=!!out.capabilities.addItem||!!live.liveSpawnerAvailable;
  return out;
}
async function verifyCatalogItem(args){
  const result=await base.request('catalog',{query:String(args.key||''),category:'all',page:1,pageSize:200},90000);
  const item=Array.isArray(result.items)?result.items.find(x=>x.key===args.key&&x.itemKey===args.itemKey):null;
  if(!item)throw new Error('Der ausgewählte Gegenstand stimmt nicht mit dem aktuell eingelesenen Spieldatenkatalog überein.');
  return item;
}
async function handle(cmd,args={}){
  if(cmd==='inspect'){
    const dir=await persistDetectedDirectory();await base.restartFor(dir);return augment(await base.request(cmd,args,90000));
  }
  if(cmd==='status'||cmd==='refresh')return augment(await base.request(cmd,args));
  if(cmd==='attach'){
    const target=await matchedProcess();const result=await base.request(cmd,args,90000);
    let live;try{live=await liveClient.ensure(target.process.id,target.exe);directLog('Live-Spawner wurde in Crimson Desert geladen.');}catch(e){live={liveSpawnerAvailable:await liveClient.available(),liveSpawnerLoaded:false,liveSpawnerReady:false,liveSpawnerMessage:e.message};directLog(e.message,'warn');}
    const state=Object.assign(await augment(await base.request('status',{})),live,{gameRunning:true});
    return Object.assign({},state,{message:state.liveSpawnerReady?'Spiel verbunden. Live-Spawner bereit.':String(result&&result.message||state.message||'Spiel verbunden.')});
  }
  if(cmd==='catalog'){
    const result=await base.request(cmd,args,90000),live=await liveSummary((await gameProcesses().catch(()=>[])).length>0),available=!!live.liveSpawnerAvailable||!!result.saveEditorAvailable;
    result.items=(result.items||[]).map(item=>Object.assign({},item,{addable:available,reason:live.liveSpawnerReady?'Live-Spawner bereit: sofort ins laufende Inventar.':live.liveSpawnerAvailable?'Live-Komponente vorhanden; sie wird bei Bedarf automatisch geladen.':item.reason}));
    return Object.assign(result,live);
  }
  if(cmd==='addItem'){
    const procs=await gameProcesses();
    if(procs.length){
      const item=await verifyCatalogItem(args),target=await matchedProcess();
      const live=await liveClient.ensure(target.process.id,target.exe);
      if(!live.liveSpawnerReady)throw new Error(live.liveSpawnerMessage||'Live-Spawner ist noch nicht bereit.');
      const result=await liveClient.addItem(item.key,args.quantity);
      const state=await augment(await base.request('status',{}));
      result.message=`${args.quantity} × ${item.name} wurde live ins laufende Inventar eingefügt.`;result.state=state;directLog(result.message);return result;
    }
    return base.request(cmd,args,180000);
  }
  return base.request(cmd,args,cmd==='rebuildCatalog'?600000:120000);
}

async function main(){
  await base.start();
  const input=readline.createInterface({input:process.stdin,crlfDelay:Infinity});
  let serial=Promise.resolve();
  input.on('line',line=>{
    if(line.length>32768){emit({id:null,ok:false,error:'Ungültige Anfrage.'});return;}
    let request;try{request=JSON.parse(line.replace(/^\uFEFF/,''));}catch(_){emit({id:null,ok:false,error:'Ungültige Anfrage.'});return;}
    if(!Number.isSafeInteger(request.id)){emit({id:null,ok:false,error:'Ungültige Auftragskennung.'});return;}
    serial=serial.then(async()=>{try{emit({id:request.id,ok:true,result:await handle(request.cmd,request.args||{})});}catch(e){directLog(e.message,'error');emit({id:request.id,ok:false,error:e.message});}});
  });
  input.on('close',()=>{stopping=true;base.close();process.exit(0);});
  process.on('SIGTERM',()=>{stopping=true;base.close();process.exit(0);});
  process.on('SIGINT',()=>{stopping=true;base.close();process.exit(0);});
}
module.exports={handle,detectGameDirectory,persistDetectedDirectory,gameProcesses};
if(require.main===module)main().catch(e=>{directLog(e.message,'error');process.exit(1);});