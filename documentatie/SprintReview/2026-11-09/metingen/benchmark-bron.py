import concurrent.futures,csv,datetime,hashlib,http.client,json,math,os,platform,random,shutil,socket,statistics,subprocess,sys,time
from pathlib import Path
ROOT=Path('/Users/jeffreydoornbos/INF2C-Team-1');TMP=Path('/tmp/cargohub-review-20261109');OUT=ROOT/'documentatie/SprintReview/2026-11-09/metingen'
PSQL=['/opt/homebrew/bin/psql','-X','-w','-h','127.0.0.1','-p','55447','-U','cargohub_review','-v','ON_ERROR_STOP=1','-At']
DB='review_benchmark';KEY='review-local-disposable';PATHS=['/api/v1/warehouses','/api/v1/items','/api/v1/items/1','/api/v1/items/1/inventory/totals']
processes=[];logs=[];samples=[];runs=[];made=False
hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'data').glob('*.json')}
def sql(db,query=None,file=None):return subprocess.check_output(PSQL+['-d',db]+(['-c',query] if query else ['-f',str(file)]),text=True)
def freeport():
 with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
def request(port,path):
 c=http.client.HTTPConnection('127.0.0.1',port,timeout=10);start=time.perf_counter()
 try:
  c.request('GET',path,headers={'API_KEY':KEY,'Connection':'close'});r=c.getresponse();body=r.read()
  return r.status,body,(time.perf_counter()-start)*1000,None
 except Exception as e:return 0,b'',(time.perf_counter()-start)*1000,type(e).__name__
 finally:c.close()
def norm(v):
 if isinstance(v,list):return [norm(x) for x in sorted(v,key=lambda x:x.get('id',0))]
 if isinstance(v,dict):
  return {k:datetime.datetime.fromisoformat(x.replace('Z','+00:00')).isoformat() if k in ['created_at','updated_at'] else str(x) if k=='commodity_code' else norm(x) for k,x in v.items()}
 return v
try:
 subprocess.run(['dotnet','build','api-csharp','-c','Release','--nologo'],cwd=ROOT,check=True,stdout=(TMP/'release-build.log').open('w'),stderr=subprocess.STDOUT)
 subprocess.run(['dotnet','ef','migrations','script','--project','api-csharp','--configuration','Release','--no-build','--output',str(TMP/'migrations.sql')],cwd=ROOT,check=True,stdout=subprocess.DEVNULL)
 sql('postgres','CREATE DATABASE '+DB);made=True;sql(DB,file=TMP/'migrations.sql');(OUT/'import.log').write_text(sql(DB,file=ROOT/'database/postgresql/import-legacy-json.sql'))
 shutil.copytree(ROOT/'data',TMP/'python-data')
 (TMP/'python-data/user.json').write_text(json.dumps([{'app':'review','api_key':KEY,'endpoint_access':{r:{'get':True} for r in ['warehouses','items']}}]))
 pyport,csport=freeport(),freeport()
 launch='''import os,sys,socketserver,json,platform
sys.path.insert(0,sys.argv[1])
from main import ApiRequestHandler
from providers import auth_provider,data_provider
from processors import notification_processor
auth_provider.ROOT_PATH=data_provider.ROOT_PATH=sys.argv[2]+os.sep
class Quiet(ApiRequestHandler):
 def log_message(self,*args):pass
with socketserver.TCPServer(('127.0.0.1',int(sys.argv[3])),Quiet) as server:
 open(sys.argv[4],'w').write(json.dumps({'version':platform.python_version(),'server':'socketserver.TCPServer','backlog':server.request_queue_size}))
 notification_processor.start()
 server.serve_forever()
'''
 env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Production','Swagger__Enabled':'false','ConnectionStrings__CargoHub':f'Host=127.0.0.1;Port=55447;Database={DB};Username=cargohub_review','Logging__LogLevel__Default':'None','Logging__LogLevel__Microsoft':'None'}
 for label,cmd,environment in [('Python',[sys.executable,'-u','-c',launch,str(ROOT/'api'),str(TMP/'python-data'),str(pyport),str(TMP/'python-meta.json')],os.environ.copy()),('C#',['dotnet',str(ROOT/'api-csharp/bin/Release/net10.0/CargoHub.Api.dll'),'--urls',f'http://127.0.0.1:{csport}'],env)]:
  log=(TMP/(label.replace('#','sharp')+'.log')).open('w');logs.append(log);processes.append(subprocess.Popen(cmd,cwd=ROOT/'api-csharp',env=environment,stdout=log,stderr=log))
 ports={'Python':pyport,'C#':csport}
 for label,port in ports.items():
  for i in range(100):
   if request(port,PATHS[0])[0]==200:break
   time.sleep(.1)
  else:raise RuntimeError('Server start failed: '+label)
 expected={};checks=[]
 for path in PATHS:
  a=request(pyport,path);b=request(csport,path);assert a[0]==b[0]==200
  assert norm(json.loads(a[1]))==norm(json.loads(b[1])),path
  for label,r in [('Python',a),('C#',b)]:expected[(label,path)]=hashlib.sha256(r[1]).hexdigest()
  checks.append({'endpoint':path,'equivalent_after_known_normalization':True,'python_bytes':len(a[1]),'csharp_bytes':len(b[1])})
  for port in ports.values():
   for _ in range(100):assert request(port,path)[0]==200
 print('Validated equivalent responses and completed 100 warm-ups for each endpoint/implementation.',flush=True)
 rng=random.Random(20261109)
 for path in PATHS:
  for clients in [1,10,25]:
   for repeat in range(1,4):
    order=list(ports);rng.shuffle(order)
    for label in order:
     def one(i):
      status,body,ms,error=request(ports[label],path)
      return {'implementation':label,'endpoint':path,'clients':clients,'round':repeat,'request':i,'status':status,'valid':status==200 and hashlib.sha256(body).hexdigest()==expected[(label,path)],'milliseconds':ms,'bytes':len(body),'error':error}
     start=time.perf_counter()
     with concurrent.futures.ThreadPoolExecutor(max_workers=clients) as pool:batch=list(pool.map(one,range(100)))
     seconds=time.perf_counter()-start;samples+=batch
     errors=sum(not r['valid'] for r in batch)
     runs.append({'implementation':label,'endpoint':path,'clients':clients,'round':repeat,'requests':100,'errors':errors,'seconds':seconds})
     print(f'{label:6} {path:38} clients={clients:2} round={repeat} duration={seconds:.3f}s errors={errors}',flush=True)
 summary=[]
 for path in PATHS:
  for clients in [1,10,25]:
   for label in ports:
    rows=[r for r in samples if (r['endpoint'],r['clients'],r['implementation'])==(path,clients,label)];times=sorted(r['milliseconds'] for r in rows if r['valid']);wall=sum(r['seconds'] for r in runs if (r['endpoint'],r['clients'],r['implementation'])==(path,clients,label))
    summary.append({'implementation':label,'endpoint':path,'clients':clients,'requests':len(rows),'errors':len(rows)-len(times),'median_ms':statistics.median(times) if times else None,'p95_ms':times[math.ceil(len(times)*.95)-1] if times else None,'rps':len(times)/wall})
 assert hashes=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'data').glob('*.json')}
 result={'date':'2026-11-09','git_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'environment':{'os':platform.platform(),'cpu':platform.machine(),'logical_cpus':os.cpu_count(),'python':json.loads((TMP/'python-meta.json').read_text()),'dotnet':subprocess.check_output(['dotnet','--version'],text=True).strip(),'postgresql':sql('postgres','SELECT version()').strip(),'build':'C# Release / Production'},'method':{'requests':100,'rounds':3,'warmups_per_endpoint_implementation':100,'clients':[1,10,25],'seed':20261109,'timeout_seconds':10,'new_tcp_connection':True,'request_logging':False,'python_checks_key_csharp_does_not':True,'latency_includes':'TCP connect through full body read, excluding hash verification','throughput_includes':'executor and client verification overhead','normalization':['record order','UTC notation','commodity_code string conversion'],'database':'isolated temporary import; 80,465 business records','source_json_unchanged':True},'response_checks':checks,'summary':summary,'runs':runs,'samples':samples}
 (OUT/'resultaten.json').write_text(json.dumps(result,indent=2))
 for name,rows in [('metingen.csv',samples),('samenvatting.csv',summary)]:
  with (OUT/name).open('w',newline='') as f:w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
 print(f'COMPLETE: {len(samples)} measured requests, {sum(not r["valid"] for r in samples)} errors, source JSON unchanged.',flush=True)
finally:
 for p in processes:
  p.terminate()
  try:p.wait(timeout=10)
  except subprocess.TimeoutExpired:p.kill();p.wait()
 for log in logs:log.close()
 if made:sql('postgres','DROP DATABASE '+DB)
 print('Owned benchmark processes and database cleaned up.',flush=True)
