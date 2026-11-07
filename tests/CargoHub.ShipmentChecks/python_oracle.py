"""Execute the ORIGINAL Python models with isolated, stable provider instances.

This tests intended model calculations, not the old handler's lost-save bug.
Never reads or writes data/*.json. Only stdout contains synthetic test fixtures.
"""
import copy
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'api'))
from providers import data_provider
from models.shipments import Shipments
from models.shipment_items import ShipmentItems
from models.inventories import Inventories

NOW='2026-11-02T10:00:00Z'
def header(id,order=1):
    return dict(id=id,reference=f'S{id}',order_id=order,shipment_date='2026-11-02T12:34:56Z',
                shipment_type='Standard',shipment_status='Scheduled',carrier_name='Test Carrier',
                shipping_method='Road',payment_type='Paid',created_at=NOW,updated_at=NOW)
def inventory(item,location,on_hand,ordered):
    return dict(item_id=item,location_id=location,quantity_on_hand=on_hand,quantity_ordered=ordered,
                quantity_expected=0,quantity_allocated=0,created_at=NOW,updated_at=NOW)

shipments=Shipments('',True);shipments.data=[header(1),header(2,None)]
lines=ShipmentItems('',True);lines.data=[dict(id=1,shipment_id=1,item_id=1,amount=5),dict(id=2,shipment_id=1,item_id=2,amount=3)]
stock=Inventories('',True);stock.data=[inventory(1,1,10,5),inventory(1,2,100,20),inventory(2,1,50,2),inventory(4,1,30,1),inventory(4,2,30,9)]
shipments.get_timestamp=lambda:NOW
stock.get_timestamp=lambda:NOW
data_provider.fetch_shipment_item_pool=lambda:lines
data_provider.fetch_inventory_pool=lambda:stock
initial=dict(shipments=copy.deepcopy(shipments.data),lines=copy.deepcopy(lines.data),inventory=copy.deepcopy(stock.data))

def snapshot():
    return dict(shipments=copy.deepcopy(shipments.get_shipments()),inventory=copy.deepcopy(stock.data))
operations=[]
def action(name,method,path,body,run):
    run()
    operations.append(dict(name=name,method=method,path=path,body=body,expected=snapshot()))

start=snapshot()
new=header(3);new['items']=[dict(item_id=1,amount=8)]
action('POST stores lines without an ordered-stock delta','POST','/api/v1/shipments',copy.deepcopy(new),lambda:shipments.add_shipment(copy.deepcopy(new)))
updated=[dict(item_id=1,amount=9),dict(item_id=3,amount=4),dict(item_id=4,amount=2)]
action('PUT items applies increases, removals, clamping and skips missing inventory','PUT','/api/v1/shipments/1/items',updated,lambda:shipments.update_items_in_shipment(1,updated))
action('Identical PUT is idempotent for ordered-stock deltas','PUT','/api/v1/shipments/1/items',updated,lambda:shipments.update_items_in_shipment(1,updated))
duplicates=[dict(item_id=1,amount=2),dict(item_id=1,amount=6)]
action('Duplicate item lines retain Python last-value delta behavior','PUT','/api/v1/shipments/1/items',duplicates,lambda:shipments.update_items_in_shipment(1,duplicates))
action('Empty items removes all lines with the existing negative delta','PUT','/api/v1/shipments/1/items',[],lambda:shipments.update_items_in_shipment(1,[]))
full=header(3);full['reference']='Changed';full['items']=[dict(item_id=1,amount=10)]
action('Full PUT updates header and items in one operation','PUT','/api/v1/shipments/3',copy.deepcopy(full),lambda:shipments.update_shipment(3,copy.deepcopy(full)))
no_items=header(3);no_items['reference']='Header only'
action('PUT without items preserves existing shipment lines','PUT','/api/v1/shipments/3',copy.deepcopy(no_items),lambda:shipments.update_shipment(3,copy.deepcopy(no_items)))
null_items={**header(3),'items':None}
action('PUT with null items preserves existing shipment lines','PUT','/api/v1/shipments/3',copy.deepcopy(null_items),lambda:shipments.update_shipment(3,copy.deepcopy(null_items)))
action('Order lists keep the first ID for legacy v1 compatibility','PUT','/api/v1/shipments/3/orders',[2,1],lambda:shipments.update_orders_in_shipment(3,[2,1]))
action('Empty order list clears the nullable link','PUT','/api/v1/shipments/3/orders',[],lambda:shipments.update_orders_in_shipment(3,[]))
action('Delete removes header and lines without reversing ordered stock','DELETE','/api/v1/shipments/3',None,lambda:shipments.remove_shipment(3))
print(json.dumps(dict(initial=initial,start=start,operations=operations)))
