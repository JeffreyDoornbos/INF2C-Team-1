"""Original Python models AND commit handler, using synthetic stable providers.

Provider lifetime and save methods are replaced only within this process, so
lost saves and real data/*.json writes cannot contaminate the comparison.
"""
import copy
import io
import json
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'api'))
from providers import data_provider,auth_provider
from models.transfers import Transfers
from models.transfer_items import TransferItems
from models.inventories import Inventories
from processors import notification_processor
from main import ApiRequestHandler

NOW='2026-11-07T10:00:00Z'
def header(id):
    return dict(id=id,reference=f'T{id}',from_location_id=1,to_location_id=2,
                transfer_status='Scheduled',created_at=NOW,updated_at=NOW)
def stock(item,location,amount,allocated=0):
    return dict(item_id=item,location_id=location,quantity_on_hand=amount,
                quantity_expected=2,quantity_ordered=3,quantity_allocated=allocated,
                created_at=NOW,updated_at=NOW)
transfers=Transfers('',True);transfers.data=[header(1),header(2)]
lines=TransferItems('',True);lines.data=[dict(id=1,transfer_id=1,item_id=1,amount=10),
                                      dict(id=2,transfer_id=1,item_id=2,amount=5),
                                      dict(id=3,transfer_id=2,item_id=1,amount=2)]
inventory=Inventories('',True);inventory.data=[stock(1,1,100,20),stock(1,2,20),stock(2,1,50)]
for pool in (transfers,inventory):pool.get_timestamp=lambda:NOW;pool.save=lambda:None
transfers.save=lambda:None
data_provider.fetch_transfer_pool=lambda:transfers
data_provider.fetch_transfer_item_pool=lambda:lines
data_provider.fetch_inventory_pool=lambda:inventory
notifications=[]
notification_processor.push=lambda message:notifications.append(message)
auth_provider.has_access=lambda *args:True
initial=dict(transfers=copy.deepcopy(transfers.data),lines=copy.deepcopy(lines.data),inventory=copy.deepcopy(inventory.data))
def snapshot():return dict(transfers=copy.deepcopy(transfers.get_transfers()),inventory=copy.deepcopy(inventory.data))
operations=[]
def action(name,method,path,body,run):
    run();operations.append(dict(name=name,method=method,path=path,body=body,expected=snapshot()))
start=snapshot()
new=header(3);new['transfer_status']='Processed';new['items']=[dict(item_id=1,amount=8)]
action('POST forces Scheduled and stores lines without stock movement','POST','/api/v1/transfers',copy.deepcopy(new),lambda:transfers.add_transfer(copy.deepcopy(new)))
updated=header(3);updated['reference']='Changed';updated['items']=[dict(item_id=2,amount=5)]
action('PUT replaces only the selected transfer item rows','PUT','/api/v1/transfers/3',copy.deepcopy(updated),lambda:transfers.update_transfer(3,copy.deepcopy(updated)))
no_items=header(3);no_items['reference']='Header only'
action('PUT without items preserves item rows','PUT','/api/v1/transfers/3',copy.deepcopy(no_items),lambda:transfers.update_transfer(3,copy.deepcopy(no_items)))
null_items={**header(3),'items':None}
action('PUT with null items preserves item rows','PUT','/api/v1/transfers/3',copy.deepcopy(null_items),lambda:transfers.update_transfer(3,copy.deepcopy(null_items)))
action('DELETE removes header and its own item rows without stock movement','DELETE','/api/v1/transfers/3',None,lambda:transfers.remove_transfer(3))
def commit():
    handler=object.__new__(ApiRequestHandler)
    status=[];handler.send_response=lambda value:status.append(value)
    handler.end_headers=lambda:None
    handler.handle_put_version_1(['transfers','1','commit'],{})
    assert status==[200]
action('Original Python commit decreases source, increases/creates destination and sets Processed','PUT','/api/v1/transfers/1/commit',None,commit)
print(json.dumps(dict(initial=initial,start=start,operations=operations)))
