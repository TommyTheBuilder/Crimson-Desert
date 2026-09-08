'use strict';
// External read-only helper. The only child process we may terminate is ours.
const {spawn}=require('node:child_process');
const readline=require('node:readline');
class ReaderProcess {
  constructor(executable,onExit=()=>{},spawnImpl=spawn){
    this.next=1;this.pending=new Map();this.closed=false;this.closing=false;
    this.child=spawnImpl(executable,[],{windowsHide:true,stdio:['pipe','pipe','pipe']});
    this.exitPromise=new Promise(resolve=>{this.resolveExit=resolve;});
    this.lines=readline.createInterface({input:this.child.stdout,crlfDelay:Infinity});
    this.lines.on('line',line=>{
      if(line.length>16*1024*1024){this.fail(new Error('Antwort des Inventarlesers ist zu groß.'));return;}
      let value;try{value=JSON.parse(line.replace(/^\uFEFF/,''));}catch{this.fail(new Error('Ungültige Antwort des Inventarlesers.'));return;}
      const job=this.pending.get(value.id);if(!job)return;
      this.pending.delete(value.id);clearTimeout(job.timer);
      if(value.ok)job.resolve(value.result);else job.reject(new Error(String(value.error||'Inventar konnte nicht gelesen werden.')));
    });
    this.child.stderr.on('data',()=>{});
    this.child.stdin.on('error',error=>this.fail(error));
    this.child.on('error',error=>this.fail(new Error(error.code==='ENOENT'
      ? 'Die Trainer-Datei runtime\\PywelReader.exe fehlt. Bitte die vollständige Trainer-ZIP erneut in einen Ordner entpacken. Erwarteter Pfad: '+executable
      : 'Inventarleser konnte nicht gestartet werden: '+error.message)));
    this.child.on('exit',(code,signal)=>{
      const expected=this.closing;this.closed=true;this.lines.close();
      this.rejectAll(new Error('Der externe Inventarleser wurde beendet.'));
      this.resolveExit();if(!expected)onExit(code,signal);
    });
  }
  rejectAll(error){for(const job of this.pending.values()){clearTimeout(job.timer);job.reject(error);}this.pending.clear();}
  fail(error){if(this.closed)return;this.closed=true;this.rejectAll(error);this.child.kill();this.resolveExit();}
  request(cmd,args={},timeout=15000){
    if(this.closed||this.closing)return Promise.reject(new Error('Der Inventarleser ist nicht verbunden.'));
    if(!['init','status','inventory','diagnostics','dispose'].includes(cmd))return Promise.reject(new Error('Dieser Leser unterstützt ausschließlich Lesezugriffe.'));
    if(this.pending.size>=8)return Promise.reject(new Error('Bitte den laufenden Lesevorgang abwarten.'));
    const id=this.next++;
    return new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>this.fail(new Error('Inventarleser antwortet nicht. Leseverbindung wurde beendet.')),timeout);
      this.pending.set(id,{resolve,reject,timer});
      this.child.stdin.write(JSON.stringify({id,cmd,args})+'\n',error=>{if(error)this.fail(error);});
    });
  }
  async close(){
    if(this.closing)return this.exitPromise;
    this.closing=true;this.rejectAll(new Error('Verbindung wurde getrennt.'));
    if(!this.closed){this.child.stdin.end();const timer=setTimeout(()=>this.child.kill(),1500);await this.exitPromise;clearTimeout(timer);}
  }
}
module.exports={ReaderProcess};
