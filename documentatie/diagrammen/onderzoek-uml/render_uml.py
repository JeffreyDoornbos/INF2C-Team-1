from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
import math
OUT=Path(__file__).resolve().parent
FONT='/System/Library/Fonts/Supplemental/Arial.ttf'
BOLD='/System/Library/Fonts/Supplemental/Arial Bold.ttf'
def start(h=980):
 global im,d
 im=Image.new('RGB',(1600,h),'white');d=ImageDraw.Draw(im)
def text(x,y,s,size=26,bold=False,anchor='mm'):
 d.multiline_text((x,y),s,font=ImageFont.truetype(BOLD if bold else FONT,size),fill='#163647',anchor=anchor,align='center',spacing=8)
def box(x,y,w,h,title,attrs):
 d.rectangle((x,y,x+w,y+h),fill='#f4f6f8',outline='#526675',width=3);text(x+w/2,y+28,title,28,True);d.line((x,y+55,x+w,y+55),fill='#526675',width=2);text(x+w/2,y+60+(h-60)/2,attrs,24)
def line(points,a='1',b='0..*'):
 d.line(points,fill='#526675',width=3);
 for endpoint,near,label in [(points[0],points[1],a),(points[-1],points[-2],b)]:
  x,y=endpoint;dx,dy=near[0]-x,near[1]-y;n=math.hypot(dx,dy);ux,uy=dx/n,dy/n
  text(x+ux*42-uy*25,y+uy*42+ux*25,label,23)
def save(n):im.save(OUT/n)
start(900)
box(30,40,330,170,'Warehouse','id : int');box(635,40,330,170,'Location','id : int\nwarehouse_id : int');box(1220,40,350,200,'Inventory','item_id : int\nlocation_id : int\nquantity_on_hand : int')
line([(360,120),(635,120)]);line([(965,120),(1220,120)])
box(635,360,330,190,'Item','id : int\nsupplier_id : int\ncategorie-ID’s : int');box(30,360,330,150,'Supplier','id : int');line([(360,430),(635,430)])
line([(965,445),(1395,445),(1395,240)])
for x,title in [(30,'ItemLine'),(635,'ItemGroup'),(1240,'ItemType')]:box(x,700,330,130,title,'id : int')
line([(195,700),(195,610),(700,610),(700,550)]);line([(800,700),(800,550)]);line([(1405,700),(1405,610),(900,610),(900,550)])
text(800,875,'UML-klassenmodel • sleutel Inventory: (item_id, location_id)',24)
save('erd1.png')
start(1020)
box(30,30,330,150,'Client','id : int');box(635,30,330,190,'Order','id : int\nclient_id : int\nwarehouse_id : int');box(1240,30,330,150,'Warehouse','id : int');line([(360,105),(635,105)]);line([(1240,105),(965,105)])
box(30,345,330,160,'Shipment','id : int\norder_id : int [0..1]');line([(635,175),(470,175),(470,425),(360,425)],'0..1','0..*')
box(635,345,330,180,'OrderItem','id, order_id, item_id : int\namount : int\nunit_price : number');line([(800,220),(800,345)])
box(30,690,330,150,'ShipmentItem','id, shipment_id, item_id\namount : int');line([(195,505),(195,690)])
box(635,690,330,150,'Item','id : int');line([(635,765),(360,765)]);line([(800,690),(800,525)])
box(1240,345,330,160,'Transfer','id : int\nfrom_location_id : int\nto_location_id : int');box(1240,690,330,150,'TransferItem','id, transfer_id, item_id\namount : int');line([(1405,505),(1405,690)]);line([(965,765),(1240,765)])
text(800,920,'Order verwijst ook naar Client als aflever- en factuuradres.\nTransfer heeft twee relaties met Location: bron en bestemming (beide 1).\nUser (api_key, app, endpoint_access) staat los van deze relaties.',25)
save('erd2.png')
start(1070)
xs=[160,565,985,1420]
for x,name in zip(xs,['Client','API-handler','auth_provider','Model / JSON']):
 d.rectangle((x-145,20,x+145,85),outline='#526675',width=3);text(x,53,name,26,True)
 for y in range(86,1040,22):d.line((x,y,x,y+11),fill='#84919a',width=2)
def arrow(a,b,y,label,dashed=False):
 x,z=xs[a],xs[b]
 if dashed:
  for k in range(int(min(x,z)),int(max(x,z)),22):d.line((k,y,min(k+11,max(x,z)),y),fill='#163647',width=2)
 else:d.line((x,y,z,y),fill='#163647',width=3)
 sign=1 if z>x else -1;d.line((z-sign*15,y-9,z,y,z-sign*15,y+9),fill='#163647',width=3);text((x+z)/2,y-22,label,23)
arrow(0,1,145,'GET /items/1 + API_KEY');arrow(1,2,225,'get_user(key)');arrow(2,1,290,'user of None',True)
d.rectangle((20,335,1580,1020),outline='#526675',width=2);text(55,355,'alt',24,True)
text(310,385,'[ongeldige sleutel]',23);arrow(1,0,450,'401',True)
d.line((20,485,1580,485),fill='#526675',width=2);text(300,512,'[geldige sleutel]',23)
arrow(1,2,575,'has_access(user, items, get)');arrow(2,1,640,'toegestaan?',True)
d.rectangle((70,677,1535,997),outline='#526675',width=2);text(105,695,'alt',22,True);text(295,720,'[geen rechten]',22);arrow(1,0,770,'403',True)
d.line((70,799,1535,799),fill='#526675',width=2);text(295,820,'[wel rechten]',22);arrow(1,3,873,'get_item(1)');arrow(3,1,929,'item of None',True);arrow(1,0,980,'200 + JSON',True)
save('sequence.png')
start(830)
d.rectangle((480,20,1570,795),outline='#526675',width=3);text(1025,55,'CargoHub WMS',30,True)
roles=[('Magazijnmedewerker',140,175),('Orderpicker',140,395),('Verzendmedewerker',140,615)]
for name,x,y in roles:
 d.ellipse((x-22,y-58,x+22,y-14),outline='#163647',width=3);d.line((x,y-14,x,y+45),fill='#163647',width=3);d.line((x-45,y+5,x+45,y+5),fill='#163647',width=3);d.line((x-40,y+95,x,y+45,x+40,y+95),fill='#163647',width=3);text(x+85,y+125,name,25)
for y,name in [(160,'Voorraad raadplegen'),(355,'Transfer verwerken'),(550,'Orderregels bekijken'),(735,'Zending beheren')]:
 d.ellipse((700,y-48,1410,y+48),outline='#526675',width=3);text(1055,y,name,28)
for a,b in [(175,160),(175,355),(395,160),(395,550),(615,550),(615,735)]:d.line((185,a+5,700,b),fill='#526675',width=2)
save('usecase.png')
