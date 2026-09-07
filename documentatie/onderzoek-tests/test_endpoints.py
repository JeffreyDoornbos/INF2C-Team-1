import sys,json,tempfile,pathlib,threading,http.client,socketserver,copy
PROJECT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(PROJECT/'api'))
from main import ApiRequestHandler
from providers import data_provider as dp,auth_provider as ap
root=pathlib.Path(tempfile.mkdtemp(prefix='cargo-test-'))
source={p.stem:json.loads(p.read_text()) for p in (PROJECT/'data').glob('*.json')}
fixture={name:(data[:2] if name in ['order','shipment','transfer'] else data) for name,data in source.items()}
for child,parent in [('order_item','order'),('shipment_item','shipment'),('transfer_item','transfer')]:
 ids={row['id'] for row in fixture[parent]}
 fixture[child]=[row for row in source[child] if row[parent+'_id'] in ids]
for name,data in fixture.items():
 (root/(name+'.json')).write_text(json.dumps(data))
dp.ROOT_PATH=ap.ROOT_PATH=str(root)+'/'
resources=['warehouses','locations','transfers','items','item_lines','item_groups','item_types','inventories','suppliers','orders','clients','shipments']
key='temporary-local-test-key'
(root/'user.json').write_text(json.dumps([{'api_key':key,'app':'test','endpoint_access':{r:{m:True for m in ['get','post','put','delete']} for r in resources}},{'api_key':'denied','app':'test','endpoint_access':{}}]))
class Handler(ApiRequestHandler):
 def log_message(self,*args): pass
server=socketserver.TCPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
results=[]
def req(method,path,body=None,auth=key):
 c=http.client.HTTPConnection('127.0.0.1',server.server_address[1],timeout=5)
 headers={'API_KEY':auth} if auth else {}
 if body is not None: headers['Content-Type']='application/json'
 c.request(method,'/api/v1'+path,body=json.dumps(body) if body is not None else None,headers=headers)
 r=c.getresponse(); raw=r.read();status=r.status;c.close()
 try: val=json.loads(raw)
 except: val=raw.decode()
 results.append({'method':method,'path':path,'status':status,'response':val})
 return status,val
mapping=dict(zip(resources,['warehouse','location','transfer','item','item_line','item_group','item_type','inventory','supplier','order','client','shipment']))
for r,name in mapping.items():
 first=source[name][0];id=first.get('id',1)
 req('GET','/'+r);req('GET',f'/{r}/{id}')
 extra=copy.deepcopy(first);extra['id']=999999
 if r in ['orders','transfers','shipments']:extra['items']=[]
 before=(root/(name+'.json')).read_text(); req('POST','/'+r,extra);after=(root/(name+'.json')).read_text()
 results[-1]['data_changed']=json.loads(before)!=json.loads(after)
 altered=copy.deepcopy(first);altered['reference']='REPORT_TEST';req('PUT',f'/{r}/{id}',altered)
 results[-1]['data_changed']=json.loads(after)!=json.loads((root/(name+'.json')).read_text())
 req('DELETE',f'/{r}/{id}');results[-1]['record_removed']=not any(x.get('id')==id for x in json.loads((root/(name+'.json')).read_text())) if r!='inventories' else False
for path in ['/warehouses/1/locations','/transfers/1/items','/items/1/inventory','/items/1/inventory/totals','/item_lines/1/items','/item_groups/1/items','/item_types/1/items','/suppliers/1/items','/orders/1/items','/clients/1/orders','/shipments/1/orders','/shipments/1/items']:
 req('GET',path)
for path,body in [('/orders/1/items',[]),('/shipments/1/items',[]),('/shipments/1/orders',[2]),('/transfers/1/commit',None)]:
 before={f.name:json.loads(f.read_text()) for f in root.glob('*.json')}
 req('PUT',path,body)
 results[-1]['any_data_changed']=before!={f.name:json.loads(f.read_text()) for f in root.glob('*.json')}
req('GET','/warehouses',auth=None);req('GET','/warehouses',auth='denied');req('GET','/warehouses/999999');req('GET','/warehouses/not-an-id');req('GET','/unknown')
# Extra controles op JSON-vorm, timestamps en opnieuw lezen.
warehouse=copy.deepcopy(source['warehouse'][0]);warehouse['id']=999999
warehouse.pop('created_at',None);warehouse.pop('updated_at',None)
for label,payload in [('object zonder timestamps',warehouse),('array met object',[warehouse]),('JSON-string','voorbeeld'),('lege body',None)]:
 req('POST','/warehouses',payload);results[-1]['scenario']=label
req('GET','/warehouses/999999');results[-1]['scenario']='readback na POST: ontbreekt'
req('GET','/warehouses/1');results[-1]['scenario']='readback na PUT en DELETE: origineel bestaat'
for path in ['/orders/1/items','/shipments/1/items','/shipments/1/orders','/transfers/1','/items/1/inventory/totals']:
 req('GET',path);results[-1]['scenario']='readback na aanvullende PUT'
# Modelcontrole: server genereert timestamps, maar provider laadt bij save opnieuw.
model=dp.fetch_warehouse_pool();obj=copy.deepcopy(warehouse);model.add_warehouse(obj)
model_checks={'timestamps_generated':all(isinstance(obj.get(k),str) and obj[k].endswith('Z') for k in ['created_at','updated_at']), 'fresh_instance_contains_added_record': dp.fetch_warehouse_pool().get_warehouse(999999) is not None}
# Vergelijk GET-inhoud onafhankelijk met de tijdelijke JSON-fixture.
def expanded(name, row):
 value=copy.deepcopy(row)
 if name in ['order','shipment','transfer']:
  value['items']=[{'item_id':r['item_id'],'amount':r['amount']} for r in fixture[name+'_item'] if r[name+'_id']==row['id']]
 return value
read_checks=[]
for entry in results[:76]:
 if entry['method']!='GET' or entry['status']!=200:continue
 bits=entry['path'].strip('/').split('/');res=bits[0];name=mapping[res]
 if len(bits)==1:expected=[expanded(name,r) for r in fixture[name]]
 elif len(bits)==2:expected=next(expanded(name,r) for r in fixture[name] if r['id']==int(bits[1]))
 else:
  rid=int(bits[1]);tail='/'.join(bits[2:])
  if res=='warehouses':expected=[r for r in fixture['location'] if r['warehouse_id']==rid]
  elif res in ['orders','transfers','shipments'] and tail=='items':expected=expanded(name,next(r for r in fixture[name] if r['id']==rid))['items']
  elif res=='shipments':
   order=next(r for r in fixture['shipment'] if r['id']==rid).get('order_id');expected=[order] if order is not None else []
  elif res=='clients':expected=[expanded('order',r) for r in fixture['order'] if rid in [r.get('client_id'),r.get('ship_to_client_id'),r.get('bill_to_client_id')]]
  elif res in ['item_lines','item_groups','item_types']:expected=[r['id'] for r in fixture['item'] if r[name+'_id']==rid]
  elif res=='suppliers':expected=[r for r in fixture['item'] if r['supplier_id']==rid]
  elif tail=='inventory':expected=[r for r in fixture['inventory'] if r['item_id']==rid]
  else:
   rows=[r for r in fixture['inventory'] if r['item_id']==rid]
   expected={k:sum(r[f] for r in rows) for k,f in [('total_expected','quantity_expected'),('total_ordered','quantity_ordered'),('total_allocated','quantity_allocated')]}
   expected['total_available']=sum(r['quantity_on_hand']-r['quantity_allocated'] for r in rows)
 read_checks.append({'path':entry['path'],'matches_fixture':entry['response']==expected})
server.shutdown();server.server_close()
import shutil
shutil.rmtree(root)
output=pathlib.Path(__file__).with_name('resultaten.json')
output.write_text(json.dumps({'http_count':len(results),'model_checks':model_checks,'read_checks':read_checks,'requests':results},indent=2))
print(json.dumps({'http_count':len(results),'model_checks':model_checks,'read_checks_passed':sum(r['matches_fixture'] for r in read_checks),'read_checks_total':len(read_checks),'additional_put_data_changed':[r.get('any_data_changed') for r in results if 'any_data_changed' in r]},indent=2))
