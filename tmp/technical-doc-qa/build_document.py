from pathlib import Path
import math, json
from PIL import Image,ImageDraw,ImageFont
from docx import Document
from docx.shared import Inches,Pt,RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT=Path('/Users/jeffreydoornbos/INF2C-Team-1')
TMP=ROOT/'tmp/technical-doc-qa'
OUT=ROOT/'documentatie/Technische-documentatie'
OUT.mkdir(parents=True,exist_ok=True)

# UML sequence diagram of the transaction implemented in TransferService.
im=Image.new('RGB',(1450,1150),'white');dr=ImageDraw.Draw(im)
def f(n,b=False):return ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial'+(' Bold' if b else '')+'.ttf',n)
def st(x,y,t,n=25,c='#243247',b=False):dr.text((x,y),t,font=f(n,b),fill=c)
xs=[130,410,715,1020,1320]
names=['Bruno','Nginx','Transfers\nController','Transfer\nService','PostgreSQL']
for x,n in zip(xs,names):
    dr.rounded_rectangle((x-115,20,x+115,115),radius=10,fill='#eef1f6',outline='#a9b5c5',width=2)
    for j,line in enumerate(n.split('\n')):
        w=dr.textbbox((0,0),line,font=f(25,True))[2];st(x-w/2,40+j*30,line,25,b=True)
    for y in range(115,1130,18):dr.line((x,y,x,y+9),fill='#b3becd',width=2)
def msg(a,b,y,t,ret=False):
    x1,x2=xs[a],xs[b]
    if ret:
        for x in range(min(x1,x2),max(x1,x2),18):dr.line((x,y,min(x+10,max(x1,x2)),y),fill='#64748b',width=2)
    else:dr.line((x1,y,x2,y),fill='#243247',width=3)
    s=1 if x2>x1 else -1
    dr.polygon([(x2,y),(x2-s*14,y-7),(x2-s*14,y+7)],fill='#243247')
    w=dr.textbbox((0,0),t,font=f(23))[2];st((x1+x2-w)/2,y-32,t,23)
msg(0,1,185,'PUT /api/v2/transfers/{id}/commit')
msg(1,2,255,'Doorsturen naar actieve API')
msg(2,3,325,'CommitTransferAsync(id)')
msg(3,4,395,'BEGIN + transfer vergrendelen')
msg(4,3,465,'Transfer en huidige status',True)
dr.rectangle((890,495,1440,1085),outline='#8795a9',width=2)
dr.rectangle((890,495,950,531),fill='#eef1f6',outline='#8795a9',width=2);st(900,500,'alt',23,b=True)
st(965,503,'[status is Processed]',24,b=True)
st(945,550,'Geen tweede voorraadwijziging',23)
dr.line((890,605,1440,605),fill='#8795a9',width=2)
st(915,620,'[status is Scheduled]',24,b=True)
msg(3,4,715,'Voorraad locken en controleren')
msg(4,3,785,'Beschikbare voorraad',True)
msg(3,4,875,'Voorraad wijzigen + status Processed')
msg(3,4,965,'SaveChanges + COMMIT')
st(930,1020,'Bij fout: transactie terugdraaien',23)
msg(3,2,1110,'200 bij succes',True)
im.save(TMP/'uml_sequence.png')

doc=Document();sec=doc.sections[0]
sec.page_width=Inches(8.5);sec.page_height=Inches(11)
sec.top_margin=Inches(.65);sec.bottom_margin=Inches(.6)
sec.left_margin=Inches(.7);sec.right_margin=Inches(.7)
sec.header_distance=Inches(.25);sec.footer_distance=Inches(.27)
normal=doc.styles['Normal'];normal.font.name='Arial';normal.font.size=Pt(10.5)
normal.font.color.rgb=RGBColor.from_string('243247')
normal.paragraph_format.space_after=Pt(7);normal.paragraph_format.line_spacing=1.12
for name,size in [('Title',32),('Subtitle',15),('Heading 1',21),('Heading 2',13),('Heading 3',11)]:
    s=doc.styles[name];s.font.name='Arial';s.font.size=Pt(size);s.font.color.rgb=RGBColor(0,0,0)
    s.font.bold=name!='Subtitle';s.paragraph_format.space_after=Pt(10)
    if name.startswith('Heading'):s.paragraph_format.space_before=Pt(11)
for name in ['List Bullet','List Number']:
    doc.styles[name].font.name='Arial';doc.styles[name].font.size=Pt(10.5)
    doc.styles[name].paragraph_format.space_after=Pt(5)
sec.different_first_page_header_footer=True
header=sec.header.paragraphs[0];header.text='CARGOHUB  |  TECHNISCHE DOCUMENTATIE';header.style='Caption'
header.runs[0].font.color.rgb=RGBColor(0,0,0);header.runs[0].font.size=Pt(8)
p=sec.footer.paragraphs[0];p.alignment=WD_ALIGN_PARAGRAPH.RIGHT
r=p.add_run('Groep 1  |  ');r.font.size=Pt(8)
field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');p._p.append(field)
doc.core_properties.title='Technische documentatie CargoHub'
doc.core_properties.subject='Overdracht en onboarding van ontwikkelaars'
doc.core_properties.author='CargoHub groep 1'
doc.core_properties.keywords='CargoHub, UML, ERD, Swagger, GitHub Actions, Docker Compose'

def p(t='',style=None):return doc.add_paragraph(t,style)
def h(t,level=2):return doc.add_heading(t,level)
def page(t):
    z=h(t,1);z.paragraph_format.page_break_before=True
def bullet(t):return p(t,'List Bullet')
def code(t):
    z=p();z.paragraph_format.space_before=Pt(3);z.paragraph_format.space_after=Pt(8)
    for i,line in enumerate(t.splitlines()):
        r=z.add_run(('\n' if i else '')+line);r.font.name='Courier New';r.font.size=Pt(8.4);r.font.color.rgb=RGBColor.from_string('243247')
    return z
def table(headers,rows,widths=None):
    t=doc.add_table(rows=1, cols=len(headers));t.autofit=False
    if widths:
        for c,w in zip(t.columns,widths):c.width=Inches(w)
    for c,s in zip(t.rows[0].cells,headers):c.text=s
    for row in rows:
        for c,s in zip(t.add_row().cells,row):c.text=str(s)
    for i,row in enumerate(t.rows):
        trPr=row._tr.get_or_add_trPr();cant=OxmlElement('w:cantSplit');trPr.append(cant)
        for j,c in enumerate(row.cells):
            if widths:c.width=Inches(widths[j])
            c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            tcPr=c._tc.get_or_add_tcPr();sh=OxmlElement('w:shd');sh.set(qn('w:fill'),'172338' if i==0 else ('F0F3F7' if i%2==0 else 'FFFFFF'));tcPr.append(sh)
            bd=OxmlElement('w:tcBorders')
            for edge in ['top','left','bottom','right']:
                e=OxmlElement('w:'+edge);e.set(qn('w:val'),'single');e.set(qn('w:sz'),'4');e.set(qn('w:color'),'D9D9D9');bd.append(e)
            tcPr.append(bd)
            mar=OxmlElement('w:tcMar')
            for edge in ['top','left','bottom','right']:
                e=OxmlElement('w:'+edge);e.set(qn('w:w'),'85');e.set(qn('w:type'),'dxa');mar.append(e)
            tcPr.append(mar)
            for z in c.paragraphs:
                z.paragraph_format.space_after=Pt(2);z.paragraph_format.line_spacing=1.05
                for r in z.runs:r.font.size=Pt(9);r.bold=i==0;r.font.color.rgb=RGBColor.from_string('FFFFFF' if i==0 else '243247')
    repeat=OxmlElement('w:tblHeader');t.rows[0]._tr.get_or_add_trPr().append(repeat)
    p().paragraph_format.space_after=Pt(1)
    return t
def pic(name,caption,maxh=5.1):
    path=TMP/name
    w,hh=Image.open(path).size
    width=min(7.05,maxh*w/hh)
    z=p();z.alignment=WD_ALIGN_PARAGRAPH.CENTER;z.paragraph_format.space_after=Pt(3)
    shape=z.add_run().add_picture(str(path),width=Inches(width))
    shape._inline.docPr.set('descr',caption)
    c=p(caption,'Caption');c.paragraph_format.space_after=Pt(9)
    for r in c.runs:r.font.size=Pt(8.5);r.font.color.rgb=RGBColor.from_string('526174')
def source(t):
    z=p('Codeverwijzing: '+t)
    for r in z.runs:r.font.size=Pt(8.5);r.font.color.rgb=RGBColor.from_string('526174')

# 1 Cover and introduction
p('CARGOHUB  |  GROEP 1').runs[0].bold=True
doc.add_heading('Technische documentatie CargoHub',0)
p('Handleiding voor nieuwe ontwikkelaars en technische overdracht','Subtitle')
p('Jeffrey Doornbos · Artur · Anmol Haribhajan · Ihssane')
p('Versie 1.0  |  18 november 2026  |  Codebasis c85e754')
h('Inleiding')
p('CargoHub ondersteunt het beheer van magazijnen, artikelen, voorraad, orders, zendingen en interne transfers. De oorspronkelijke Python API blijft als V1 beschikbaar. De nieuwe C# API draait als V2 met PostgreSQL en wordt automatisch uitgerold via GitHub Actions, SSH en Docker Compose.')
p('Dit document helpt een nieuwe collega de code te vinden, het datamodel en de belangrijkste processen te begrijpen, lokaal te starten en de server te beheren. Het beschrijft de huidige implementatie en benoemt beveiligingsmaatregelen die nog nodig zijn.')
h('Leeswijzer')
table(['Onderwerp','Pagina'],[
('Projectstructuur en gebruikte frameworks','2'),('Systeemontwerp en procesverloop met UML','3 en 4'),('Datamodel en constraints met ERD','5 en 6'),('API en Swagger','7'),('Kwaliteitseisen en conventies','8'),('CI CD pipeline','9'),('Sleutels en persoonsgegevens','10'),('GitHub flow en samenwerking','11'),('Lokaal starten','12'),('VPS beheer en overdracht','13')],[6.0,1.0])
p('Repository: https://github.com/JeffreyDoornbos/INF2C-Team-1')

# 2 Structure
page('1 Projectstructuur en frameworks')
p('De repository bevat zowel de bestaande Python API als de C# opvolger. Gebruik de C# solution voor ontwikkeling en automatische tests. De releasecode staat apart van de applicatielogica.')
table(['Pad vanaf de repositoryroot','Verantwoordelijkheid'],[
('api/ en data/','Python V1 en oorspronkelijke JSON-data.'),('api-csharp/Controllers/','HTTP-routes, invoer en responses.'),('api-csharp/Domain/','Entities van het zakelijke datamodel.'),('api-csharp/Data/ en Migrations/','DbContext, databaseconfiguratie en schemawijzigingen.'),('api-csharp/Services/ en Contracts/','Procesregels, regelopslag en request- en responsemodellen voor transfers en zendingen.'),('api-csharp/Documentation/ en Routing/','Swagger-voorbeelden, auteursgroepen en versieconventie.'),('api-csharp/Tests/','Integratietests met een echte tijdelijke PostgreSQL-database.'),('tests/CargoHub.Api.Tests/','Unittests; overige *Checks-projecten zijn aanvullende consolechecks.'),('deployment/csharp/ en .github/workflows/','Docker, SSH-uitrol en GitHub Actions.'),('database/postgresql/ en documentatie/','Importgenerator, SQL en technische achtergrond.')],[3.0,4.0])
h('Technische basis')
table(['Technologie','Toepassing'],[
('.NET 10 en ASP.NET Core','C# web-API, controllers en dependency injection.'),('Entity Framework Core 10 en Npgsql','Objectmapping, SQL-toegang en migraties voor PostgreSQL.'),('Swashbuckle 10.2.3','OpenAPI-specificatie en interactieve Swagger UI.'),('xUnit en coverlet.collector','Geautomatiseerde tests en coveragebestanden.'),('PostgreSQL 16 en Docker Compose','Blijvende gegevensopslag en containerbeheer.'),('Nginx en GitHub Actions','Routing, omschakeling en automatische build, tests en uitrol.')],[3.0,4.0])
source('CargoHub.sln; api-csharp/CargoHub.Api.csproj; deployment/csharp/compose.yml.')

# 3 Component UML
page('2 Systeemontwerp met UML')
p('Het componentdiagram toont de scheiding tussen clients, routing, applicatielogica en opslag. Nginx kiest op basis van het URL-pad tussen Python V1 en C# V2.')
pic('uml_component.png','Figuur 1  UML componentdiagram van de huidige applicatie',maxh=5.35)
h('Belangrijke ontwerpkeuzes')
bullet('De meeste CRUD-controllers gebruiken de DbContext rechtstreeks. Transfers en zendingen gebruiken services voor hun procesregels en transacties.')
bullet('ASP.NET Core injecteert DbContext, services en stores per request. EF Core verzorgt het volgen en opslaan van gewijzigde entities.')
bullet('V1 en V2 hebben afzonderlijke opslag. Na de beginimport vindt geen automatische synchronisatie tussen JSON en PostgreSQL plaats.')
p('Nginx publiceert poorten 80, 3000 en 8401. Gebruik publiek poort 80. De C# slots zijn op de host alleen via 127.0.0.1:8501 en :8502 bereikbaar en luisteren binnen de container op 8080. PostgreSQL is intern bereikbaar als db:5432.')
source('Program.cs; Controllers/; Services/; Data/CargoHubDbContext.cs; nginx.conf.template.')

# 4 Sequence
page('3 Procesverloop van een transfer')
p('Een transfer verplaatst voorraad tussen twee bestaande locaties. De commit verwerkt regels en voorraad als één geheel. Dit voorkomt dat bij een fout slechts een deel van de verplaatsing wordt opgeslagen.')
pic('uml_sequence.png','Figuur 2  UML sequencediagram van de transfercommit met twee statuspaden',maxh=5.45)
p('De service vergrendelt de transfer en relevante voorraad. Een verwerkte transfer opnieuw committen wijzigt de voorraad niet nogmaals. Bij onvoldoende beschikbare voorraad volgt 409 en wordt de transactie niet vastgelegd. Een onbekende transfer geeft bij commit 404.')
p('De response loopt via de controller en Nginx terug naar de client. V2 voert momenteel geen authenticatie of autorisatie uit; daarom staat er geen bestaande tokencontrole in dit diagram.')
source('Controllers/TransfersController.cs; Services/TransferService.cs en TransferItemStore.cs.')

# 5 ERD
page('4 Datamodel van artikelen en voorraad')
p('Het EF Core-model bepaalt het PostgreSQL-schema. De twee ERD-figuren tonen alle vijftien zakelijke tabellen. Technische migratie- en importhistorie staat hier niet in.')
pic('erd_stam.png','Figuur 3  ERD van stamgegevens en voorraad met de belangrijkste kolommen',maxh=4.75)
p('PK betekent primary key en FK betekent foreign key. Een 1 naar 0..* betekent dat één bovenliggend record aan nul of meer onderliggende records gekoppeld kan zijn. Iedere getoonde verplichte FK verwijst naar precies één bestaand record.')
h('Voorraad is een combinatie')
p('inventory heeft geen apart ID: de combinatie (item_id, location_id) is de primary key. Een artikel kan daardoor op meerdere locaties voorkomen, met per locatie eigen voorraadhoeveelheden.')
h('Database bewaakt geldigheid')
bullet('Voorraadhoeveelheden zijn niet negatief en quantity_allocated mag niet groter zijn dan quantity_on_hand.')
bullet('Foreign keys gebruiken RESTRICT. Verwijderen met bestaande verwijzingen wordt geweigerd.')
source('Domain/; Data/CargoHubDbContext.cs; documentatie/diagrammen/database_erd_efcore.md.')

# 6 ERD2
page('5 Datamodel van orders en bewegingen')
pic('erd_proces.png','Figuur 4  ERD van klanten, orders, zendingen en transfers',maxh=5.1)
p('De herhaalde item-, location- en warehouse-nodes verwijzen naar figuur 3. De relatie van client naar order omvat drie afzonderlijke foreign keys: opdrachtgever, afleverklant en factuurklant. Een shipment kan zonder order bestaan; order_id is daarom nullable.')
table(['Regel','Implementatie'],[
('Statuswaarden','Order: Pending of Shipped. Shipment: Scheduled, Transit of Delivered. Transfer: Scheduled of Processed.'),('Artikelregels','amount > 0; order_item.unit_price ≥ 0. transfer_item is uniek op (transfer_id, item_id).'),('Transferlocaties','from_location_id en to_location_id moeten verschillen.'),('Datatypes','unit_price: numeric(18,4); unit_weight: numeric(18,6). shipment_date bewaart een volledig tijdstip.'),('Timestamps','created_at en updated_at krijgen bij invoegen CURRENT_TIMESTAMP; de API moet updated_at bij wijzigen zelf bijwerken.')],[1.7,5.3])
source('Data/CargoHubDbContext.cs; Domain/Shipment.cs; Migrations/.')

# 7 API
page('6 API beschrijving en Swagger')
p('Swagger is de interactieve ingang voor de API. Per actie zijn methode, pad, parameters en schema’s zichtbaar. Veel acties hebben daarnaast voorbeelden en toegelichte statuscodes. Try it out voert een echte aanvraag uit tegen de aangesloten database.')
p('Publiek: http://145.24.237.22/api/v2/swagger/index.html')
p('Specificatie: /api/v2/swagger/v2/swagger.json')
table(['Resource onder /api/v2','Acties','Onderwerp'],[
('warehouses','6','Magazijnen en locaties'),('locations','5','Opslaglocaties'),('items','7','Artikelen, filters en voorraad'),('inventories','6','Voorraad per artikel en locatie'),('item_groups / item_lines / item_types','18','Artikelclassificaties en gekoppelde artikelen'),('clients / suppliers','12','Klanten, leveranciers en gekoppelde records'),('orders','7','Orders en orderregels'),('shipments','9','Zendingen, regels en gekoppelde orders'),('transfers','7','Interne verplaatsingen en commit')],[3.25,.65,3.1])
p('Totaal: 77 methode-padcombinaties in de huidige OpenAPI-specificatie. De versie-informatie op /api/v2 en de gereedheidscheck op /health/ready staan hiernaast als minimale endpoints.')
h('Voorbeeld van een schrijfactie')
code('POST /api/v2/item_groups\nContent-Type: application/json\n\n{"id":900001,"name":"Demogroep","description":"Presentatie"}')
p('Verwacht 201 bij aanmaken en 409 bij een dubbel ID. De server zet created_at en updated_at. GET met dit ID geeft 200 en het record terug. Na verwijderen geeft deze classificatieroute 200 met null; de oude voorraadroute met één ID geeft juist altijd 404.')
p('Gebruik een aparte demodatabase voor schrijfacties. Behoud dit bestaande gedrag bij een wijziging, of leg een bewuste API-wijziging met tests vast. Niet iedere controller handelt alle databasefouten al uniform af.')
source('Program.cs; Documentation/; Controllers/; publieke OpenAPI-specificatie van 18 november 2026.')

# 8 Quality
page('7 Kwaliteitseisen en conventies')
p('Een wijziging is klaar voor overdracht wanneer het verwachte gedrag duidelijk is, relevante tests slagen en documentatie en migraties bij de code passen. Onderstaande afspraken dienen als checklist bij nieuwe wijzigingen.')
table(['Onderwerp','Afspraak en controle'],[
('Naamgeving','PascalCase voor C# types en publieke members; camelCase voor lokale variabelen. JSON en databasekolommen gebruiken snake_case.'),('API gedrag','Behoud bestaande routes en responsevormen. Test statuscode én inhoud; een geslaagde HTTP-code alleen bewijst geen juiste opslag.'),('Verantwoordelijkheden','HTTP-afhandeling in controllers. Complexe procesregels in services. EF Core wordt rechtstreeks gebruikt; er is geen algemene repositorylaag.'),('Database','Schemawijzigingen via EF-migraties. Controleer relaties en constraints met echte PostgreSQL-tests.'),('Dataveiligheid','Voorraadmutaties in een transactie. Test herhaalde en gelijktijdige commits en het terugdraaien bij fouten.'),('Tests','Duidelijke naam en XML-summary met doel. Arrange, Act, Assert. Gebruik geïsoleerde testgegevens, nooit de live database.'),('Review','Code, tests, Swagger, secrets en terugwaartse compatibiliteit controleren. Naamgevingsafspraken worden bij review gecontroleerd; de workflow dwingt geen .editorconfig-regels af.')],[1.45,5.55])
h('Bestaande testlagen')
p('De regressieset bevat 73 unit-testcases en 43 PostgreSQL-integratietestcases. Integratietests maken een unieke database, voeren migraties uit en gebruiken een ASP.NET Core-testhost. De fixture ruimt de zelf aangemaakte database na afloop op. Dit is geen volledige dekking van alle schrijfroutes.')
p('De pipeline bewaart TRX-resultaten en Cobertura-coverage. Coverage maakt ongeteste code zichtbaar; een hoog percentage vervangt geen goede assertions. Er is geen harde responstijd- of coveragegrens in de workflow ingesteld.')
code('dotnet test CargoHub.sln --configuration Release')
p('Voor integratietests moet CARGOHUB_TEST_ADMIN_CONNECTION verwijzen naar een aparte PostgreSQL-testserver met rechten om tijdelijke databases aan te maken.')
source('tests/CargoHub.Api.Tests/; api-csharp/Tests/; .github/workflows/csharp-tests.yml.')

# 9 Pipeline
page('8 CI CD pipeline')
p('GitHub Actions voert bij pushes en pull requests de build en tests uit. Alleen een geslaagde run op main gaat door naar een release en deployment. Een handmatige workflowstart op main kan dezelfde keten uitvoeren.')
pic('pipeline.png','Figuur 5  Pipeline van codewijziging tot actieve V2-container',maxh=5.25)
p('De VPS-runner verbindt uitgaand met GitHub omdat inkomend SSH vanaf GitHub-cloudrunners door het schoolnetwerk wordt tegengehouden. De deployjob downloadt het artifact en verstuurt de ZIP via een beperkte SSH-sleutel op poort 8422 naar de releaseontvanger.')
p('Een versie heeft de vorm 2.0.RUNNUMMER.POGING. De ontvanger controleert paden, bestandsgrootte en hashes. Het script bewaart de database en importeert JSON alleen als de beginimport nog niet geregistreerd is.')
p('Nginx schakelt pas na kandidaatcontroles om. Bij een mislukte proxycontrole wordt de vorige configuratie teruggezet. Databasemigraties worden niet automatisch teruggedraaid; maak migraties daarom geschikt voor de oude én nieuwe API tijdens de omschakeling.')
source('.github/workflows/csharp-tests.yml; deployment/csharp/receive-release.sh en deploy.py.')

# 10 Security
page('9 Sleutels en persoonsgegevens')
p('Een SSH-sleutel beveiligt de deploymentverbinding. Een databasewachtwoord geeft toegang tot PostgreSQL. Beide zijn iets anders dan een sleutel waarmee persoonsgegevens in de database worden versleuteld. Het project heeft momenteel geen ingerichte versleuteling van persoonsgegevens (PII).')
table(['Geheim of sleutel','Huidige opslag en gebruik'],[
('SSH deploysleutel','Private sleutel in GitHub Actions-secret VPS_DEPLOY_KEY. De job schrijft hem tijdelijk met rechten 600 en verwijdert hem bij afsluiten.'),('Publieke deploysleutel','Op de VPS in authorized_keys van cargohub-deploy. restrict en een forced command beperken gebruik tot receive-release.sh.'),('SSH hostkeys','Publieke hostkeys in deployment/csharp/known_hosts. StrictHostKeyChecking voorkomt stilzwijgend vertrouwen van een andere host.'),('Databasewachtwoord','Root-only /opt/cargohub-v2/.env met rechten 600. Compose geeft de verbinding via de containeromgeving door. Dit is toegangsbeperking, geen versleutelde opslag.'),('Lokale databaseverbinding','Omgevingsvariabele of .NET User Secrets buiten Git. User Secrets is bedoeld voor ontwikkeling en is geen versleutelde kluis.'),('Python V1 API-sleutels','De oude API leest API_KEY en vergelijkt met api_key in data/user.json. De bronwaarden staan als leesbare tekst in JSON en worden niet naar V2 geïmporteerd.'),
('PII encryptiesleutel','Nog niet ingericht. Geen bestaande sleutel, sleutelbeheer of veldversleuteling in het C# model.')],[1.65,5.35])
h('Rechten en huidige grenzen')
p('cargohub-runner draait zonder Docker-groep of sudo-rechten. De deploygebruiker mag alleen de releaseontvanger met sudo starten. PostgreSQL heeft geen gepubliceerde hostpoort. De API-containers draaien zonder root, met een alleen-lezen bestandssysteem en tijdelijke /tmp-opslag.')
p('V2 heeft nog geen authenticatie of autorisatie. De publieke verbinding gebruikt HTTP. Namen, adressen, e-mailadressen en telefoonnummers in klant-, leverancier- en magazijnrecords kunnen persoonsgegevens zijn. Een Docker-volume of bestand met rechten 600 maakt die gegevens niet automatisch versleuteld.')
h('Aanpak voordat echte persoonsgegevens worden gebruikt')
p('Richt HTTPS en API-toegangscontrole in. Spreek af welke PII bewaard moet worden. Gebruik voor benodigde veld- of back-upversleuteling een beheerde sleutelkluis buiten database en repository, met beperkte rechten en sleutelrotatie. Maak versleutelde back-ups buiten de VPS en controleer herstel. Deze maatregelen zijn een vervolgadvies, geen bestaande functionaliteit.')
source('Program.cs; compose.yml; install-server.sh; install-runner.py; workflow deployjob.')

# 11 Collaboration
page('10 GitHub flow en samenwerking')
p('Werk per taak in een aparte branch. main is de gedeelde basis en triggert bij een geslaagde pipeline ook een echte uitrol. Gebruik daarom een pull request om de wijziging te laten beoordelen voordat deze op main komt.')
pic('github_flow.png','Figuur 6  Werkafspraak voor een wijziging van taak tot deployment',maxh=1.1)
h('Stappen voor een nieuwe wijziging')
for t in ['Pak een issue op en beschrijf verwacht gedrag en acceptatiecriteria.','Werk main bij en maak bijvoorbeeld feature/ch-029-item-classifications.','Maak kleine commits met een duidelijke beschrijving. Voeg tests en documentatie toe bij gewijzigd gedrag.','Push de branch en open een pull request met wat is veranderd en hoe het is gecontroleerd.','Laat een teamgenoot reviewen en controleer de Actions-resultaten. Merge pas wanneer opmerkingen zijn verwerkt.','Controleer na de merge de release, deployjob en Swagger. Noteer belangrijke keuzes bij het issue.']:
    p(t,'List Number')
h('Wegwijzer naar de huidige onderdelen')
table(['Teamgenoot','Onderdelen volgens de Swagger-auteursindeling'],[
('Artur','Magazijnen, locaties, artikelen, voorraad en gekoppelde artikelroutes.'),('Jeffrey Doornbos','CRUD van artikelgroepen, artikellijnen en artikeltypen.'),('Ihssane','Klanten, leveranciers, orders en orderregels.'),('Anmol Haribhajan','Zendingen en transfers.')],[1.65,5.35])
p('Deze indeling helpt bij vragen en overdracht; het team blijft gezamenlijk verantwoordelijk voor review en onderhoud. Verplichte branch protection of verplichte reviewers moeten apart in GitHub Settings worden gecontroleerd; de workflow alleen dwingt dat niet af.')
source('Documentation/DemoOperationFilter.cs; .github/workflows/csharp-tests.yml.')

# 12 Local onboarding
page('11 Lokaal starten')
p('Nodig: Git, .NET 10 SDK, Docker met Compose en voor de optionele JSON-import een PostgreSQL-client met psql. Voer applicatiecommando’s uit vanuit de repositoryroot.')
h('Een lokale PostgreSQL database')
code('docker run --name cargohub-local \\\n  -e POSTGRES_USER=cargohub \\\n  -e POSTGRES_PASSWORD=KIES_EEN_LOKAAL_WACHTWOORD \\\n  -e POSTGRES_DB=cargohub \\\n  -p 127.0.0.1:5432:5432 \\\n  -v cargohub-local-data:/var/lib/postgresql/data \\\n  -d postgres:16-alpine')
p('Vervang het voorbeeldwachtwoord door een eigen lokaal wachtwoord. Bestaat de container al, gebruik dan docker start cargohub-local in plaats van opnieuw docker run. Wacht tot PostgreSQL gereed is.')
h('Verbinding en schema instellen')
code('dotnet tool restore\ndotnet user-secrets set "ConnectionStrings:CargoHub" \\\n  "Host=localhost;Port=5432;Database=cargohub;Username=cargohub;Password=JOUW_WACHTWOORD" \\\n  --project api-csharp\ndotnet ef database update --project api-csharp')
p('Zet de verbindingsstring niet in een gedeeld bestand. Omgevingsvariabele ConnectionStrings__CargoHub heeft voorrang op User Secrets. Herstart de API na een configuratiewijziging.')
h('Optioneel de oorspronkelijke data laden')
code('python3 database/postgresql/generate_legacy_import.py\npsql -h localhost -U cargohub -d cargohub \\\n  -f database/postgresql/import-legacy-json.sql')
p('Gebruik een lege database met actuele migraties. Genereer de SQL opnieuw: een historisch importbestand bevat nog ingekorte shipmentdatums. De generator houdt rekening met het actuele model. De beginimport laadt 80.465 zakelijke records; API-gebruikers en sleutels worden niet geïmporteerd.')
h('De C sharp API als V2 starten')
code('export Api__Version=v2\nexport Swagger__RoutePrefix=api/v2/swagger\ndotnet run --project api-csharp --launch-profile http')
p('Open http://localhost:5268/api/v2/swagger/index.html. Zonder de versie-instellingen gebruikt de lokale C# API standaard /api/v1 en /swagger. Op de VPS zet Compose deze instellingen expliciet op V2.')
source('Program.cs; Properties/launchSettings.json; database/postgresql/IMPORT-ANMOL.md.')

# 13 Operations
page('12 VPS beheer en overdracht')
p('Beheer loopt via de bestaande beheerdersverbinding. De deploylistener op 8422 is voor de beperkte deploysleutel, niet voor interactief serverbeheer.')
code('ssh ubuntu-1098684@145.24.237.22\ncd /opt/cargohub-v2\nls -lah\ncat deployment.json\nsudo docker compose ps')
table(['Locatie op de VPS','Inhoud'],[
('/opt/cargohub-v2/','Compose-configuratie, deployscripts en actieve versie.'),('/opt/cargohub-v2/releases/','Versiepakketten en uitgepakte applicatiebestanden.'),('/opt/cargohub-v2/backups/','pg_dump-back-ups vóór schemawijzigingen.'),('/opt/cargohub-runner/','Installatie van de GitHub Actions-runner.'),('/opt/cargohub-python/','Bestaande Python-code en JSON-data.'),('/var/log/cargohub-deploy.log','Verloop van de release en uitrol.')],[3.15,3.85])
h('Status en logs bekijken')
code("sudo systemctl status 'actions.runner.*' --no-pager\nsudo systemctl status cargohub-deploy-ssh --no-pager\nsudo docker compose logs --tail 30 api-blue api-green\nsudo tail -n 50 /var/log/cargohub-deploy.log\ncurl -i http://145.24.237.22/health/ready")
p('GitHub toont de runner onder Settings → Actions → Runners als cargohub-vps. Idle betekent dat hij gereed is voor een opdracht. Het actieve slot staat in deployment.json; blue en green wisselen per geslaagde uitrol.')
h('Bij een storing')
table(['Symptoom','Eerste controle'],[
('Configure ConnectionStrings__CargoHub','Lokale verbinding ontbreekt. Controleer User Secrets of de omgevingsvariabele.'),('API of database niet gereed','Bekijk compose ps en de logs van de actieve API en db.'),('Deployment blijft wachten','Controleer de runner-service en de runnerstatus in GitHub.'),('IP met :3000 niet bereikbaar','Gebruik het publieke adres zonder poort, dus via HTTP 80.'),('Deployment faalt','Lees de mislukte Actions-stap en het deploylog voordat je opnieuw start.')],[2.9,4.1])
p('Bewaar .env en sleutels buiten Git en toon ze niet tijdens een demo. Gebruik op de VPS nooit docker compose down -v: dat verwijdert ook het databasevolume. Een databaseherstel is een aparte beheeractie; leg vooraf de te herstellen back-up en gevolgen voor recente wijzigingen vast.')
source('deployment/csharp/README.md; compose.yml; deploy.py; install-runner.py.')

for elem in list(doc._element.iter(qn('w:pBdr'))):
    elem.getparent().remove(elem)
for elem in list(doc.styles.element.iter(qn('w:pBdr'))):
    elem.getparent().remove(elem)
target=OUT/'CargoHub-Technische-documentatie.docx'
doc.save(target)
print(target)
