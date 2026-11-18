'use strict';
const $ = id => document.getElementById(id);
let csrf = '', busy = false, lastSnapshot = null;
const history = [];
const actions = {start:'Starten',stop:'Stoppen',restart:'Herstarten'};
function bytes(n){return (n/1024**3).toLocaleString('nl-NL',{maximumFractionDigits:1})+' GB';}
function duration(s){s=Math.max(0,Math.floor(s||0));const days=Math.floor(s/86400),hours=Math.floor(s%86400/3600),minutes=Math.floor(s%3600/60);return days?`${days}d ${hours}u`:hours?`${hours}u ${minutes}m`:`${minutes}m ${s%60}s`;}
function notice(message){$('notice').textContent=message;$('notice').hidden=!message;}
function showLogin(){ $('dashboard').hidden=true;$('login').hidden=false;csrf=''; }
async function request(path,body){
  const r=await fetch('/beheer/api/'+path,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json','X-CSRF-Token':csrf}:{},body:body?JSON.stringify(body):undefined,credentials:'same-origin',cache:'no-store'});
  const data=await r.json();if(!r.ok){if(r.status===401&&path!=='login')showLogin();throw new Error(data.error||'Aanvraag mislukt.');}return data;
}
$('login-form').addEventListener('submit',async e=>{e.preventDefault();const button=e.target.querySelector('button');button.disabled=true;$('login-error').textContent='';try{await request('login',{username:$('username').value,password:$('password').value});$('password').value='';await refresh();}catch(error){$('login-error').textContent=error.message;}finally{button.disabled=false;}});
$('logout').addEventListener('click',async()=>{try{await request('logout',{});showLogin();history.length=0;}catch(error){notice(error.message);}});
function el(tag,cls,text){const node=document.createElement(tag);if(cls)node.className=cls;if(text!==undefined)node.textContent=text;return node;}
function serviceCard({key,title,subtitle,icon,iconClass='',status,control,stats=true}){
  const s=status||{running:false,status:'unknown'};
  const card=el('article','service'),top=el('div','service-title');
  top.append(el('div','service-icon '+iconClass,icon));const heading=el('div');heading.append(el('h3','',title),el('div','service-sub',subtitle));top.append(heading);
  card.append(top,el('span','badge'+(s.running?'':' off'),s.running?'Actief':s.status==='unknown'?'Onbekend':'Gestopt'));
  const details=el('div','service-details');
  for(const [name,value] of [['Uptime',s.running?duration(s.uptime):'—'],['CPU',stats?(s.cpu||'—'):'—'],['Geheugen',stats?(s.memory||'—'):'—']]){const item=el('div','detail');item.append(el('span','',name),document.createTextNode(value));details.append(item);}card.append(details);
  if(control){const bar=el('div','service-actions');for(const action of ['start','restart','stop']){const button=el('button',action==='stop'?'stop':'',actions[action]);button.disabled=busy||(action==='start'?s.running:!s.running);button.addEventListener('click',()=>confirmAction(key,action,title));bar.append(button);}card.append(bar);}else{card.append(el('div','service-readonly','Alleen statusweergave'));}return card;
}
function renderServices(data){
  const c=data.containers||{},slot=data.deployment.active_slot;
  const active=c['api-'+slot];
  const cards=[{key:'v2',title:'CargoHub API V2',subtitle:`C# / .NET · Actief slot: ${slot}`,icon:'.NET',status:active,control:true},
    {key:'v1',title:'CargoHub API V1',subtitle:'Python · Bestaande API',icon:'Py',iconClass:'python',status:data.python,control:true,stats:false},
    {title:'PostgreSQL',subtitle:'Database · Alleen intern bereikbaar',icon:'PG',iconClass:'db',status:c.db},
    {title:'Nginx',subtitle:'Reverse proxy · Routing V1 en V2',icon:'N',iconClass:'nginx',status:c.proxy},
    {title:'GitHub Actions-runner',subtitle:'cargohub-vps · Deployments',icon:'GH',iconClass:'runner',status:data.runner,stats:false},
    {title:'V2 reserveslot',subtitle:`C# / .NET · Slot: ${slot==='blue'?'green':'blue'}`,icon:'.NET',status:c['api-'+(slot==='blue'?'green':'blue')]}];
  $('services').replaceChildren(...cards.map(serviceCard));
}
function chart(){const canvas=$('cpu-chart'),ctx=canvas.getContext('2d'),w=canvas.width,h=canvas.height;ctx.clearRect(0,0,w,h);if(history.length<2)return;const x=i=>i/(Math.max(history.length,30)-1)*w;ctx.beginPath();ctx.moveTo(0,h);history.forEach((v,i)=>ctx.lineTo(x(i),h-8-v/100*(h-15)));ctx.lineTo(x(history.length-1),h);ctx.closePath();ctx.fillStyle='#c9183c12';ctx.fill();ctx.beginPath();history.forEach((v,i)=>i?ctx.lineTo(x(i),h-8-v/100*(h-15)):ctx.moveTo(x(i),h-8-v/100*(h-15)));ctx.strokeStyle='#c9183c';ctx.lineWidth=3;ctx.stroke();}
async function refresh(){
  if(busy)return;
  try{const data=await request('status');csrf=data.csrf;$('login').hidden=true;$('dashboard').hidden=false;$('username-label').textContent=data.username;
    if(data.loading){$('connection').textContent='Eerste meting ophalen';return;}
    lastSnapshot=data;const h=data.host;const stale=Date.now()/1000-data.sampled_at>20;
    $('connection').textContent=stale?'Meting verouderd':'Live verbonden';$('updated').textContent='Bijgewerkt om '+new Date(data.sampled_at*1000).toLocaleTimeString('nl-NL');
    $('release').textContent='V'+data.deployment.version+' · '+data.deployment.active_slot;
    $('cpu').textContent=h.cpu.toLocaleString('nl-NL')+'%';$('cpu-note').textContent=h.cpu_count+' logische CPU’s · verloop tijdens deze sessie';
    const mem=Math.round(h.memory_used/h.memory_total*100),disk=Math.round(h.disk_used/h.disk_total*100);
    $('memory').textContent=mem+'%';$('memory-bar').style.width=mem+'%';$('memory-note').textContent=bytes(h.memory_used)+' van '+bytes(h.memory_total);
    $('disk').textContent=disk+'%';$('disk-bar').style.width=disk+'%';$('disk-note').textContent=bytes(h.disk_used)+' van '+bytes(h.disk_total);
    $('uptime').textContent=duration(h.uptime);$('load').textContent='Load 1 / 5 / 15 min: '+h.load.map(n=>n.toFixed(2)).join(' / ');
    history.push(h.cpu);if(history.length>60)history.shift();chart();renderServices(data);
    $('deployment-note').textContent='Laatste uitrol: '+new Date(data.deployment.deployed_at).toLocaleString('nl-NL');
    if(data.error||stale)notice(data.error||'De laatste meting is ouder dan 20 seconden.');
  }catch(error){if(!$('dashboard').hidden){$('connection').textContent='Verbinding onderbroken';notice(error.message);}}
}
async function confirmAction(service,action,title){
  const dialog=$('confirm-dialog');$('confirm-title').textContent=title+' '+actions[action].toLowerCase()+'?';
  $('confirm-description').textContent=action==='stop'?'De API wordt onbereikbaar totdat je deze weer start. Dit beïnvloedt iedereen die de API gebruikt.':action==='restart'?'De API is tijdens het herstarten kort niet bereikbaar. De opgeslagen gegevens blijven behouden.':'De applicatie wordt gestart met de bestaande configuratie.';
  $('confirm-action').textContent=actions[action];dialog.returnValue='';dialog.showModal();
  const confirmed=await new Promise(resolve=>dialog.addEventListener('close',()=>resolve(dialog.returnValue==='confirm'),{once:true}));
  if(!confirmed)return;busy=true;renderServices(lastSnapshot);notice('Beheeractie wordt uitgevoerd…');
  try{const data=await request('action',{service,action});notice(data.message+' De status wordt binnen enkele seconden bijgewerkt.');}catch(error){notice(error.message);}finally{busy=false;setTimeout(refresh,1200);}
}
refresh();setInterval(refresh,5000);
