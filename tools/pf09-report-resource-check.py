"""Windows renderer process-tree sampling. No service or port. Sampling uses ctypes; PDF verification uses pypdf.
Run from repo root with the operator-owned manifest and renderer inputs.
Sampled working sets count shared pages per process; private bytes are also recorded.
This measures this machine, not a 4-core/8-GiB deployment certification.
"""
import ctypes as C
from ctypes import wintypes as W
import hashlib,json,os,platform,re,shutil,subprocess,sys,time
from pathlib import Path
from pypdf import PdfReader

ROOT=Path.cwd()
OUT=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else ROOT/'.run/r2-resource-font/resources'
OUT.mkdir(parents=True,exist_ok=True)
K=C.WinDLL('kernel32',use_last_error=True)
P=C.WinDLL('psapi',use_last_error=True)
class Entry(C.Structure):
 _fields_=[('size',W.DWORD),('usage',W.DWORD),('pid',W.DWORD),('heap',C.c_size_t),('module',W.DWORD),('threads',W.DWORD),('parent',W.DWORD),('priority',W.LONG),('flags',W.DWORD),('name',W.WCHAR*260)]
class Memory(C.Structure):
 _fields_=[('cb',W.DWORD),('faults',W.DWORD)]+[(n,C.c_size_t) for n in ['peakWorkingSet','workingSet','peakPaged','paged','peakNonPaged','nonPaged','pagefile','peakPagefile','private']]
class MemoryStatus(C.Structure):
 _fields_=[('length',W.DWORD),('load',W.DWORD)]+[(n,C.c_ulonglong) for n in ['totalPhysical','availablePhysical','totalPagefile','availablePagefile','totalVirtual','availableVirtual','extended']]
K.CreateToolhelp32Snapshot.argtypes=[W.DWORD,W.DWORD];K.CreateToolhelp32Snapshot.restype=W.HANDLE
K.Process32FirstW.argtypes=[W.HANDLE,C.POINTER(Entry)];K.Process32NextW.argtypes=[W.HANDLE,C.POINTER(Entry)]
K.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD];K.OpenProcess.restype=W.HANDLE
K.CloseHandle.argtypes=[W.HANDLE];K.GetExitCodeProcess.argtypes=[W.HANDLE,C.POINTER(W.DWORD)]
K.TerminateProcess.argtypes=[W.HANDLE,W.UINT]
P.GetProcessMemoryInfo.argtypes=[W.HANDLE,C.POINTER(Memory),W.DWORD]

def processes():
 h=K.CreateToolhelp32Snapshot(2,0)
 if h==W.HANDLE(-1).value:raise C.WinError(C.get_last_error())
 e=Entry();e.size=C.sizeof(e);rows={}
 try:
  ok=K.Process32FirstW(h,C.byref(e))
  while ok:
   rows[e.pid]=e.parent;ok=K.Process32NextW(h,C.byref(e))
 finally:K.CloseHandle(h)
 return rows

def alive(h):
 status=W.DWORD();return bool(K.GetExitCodeProcess(h,C.byref(status))) and status.value==259

def run(name,inputs,timeout_ms=None,cancel=False):
 folder=OUT/name;folder.mkdir(exist_ok=False)
 manifest=json.loads((ROOT/'.run/pf09-c3-preflight/renderer-manifest.json').read_text(encoding='utf8'))
 if timeout_ms:manifest['timeoutMs']=timeout_ms
 manifest_file=folder/'manifest.json';raw=json.dumps(manifest).encode();manifest_file.write_bytes(raw)
 jobs=[];logs=[];handles={};owner={};samples=[];cancel_sent=False
 started=time.perf_counter()
 try:
  for i,input_file in enumerate(inputs):
   target=folder/str(i);target.mkdir();data=json.loads((ROOT/input_file).read_text(encoding='utf8'))
   data['engineManifestHash']=hashlib.sha256(raw).hexdigest();data['format']='Pdf';data['preview']=False
   (target/'input.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf8')
   log=open(target/'process.log','wb');logs.append(log)
   env={**os.environ,'PF09_RENDERER_MANIFEST':str(manifest_file)}
   job=subprocess.Popen([shutil.which('node'),str(ROOT/'tools/pf09-render-report.mjs'),'--input',str(target/'input.json'),'--output',str(target)],env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
   jobs.append(job);owner[job.pid]=i
  settled=None
  while True:
   now=time.perf_counter();rows=processes();changed=True
   while changed:
    changed=False
    for pid,parent in rows.items():
     if pid not in owner and parent in owner:owner[pid]=owner[parent];changed=True
   for pid in owner:
    if pid not in handles:
     h=K.OpenProcess(0x1000|0x10|1,False,pid)
     if h:handles[pid]=h
   totals=[{'workingSet':0,'private':0,'processes':0} for _ in jobs]
   for pid,h in handles.items():
    if not alive(h):continue
    m=Memory();m.cb=C.sizeof(m)
    if P.GetProcessMemoryInfo(h,C.byref(m),m.cb):
     t=totals[owner[pid]];t['workingSet']+=m.workingSet;t['private']+=m.private;t['processes']+=1
   samples.append({'elapsedMs':round((now-started)*1000,2),'jobs':totals})
   if cancel and not cancel_sent and now-started>1 and sum(t['processes'] for t in totals)>2:
    (folder/'0/cancel.request').write_text('cancel',encoding='utf8');cancel_sent=True
   if all(j.poll() is not None for j in jobs):
    settled=settled or now
    if now-settled>1:break
   if now-started>180:raise TimeoutError(name)
   time.sleep(.05)
  live=[pid for pid,h in handles.items() if alive(h)]
  results=[]
  for i,j in enumerate(jobs):
   target=folder/str(i);receipt=json.loads((target/'renderer-stopped.json').read_text())
   row={'exit':j.returncode,'reason':receipt['reason']}
   if not cancel and not timeout_ms:
    result=json.loads((target/'result.json').read_text(encoding='utf8'))
    pdf=PdfReader(target/'result.pdf');row['pages']=len(pdf.pages)
    assert j.returncode==0 and receipt['reason']=='Completed' and len(pdf.pages)==len(result['pages'])
    text='\n'.join(p.extract_text() or '' for p in pdf.pages)
    if 'large.json' in inputs[i]:
     ids=list(map(int,re.findall(r'Record (\d+) -',text)));assert ids==list(range(1,2401));row['verifiedRows']=len(ids)
   elif cancel:assert j.returncode==2 and receipt['reason']=='Cancelled'
   else:assert j.returncode!=0 and receipt['reason']=='TimedOut'
   results.append(row)
  gaps=[b['elapsedMs']-a['elapsedMs'] for a,b in zip(samples,samples[1:])]
  result={'name':name,'results':results,'durationMs':round((time.perf_counter()-started)*1000),'sampleCount':len(samples),'maxSampleGapMs':round(max(gaps,default=0),2),'sampledPeakWorkingSetBytes':max(sum(j['workingSet'] for j in s['jobs']) for s in samples),'sampledPeakPrivateBytes':max(sum(j['private'] for j in s['jobs']) for s in samples),'remainingOwnedProcesses':live}
  (folder/'samples.json').write_text(json.dumps(samples),encoding='utf8');(folder/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf8')
  assert not live,'Renderer left owned child processes running'
  print(json.dumps(result),flush=True);return result
 finally:
  # Only task-owned handles are terminated on a harness failure, never name-based kills.
  for h in handles.values():
   if alive(h):K.TerminateProcess(h,99)
   K.CloseHandle(h)
  for j in jobs:
   if j.poll() is None:j.terminate()
   j.wait(timeout=10)
  for log in logs:log.close()

if __name__=='__main__':
 mem=MemoryStatus();mem.length=C.sizeof(mem);K.GlobalMemoryStatusEx(C.byref(mem))
 metadata={'os':platform.platform(),'logicalCpuCount':os.cpu_count(),'physicalMemoryBytes':mem.totalPhysical,'sampleTargetMs':50,'standardMachine':False}
 (OUT/'machine.json').write_text(json.dumps(metadata,indent=2),encoding='utf8')
 large='.run/r2-nested-table/large.json';bundle='.run/photoresist/bundle/input.json'
 results=[]
 for i in range(3):results.append(run('single-large-'+str(i),[large]))
 results.append(run('parallel-large',[large,large]))
 for i in range(5):results.append(run('repeat-bundle-'+str(i),[bundle]))
 results.append(run('cancel',[large],cancel=True))
 results.append(run('timeout',[large],timeout_ms=500))
 (OUT/'summary.json').write_text(json.dumps({'machine':metadata,'cases':results},indent=2),encoding='utf8')
