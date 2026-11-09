"""Prepare or run an isolated CargoHub review fixture through HTTP only.

python3 documentatie/SprintReview/2026-11-09/demo_review.py --prepare
python3 documentatie/SprintReview/2026-11-09/demo_review.py --run
Use a new --demo-id (e.g. 900101) for another complete demonstration.
"""
import argparse,json,urllib.request,urllib.error
from pathlib import Path

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--base-url',default='http://localhost:5268');p.add_argument('--demo-id',type=int,default=900091)
 group=p.add_mutually_exclusive_group(required=True);group.add_argument('--prepare',action='store_true');group.add_argument('--run',action='store_true');args=p.parse_args()
 root=Path(__file__).resolve().parents[3];n=args.demo_id
 if n<900000:raise SystemExit('Use a demo ID >= 900000.')
 events=[]
 def req(method,path,body=None,expected=None):
  r=urllib.request.Request(args.base_url+'/api/v1'+path,method=method,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json'})
  try:
   with urllib.request.urlopen(r,timeout=20) as response:status,raw=response.status,response.read()
  except urllib.error.HTTPError as e:status,raw=e.code,e.read()
  if expected is not None and status!=expected:raise RuntimeError(f'{method} {path}: HTTP {status}, expected {expected}: {raw[:250]!r}')
  value=json.loads(raw) if raw else None
  events.append({'method':method,'path':path,'status':status})
  return status,value
 source=f'/inventories/{n}/{n}';target=f'/inventories/{n}/{n+1}';transfer=f'/transfers/{n}'
 if args.prepare:
  # Stop before writing if any ID belongs to an existing record. No resets/deletes.
  for r in ['warehouses','items','transfers']:
   status,value=req('GET',f'/{r}/{n}')
   if status not in (200,404) or (status==200 and value is not None):raise RuntimeError(f'ID {n} is already used in {r}; choose a new --demo-id.')
  for x in [n,n+1]:
   status,value=req('GET',f'/locations/{x}')
   if status not in (200,404) or (status==200 and value is not None):raise RuntimeError(f'Location {x} exists; choose a new --demo-id.')
  # Existing import supplies the referenced group/line/type/supplier.
  item=json.loads((root/'data/item.json').read_text())[0].copy()
  item.update(id=n,code='REVIEW-ITEM',description='Review testartikel',commodity_code=str(item['commodity_code']))
  item.pop('created_at',None);item.pop('updated_at',None)
  req('POST','/warehouses',{'id':n,'code':'REVIEW-WH','name':'Review magazijn','address':'Demostraat 1','city':'Rotterdam','zip_code':'3000AA','province':'Zuid-Holland','country':'NL','contact_name':'Review','contact_phone':'0100000000','contact_email':'review@example.com'},201)
  for location,name in [(n,'Bron'),(n+1,'Bestemming')]:req('POST','/locations',{'id':location,'warehouse_id':n,'code':f'REVIEW-{location}','name':name},201)
  req('POST','/items',item,201)
  for location,qty,allocated in [(n,100,20),(n+1,20,0)]:req('POST','/inventories',{'item_id':n,'location_id':location,'quantity_on_hand':qty,'quantity_expected':0,'quantity_ordered':0,'quantity_allocated':allocated},201)
  req('POST','/transfers',{'id':n,'reference':f'REVIEW-{n}','from_location_id':n,'to_location_id':n+1,'transfer_status':'Scheduled','items':[{'item_id':n,'amount':10}]},201)
  print(json.dumps({'prepared':True,'demo_id':n,'source':100,'allocated':20,'destination':20,'transfer':'Scheduled','events':events},indent=2));return
 status,header=req('GET',transfer,expected=200)
 if not header or header['reference']!=f'REVIEW-{n}' or header['transfer_status']!='Scheduled':raise RuntimeError('Prepare a new, unprocessed review fixture before --run.')
 def snapshot():
  a=req('GET',source,expected=200)[1];b=req('GET',target,expected=200)[1];t=req('GET',transfer,expected=200)[1]
  return {'source':a['quantity_on_hand'],'allocated':a['quantity_allocated'],'destination':b['quantity_on_hand'],'status':t['transfer_status']}
 before=snapshot();assert before=={'source':100,'allocated':20,'destination':20,'status':'Scheduled'},before
 req('PUT',transfer+'/commit',expected=200);after=snapshot();assert after=={'source':90,'allocated':20,'destination':30,'status':'Processed'},after
 req('PUT',transfer+'/commit',expected=200);repeated=snapshot();assert repeated==after
 # A second, own transfer requests 71 while only 70 are available.
 status,value=req('GET',f'/transfers/{n+1}',expected=200)
 if value is not None:raise RuntimeError('Next demo transfer ID already exists; cannot run negative case safely.')
 req('POST','/transfers',{'id':n+1,'reference':f'REVIEW-REJECT-{n}','from_location_id':n,'to_location_id':n+1,'transfer_status':'Scheduled','items':[{'item_id':n,'amount':71}]},201)
 req('PUT',f'/transfers/{n+1}/commit',expected=409);assert snapshot()==after
 req('DELETE',f'/transfers/{n+1}',expected=200)
 print(json.dumps({'demo_id':n,'before':before,'after':after,'repeated':repeated,'insufficient_stock_status':409,'total_stock_before':120,'total_stock_after':120,'events':events},indent=2))

if __name__=='__main__':main()
