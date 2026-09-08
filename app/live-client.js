'use strict';
// Live item spawning bridge. A tiny x64 injector loads PywelLive.dll into the
// already-running single-player game, then this module talks to it over a local
// named pipe. The injected DLL queues item creation onto the game's own game
// thread and uses the engine's inventory transaction path.
const fs=require('node:fs/promises');
const path=require('node:path');
const net=require('node:net');
const {execFile}=require('node:child_process');
const {promisify}=require('node:util');
const runFile=promisify(execFile);

const ROOT=path.dirname(__dirname);
const INJECTOR=path.join(ROOT,'runtime','PywelInjector.exe');
const DLL=path.join(ROOT,'runtime','PywelLive.dll');
const PIPE='\\\\.\\pipe\\PywelTrainer-CrimsonDesert';
const SOURCE='XeTrinityz/Trinity@70c9a00dd6e10b2081d706a837756844c11f5c2b + Pywel named-pipe bridge';

async function fileExists(file){
  const s=await fs.stat(file).catch(()=>null);
  return !!s&&s.isFile()&&s.size>0;
}
async function available(){return await fileExists(INJECTOR)&&await fileExists(DLL);}

function request(command,timeout=8000){
  return new Promise((resolve,reject)=>{
    let done=false,data='';
    const socket=net.createConnection(PIPE);
    const timer=setTimeout(()=>finish(new Error('Der Live-Spawner antwortet nicht.')),timeout);
    function finish(error,value){
      if(done)return;done=true;clearTimeout(timer);
      try{socket.destroy();}catch(_){ }
      if(error)reject(error);else resolve(value);
    }
    socket.setEncoding('utf8');
    socket.on('connect',()=>socket.write(command+'\n'));
    socket.on('data',chunk=>{
      data+=chunk;
      const nl=data.indexOf('\n');
      if(nl>=0)finish(null,data.slice(0,nl).trim());
      else if(data.length>16384)finish(new Error('Ungültige Antwort des Live-Spawners.'));
    });
    socket.on('error',error=>finish(error));
    socket.on('end',()=>{if(!done)finish(data.trim()?null:new Error('Live-Spawner-Verbindung wurde beendet.'),data.trim());});
  });
}

function parseStatus(line){
  if(!line||!line.startsWith('OK\tSTATUS'))throw new Error(line&&line.startsWith('ERR\t')?line.slice(4):'Live-Spawner meldet einen ungültigen Status.');
  const result={loaded:true,inventoryReady:false,catalogReady:false,persist:false};
  for(const part of line.split('\t').slice(2)){
    const at=part.indexOf('=');if(at<0)continue;
    const key=part.slice(0,at),value=part.slice(at+1);
    if(key==='inventory')result.inventoryReady=value==='1';
    if(key==='catalog')result.catalogReady=value==='1';
    if(key==='persist')result.persist=value==='1';
  }
  result.ready=result.inventoryReady&&result.catalogReady&&result.persist;
  return result;
}

async function status(){
  const have=await available();
  if(!have)return {liveSpawnerAvailable:false,liveSpawnerLoaded:false,liveSpawnerReady:false,liveSpawnerMessage:'Live-Komponente fehlt im runtime-Ordner.'};
  try{
    const pong=await request('PING',1200);
    if(pong!=='OK\tPONG')throw new Error('Kein gültiger PONG.');
    const raw=parseStatus(await request('STATUS',2500));
    return {
      liveSpawnerAvailable:true,
      liveSpawnerLoaded:true,
      liveSpawnerReady:raw.ready,
      liveSpawnerMessage:raw.ready?'Live-Spawner bereit. Gegenstände können sofort ins laufende Inventar eingefügt werden.':
        !raw.inventoryReady?'Live-Mod geladen. Lade deinen Spielstand und warte, bis das Inventar bereit ist.':
        !raw.catalogReady?'Live-Mod geladen. Gegenstandstabelle wird noch vorbereitet.':
        'Live-Mod geladen. Die serverseitige Inventarspiegelung ist noch nicht bereit; kurz warten oder den Spielstand neu laden.',
      liveInventoryReady:raw.inventoryReady,
      liveCatalogReady:raw.catalogReady,
      livePersistReady:raw.persist,
      liveSpawnerSource:SOURCE
    };
  }catch(_){
    return {liveSpawnerAvailable:true,liveSpawnerLoaded:false,liveSpawnerReady:false,liveSpawnerMessage:'Live-Komponente ist vorhanden, aber noch nicht in Crimson Desert geladen.',liveSpawnerSource:SOURCE};
  }
}

async function inject(pid,expectedExe){
  if(!await available())throw new Error('runtime\\PywelInjector.exe oder runtime\\PywelLive.dll fehlt. Bitte den vollständigen Trainer-Build verwenden.');
  if(!Number.isInteger(pid)||pid<1)throw new Error('Ungültiger Crimson-Desert-Prozess.');
  try{
    const result=await runFile(INJECTOR,[String(pid),DLL,expectedExe||''],{windowsHide:true,encoding:'utf8',maxBuffer:1024*1024,timeout:20000});
    const text=String(result.stdout||'').trim();
    if(!text.startsWith('OK'))throw new Error(text||String(result.stderr||'').trim()||'Injektion nicht bestätigt.');
    return text;
  }catch(error){
    const detail=String(error?.stderr||error?.stdout||error?.message||'').trim();
    throw new Error('Live-Mod konnte nicht geladen werden'+(detail?': '+detail.slice(0,3000):'.')+' Trainer und Spiel müssen mit derselben Rechteebene laufen.');
  }
}

async function ensure(pid,expectedExe){
  let current=await status();
  if(current.liveSpawnerLoaded)return current;
  await inject(pid,expectedExe);
  const until=Date.now()+12000;
  do{
    await new Promise(r=>setTimeout(r,250));
    current=await status();
    if(current.liveSpawnerLoaded)return current;
  }while(Date.now()<until);
  throw new Error('PywelLive.dll wurde geladen, aber der Live-Spawner hat seine lokale Verbindung nicht geöffnet. Die Spielversion wird möglicherweise von den Live-Signaturen noch nicht erkannt.');
}

async function addItem(key,quantity){
  if(typeof key!=='string'||!key||key.length>512||/[\t\r\n]/.test(key))throw new Error('Ungültiger interner Gegenstandsschlüssel.');
  if(!Number.isSafeInteger(quantity)||quantity<1||quantity>999999)throw new Error('Ungültige Menge.');
  const line=await request(`ADD\t${key}\t${quantity}`,15000);
  if(line.startsWith('OK\tADDED')){
    const parts=line.split('\t');
    return {message:`${quantity} × ${key} wurde live ins laufende Inventar eingefügt.`,typeId:Number(parts[2])||null,raw:line};
  }
  throw new Error(line.startsWith('ERR\t')?line.slice(4):'Live-Spawner konnte den Gegenstand nicht hinzufügen.');
}

module.exports={INJECTOR,DLL,PIPE,SOURCE,available,status,inject,ensure,addItem,request};