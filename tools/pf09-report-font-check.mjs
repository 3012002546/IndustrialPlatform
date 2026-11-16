import {createRequire} from 'node:module';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
const out=resolve(process.argv[2] || '.run/r2-resource-font/fonts');
const {mkdir}=await import('node:fs/promises');await mkdir(out,{recursive:true});
const hash=b=>createHash('sha256').update(b).digest('hex');
const require=createRequire(resolve('src/frontend/package.json'));
const {chromium}=require('@playwright/test');
const m=JSON.parse(await readFile('.run/pf09-c3-preflight/renderer-manifest.json','utf8'));
const browser=await chromium.launch({executablePath:m.browserPath,headless:true,chromiumSandbox:true});
try {
 const page=await browser.newPage();let requests=0;await page.route('**/*',r=>{requests++;return r.abort()});
 const families=['Microsoft YaHei','SimSun','Arial','PF09 Missing Font 20261115'];
 await page.setContent('<meta charset="utf-8"><body>'+families.map((f,i)=>'<p id="f'+i+'" style="font-family:&quot;'+f+'&quot;,sans-serif">光刻胶工单 称量记录 合计 繁體 µg ℃ ± ≤ ≥ 0123456789 ABCxyz</p>').join(''));
 const availability=await page.evaluate(async families=>Promise.all(families.map(async(f,i)=>{try {await new FontFace('probe'+i,'local("'+f+'")').load();return {family:f,available:true}}catch{return {family:f,available:false}}})),families);
 const cdp=await page.context().newCDPSession(page);await cdp.send('DOM.enable');await cdp.send('CSS.enable');const {root}=await cdp.send('DOM.getDocument');
 for(let i=0;i<families.length;i++){const {nodeId}=await cdp.send('DOM.querySelector',{nodeId:root.nodeId,selector:'#f'+i});availability[i].actual=(await cdp.send('CSS.getPlatformFontsForNode',{nodeId})).fonts;}
 await page.pdf({path:join(out,'font-probe.pdf'),format:'A4',printBackground:true});
 const registered=resolve('.run/pf09-c3-preflight/renderer-manifest.json');
 const manifestHash=hash(await readFile(registered));const cases=[];
 for (const [index,family] of families.entries()) for (const orientation of ['Portrait','Landscape']) {
  const folder=join(out,index+'-'+orientation);await mkdir(folder,{recursive:true});
  const text='光刻胶工单 称量记录 合计 繁體 µg ℃ ± ≤ ≥ 0123456789 ABCxyz';
  const html=Array.from({length:60},(_,i)=>'<p data-report-marker="r'+i+'" style="font-family:&quot;'+family+'&quot;,sans-serif;font-weight:'+(i%2?700:400)+'">FONTROW-'+String(i).padStart(3,'0')+' '+text+'</p>').join('');
  await writeFile(join(folder,'input.json'),JSON.stringify({html,css:'',paper:{size:'A4',orientation},expectedMarkers:Array.from({length:60},(_,i)=>'r'+i),engineManifestHash:manifestHash}));
  const p=spawnSync(process.execPath,['tools/pf09-render-report.mjs','--input',join(folder,'input.json'),'--output',folder],{env:{...process.env,PF09_RENDERER_MANIFEST:registered},windowsHide:true,encoding:'utf8',timeout:60000});
  if(p.status!==0)throw new Error('Font rendering failed: '+family+' '+p.stderr);
  const result=JSON.parse(await readFile(join(folder,'result.json'),'utf8'));
  const substituted=result.diagnostics.some(d=>d.code==='REPORT_FONT_SUBSTITUTED');
  if(substituted!==!availability[index].available)throw new Error('Font diagnostic mismatch: '+family);
  cases.push({family,orientation,pages:result.pages.length,substituted,fontResolution:result.fontResolution,folder});
 }
 const inventory=[];
 for(const filename of ['msyh.ttc','msyhbd.ttc','simsun.ttc','arial.ttf','arialbd.ttf']) {
  const path=join(process.env.WINDIR || 'C:/Windows','Fonts',filename);const bytes=await readFile(path);inventory.push({filename,bytes:bytes.length,sha256:hash(bytes)});
 }
 await writeFile(join(out,'font-check.json'),JSON.stringify({requests,fonts:availability,cases,localFontInventory:inventory,fontRedistributionTested:false},null,2));console.log(JSON.stringify({requests,cases:cases.map(c=>({family:c.family,orientation:c.orientation,pages:c.pages,substituted:c.substituted}))}));
}finally{await browser.close()}
