# CH-010 — Pilotafspraken en migratiecontract

## 1. Doel en gebruikte bevindingen

We migreren eerst de bestaande API naar C# / ASP.NET Core en PostgreSQL in Docker, als basis voor verdere verbeteringen. Het uiteindelijke migratiedoel omvat alle bestaande ondersteunde methode-padcombinaties. De eerste pilot toetst een beperkte voorraad- en transferketen. Een beperkte pilot vervangt niet automatisch de volledige bestaande API.

| Input | Bevinding | Gevolg voor de afspraken |
|---|---|---|
| CH-001: `api/main.py`, providers en onderzoeksresultaten | Een handler en een 200/201 bewijzen geen blijvende wijziging; foutafhandeling is niet consistent. | Iedere schrijfcontrole leest onafhankelijk terug en controleert gedrag na herstart. |
| CH-002: bestaand ERD en modellen | Voorraad heeft een samengestelde sleutel; shipments verwijzen via `order_id` naar orders. | Geen kunstmatige inventory-ID; expliciete relaties en foreign keys. |
| CH-008: transferhandler en eerdere transfercontroles | Correcte aantallen, dubbele verwerking en rollback zijn niet afdoende bewezen door alleen statuscodes of een gewijzigd bestand. | Testen op eindvoorraad, herhaling, gelijktijdigheid en geforceerde fouten. |
| CH-009: herzien adviesrapport en presentatie | Bouw eerst een betrouwbare kern, gebruik PostgreSQL, oefen migratie en herstel en breid daarna uit. | Afgebakende pilot, één gegevensbron en een onderbouwd go/no-go-besluit. |

De bronnen bevatten verschillende testreeksen. Het herziene advies beschrijft 88 HTTP-aanvragen en 37 mislukte opslagcontroles; de repository bevat daarnaast een afzonderlijk verslag met 92 HTTP-metingen. Deze aantallen worden niet opgeteld of als één reeks gepresenteerd. De 64 CH-012-schemacontroles zijn weer een andere reeks en bewijzen geen werkende HTTP-endpoints.

## 2. Pilotroutes en uitsluitingen — akkoord

Alle onderstaande paden hebben prefix `/api/v1`. Toegang vereist een geldige `API_KEY` en rechten voor de handeling. Alleen aangewezen pilotclients en testgegevens gebruiken de pilotomgeving.

| Methode | Pad | Doel |
|---|---|---|
| GET | `/warehouses`, `/warehouses/{id}` | Magazijnen raadplegen |
| GET | `/warehouses/{id}/locations` | Locaties in een magazijn |
| GET | `/locations`, `/locations/{id}` | Locaties raadplegen |
| GET | `/items`, `/items/{id}` | Artikelen raadplegen |
| GET | `/items/{id}/inventory` | Voorraad per locatie |
| GET | `/items/{id}/inventory/totals` | Voorraadtotalen per artikel |
| GET | `/inventories` | Voorraadregels raadplegen |
| GET | `/transfers`, `/transfers/{id}` | Transfers raadplegen |
| GET | `/transfers/{id}/items` | Transferregels raadplegen |
| POST | `/transfers` | Een transfer aanmaken in Scheduled |
| PUT | `/transfers/{id}` | Een Scheduled-transfer aanpassen |
| PUT | `/transfers/{id}/commit` | Een transfer eenmaal verwerken |

Dit zijn 16 methode-padcombinaties. Stamgegevens en beginvoorraad komen voor deze pilot uit een gecontroleerde proefimport; zonder import of gecontroleerde fixtures kan de keten niet worden geaccepteerd.

**Buiten deze eerste pilot, maar binnen de volledige migratie:** beheer van stamgegevens, directe voorraadmutaties via POST inventories, orders, clients, shipments, classificaties, leveranciers en DELETE-operaties. Hun contracten moeten vóór aansluiting van de betrokken clients afzonderlijk worden getest. Het databasefundament van CH-012 bevat deze zakelijke tabellen al; dat betekent niet dat de bijbehorende routes klaar zijn.

**Buiten de migratiebasis en deze twee sprints:** nieuwe picktaken, scans, tellingen, batches/serienummers, waveplanning, retourprocessen en nieuwe v2-functionaliteit. Uitbreidingen volgen na de afgesproken basis.

**Niet als nieuwe v1-functionaliteit toevoegen:** GET/PUT/DELETE `/inventories/{id}` met één losse ID. De huidige implementatie retourneert hiervoor 404; voorraad is gekoppeld aan artikel én locatie. Een nieuw bewerkingspad met beide sleutels vereist een afzonderlijk contractbesluit.

De gebruiker heeft deze selectie en uitsluitingen bevestigd (B1). De huidige API blijft beschikbaar voor niet-gemigreerde clients. Geen gedeelde productiegegevens naar twee onafhankelijk schrijvende systemen kopiëren. De definitieve omschakeling vereist dat alle betrokken schrijvers op dezelfde PostgreSQL-bron aansluiten of gecontroleerd worden gepauzeerd.

## 3. Voorraadregels — akkoord B2

1. Per `(item_id, location_id)` bestaat maximaal één voorraadregel; artikel en locatie moeten bestaan.
2. `quantity_on_hand` is fysieke voorraad, `quantity_allocated` de gereserveerde voorraad. Voor de pilot is beschikbare voorraad: **on_hand − allocated**. Verwachte en bestelde aantallen tellen niet als beschikbaar.
3. Alle vier hoeveelheidsvelden zijn gehele, niet-negatieve aantallen. De pilot staat geen nieuwe overallocatie toe (`allocated > on_hand`). Bestaande afwijkingen worden bij import gemeld en opgelost vóór pilotgebruik, niet stilzwijgend naar nul aangepast.
4. Een transfer verandert alleen `quantity_on_hand` aan bron en bestemming; de andere drie hoeveelheden blijven gelijk. Gereserveerde voorraad wordt niet verplaatst.
5. De bestaande totalenresponse behoudt `total_expected`, `total_ordered`, `total_allocated` en `total_available`. `total_available` is de som van on_hand minus allocated; een extra on_hand-veld wordt niet stilzwijgend aan het v1-contract verplicht gesteld.
6. Een ontbrekende bronvoorraadregel betekent onvoldoende voorraad. Een ontbrekende bestemmingsvoorraadregel mag bij een geldige transfer worden aangemaakt met de ontvangen hoeveelheid en nul voor de overige hoeveelheden.
7. Aanmaken of aanpassen van een Scheduled-transfer reserveert nog geen voorraad. Beschikbaarheid wordt opnieuw gecontroleerd tijdens commit, binnen dezelfde transactie als de updates.

## 4. Transferregels en statussen — akkoord B2/B3

Een transfer bevat ten minste één regel. Elke regel verwijst naar een bestaand artikel en heeft een geheel `amount > 0`. Een artikel mag binnen één transfer maar eenmaal voorkomen; dubbele regels geven 400. Bron- en bestemmingslocatie bestaan en verschillen van elkaar. Een POST maakt nooit direct een Processed-transfer aan.

| Huidige status | Handeling | Afgesproken resultaat |
|---|---|---|
| Geen | Geldige POST | 201; transfer en regels opgeslagen als Scheduled; voorraad ongewijzigd |
| Scheduled | Geldige PUT | 200; gewijzigde transfer en regels samen opgeslagen; voorraad ongewijzigd |
| Scheduled | Geldige commit | 200; voorraadmutaties en status Processed samen opgeslagen |
| Scheduled | Commit met tekort | 409; voorraad en status ongewijzigd |
| Processed | Herhaald commitverzoek | 200; geen nieuwe voorraadmutatie of tweede verwerkingsmelding |
| Processed | Aanpassen van locaties, regels of status | 409; inhoud ongewijzigd |
| Processed | Terugzetten naar Scheduled | 409; niet toegestaan |

De client kan de status niet via een gewone PUT op Processed zetten. Andere statuswaarden worden in de pilot geweigerd. Annuleren, terugboeken en verwijderen van verwerkte transfers vallen buiten de pilot. Verwerking bewaart de transferregels als historie.

**Atomiciteit en gelijktijdigheid:** controle van status en beschikbare voorraad, afboeking, bijboeking en statuswijziging gebeuren in één PostgreSQL-transactie. Bij iedere fout wordt alles teruggedraaid. Gelijktijdige commits van dezelfde transfer boeken gezamenlijk maar één keer. Verschillende transfers met dezelfde bronvoorraad mogen geen updates verliezen of samen meer boeken dan beschikbaar. Gebruik bijvoorbeeld rijvergrendeling in vaste volgorde of een gelijkwaardig geteste concurrency-oplossing; een losse voorafgaande GET-controle voldoet niet.

**Herhaling:** de garantie geldt voor commit op dezelfde transfer-ID. Twee afzonderlijke POST-aanvragen zijn niet automatisch hetzelfde verzoek; veilige herhaling van POST vereist later een apart idempotentiecontract.

## 5. Concrete acceptatiescenario’s

Elke controle vergelijkt de database en een onafhankelijke GET-response. Gebruik unieke fixtures per test. Controleer de geldige transfer ook na herstart.

| Scenario | Beginsituatie en aanvraag | Verwacht resultaat |
|---|---|---|
| Geldige transfer | Bron: on_hand 100, allocated 20. Bestemming: on_hand 20. Scheduled-transfer van 10. | 200; bron 90, bestemming 30; bron allocated blijft 20; status Processed; overige hoeveelheden onveranderd. |
| Onvoldoende beschikbare voorraad | Bron: on_hand 10, allocated 8. Scheduled-transfer van 3. | 409; beschikbare voorraad is 2; geen voorraadwijziging; status blijft Scheduled. |
| Herhaalde commit | Commit de reeds verwerkte transfer uit scenario 1 opnieuw. | 200; bron blijft 90, bestemming blijft 30; status Processed; geen tweede verwerking. |
| Dubbele gelijktijdige commit | Twee commitverzoeken voor dezelfde Scheduled-transfer van 10. | Beide mogen 200 ontvangen; totaal slechts één afboeking en bijboeking. |
| Twee verschillende transfers | Bron 10, allocated 0; twee verschillende transfers van elk 7 worden tegelijk gecommit. | Eén 200 en één 409; bron eindigt op 3; alleen de geslaagde bestemming ontvangt 7. |
| Fout halverwege | Forceer een fout na de bronupdate en vóór afronding van de transactie. | 500; bron, bestemming en status blijven op de beginwaarden; herhalen kan daarna veilig. |
| Bestemmingsregel ontbreekt | Artikel en beide locaties bestaan; bron heeft voldoende voorraad. | 200; één nieuwe bestemmingsregel, hoeveelheid gelijk aan transfer; overige hoeveelheden nul. |
| Ongeldige invoer | amount 0, negatief, niet-geheel, dubbele artikelregel, lege regels of dezelfde bron/bestemming. | 400; geen gedeeltelijk opgeslagen transfer of voorraadwijziging. |
| Onbekende verwijzing | Transfer verwijst naar niet-bestaand artikel of locatie. | 400 bij aanmaken/aanpassen; geen opslag. Onbekende transfer-ID in het pad geeft 404. |
| Aanpassen na verwerking | PUT wijzigt een Processed-transfer. | 409; transfer en voorraad ongewijzigd. |

## 6. V1-contract — behouden gedrag en bewuste reparaties

**Te behouden:** prefix `/api/v1`, methode-padcombinaties binnen de afgesproken scope, `API_KEY`-header, integer resource-IDs, snake_case-veldnamen en bestaande JSON-vormen. Collecties zijn arrays (ook wanneer leeg), detailresponses zijn objecten en transferregels gebruiken `item_id` en `amount`. Classificatie-items retourneren in de huidige modellen ID-lijsten; leveranciers-items retourneren artikelobjecten. De volledige migratie moet ook dat onderscheid controleren.

Succesvolle GET: 200 met JSON; POST: 201; PUT, DELETE en commit: 200. De huidige schrijfhandlers hebben een lege succesbody: behoud die voor v1, tenzij een expliciete contractwijziging is goedgekeurd. Succes wordt pas verstuurd nadat de transactie is vastgelegd. Data blijft na een nieuwe aanvraag en herstart beschikbaar. V1-lijstresponses krijgen niet onaangekondigd een andere envelop of paginering.

**Bewuste correcties, akkoord in B4; te verifiëren met clientcontracttests:**

| Situatie | Gewenst v1-pilotgedrag |
|---|---|
| Ontbrekende of ongeldige sleutel | 401 |
| Geldige sleutel zonder rechten | 403; commitrechten expliciet testen |
| Ongeldige JSON, ID-opmaak of invoer/verwijzing | 400 |
| Onbekende route of resource in het pad | 404, in plaats van bijvoorbeeld 200 met null of geen antwoord |
| Zakelijk conflict, voorraadtekort of verboden wijziging/verwijdering | 409 |
| Onverwachte interne fout | 500, zonder interne details |

Afgesproken foutbody: `{"error":{"code":"insufficient_stock","message":"Onvoldoende beschikbare voorraad."}}`. Machines gebruiken de stabiele code, niet de Nederlandse tekst. Voor querystrings wordt het pad afzonderlijk geparseerd; onbekende queryparameters geven in de pilot 400. Eén afsluitende slash wordt gelijk behandeld aan het pad zonder slash. Deze reparaties wijken op punten af van de oude implementatie; de clientverantwoordelijke moet impact en contracttests beoordelen.

Een functie is pas gemigreerd nadat methode, pad, invoer, uitvoer, rechten én bijwerkingen zijn gecontroleerd. Een succesvolle statuscode alleen is onvoldoende.

## 7. Shipment-orderkoppeling en verwijderen — akkoord B3/B4

De opslag volgt het bestaande model: `shipment.order_id` is nullable. Eén shipment hoort bij nul of één order; een order kan meerdere shipments hebben. Voeg dus geen unieke beperking op shipment.order_id toe.

Voor de latere migratie van `/shipments/{id}/orders` blijft GET een lijst teruggeven: `[]` of `[order_id]`. Voor PUT is afgesproken: `[]` ontkoppelt; `[bestaande_id]` koppelt; meer dan één ID geeft 400. De oude code kiest bij meerdere IDs stilzwijgend de eerste; die wijziging is met het gebruikersakkoord geaccepteerd; de clientcontracttests volgen bij de routebouw. Een niet-bestaande order wordt geweigerd en de oude koppeling blijft intact.

Afgesproken verwijderbeleid: geen automatisch doorverwijderen (cascade). Verwijderen van een record met verwijzingen geeft 409. Verwerkte transfers mogen ook na het eventueel verwijderen van verwijzingen niet verdwijnen; dit vraagt een serviceregel boven op de database. Het verwijderen van concepten met regels vereist een apart besluit. DELETE is daarom buiten de eerste pilot gehouden, maar mag bij de volledige migratie niet ongemerkt verdwijnen.

## 8. Besluitregister en resterende besluiten vóór pilot

Er zijn geen bevestigde teamuren of onderbouwde prestatienormen aangeleverd. De eigenaar hieronder is een verantwoordelijke rol, geen verzonnen toewijzing aan een persoon. Bij sprintplanning wordt per rol een naam ingevuld. B1–B4 zijn door de gebruiker akkoord bevonden op 15 oktober 2026. B5–B8 blijven OPEN; het akkoord vult geen ontbrekende capaciteit, meetwaarden of pilotresultaten in.

| ID | Besluit (B1–B4 akkoord, B5–B8 open) | Eigenaar | Uiterlijk en vereist bewijs |
|---|---|---|---|
| B1 | Pilotroutes, uitsluitingen, pilotclients en relatie tot volledige migratie | Product Owner | Vóór pilotbouw afronden: akkoord op sectie 2, datum en naam |
| B2 | Beschikbare voorraad, allocaties, positieve regels en transferstatusovergangen | Product Owner, voorbereid door backendverantwoordelijke | Vóór definitieve CH-012/CH-015-acceptatie: bevestiging of wijzigingen op secties 3–5 |
| B3 | Verwijderbeleid, statuslijsten orders/zendingen en shipment-orderrelatie | Product Owner, voorbereid door datamodelverantwoordelijke | Vóór definitieve migratieacceptatie: goedgekeurde regels en contractafwijkingen |
| B4 | V1-foutcontract, herhaalgedrag, slash/querybeleid en impact op clients | Verantwoordelijke voor clientintegraties; PO besluit over impact | Vóór pilot: goedgekeurde contractvoorbeelden en rechtenmatrix |
| B5 | Sprintduur en beschikbare capaciteit | Scrum Master / sprintplanner | Bij eerstvolgende planning, vóór pilot: teamleden, netto uren na andere verplichtingen, schattingen en afhankelijkheden |
| B6 | Representatieve dataset, gelijktijdige gebruikers en p95-responstijd per pilotroute; toegestaan foutpercentage | Testverantwoordelijke, met PO | Vóór belastingtest en pilot: nulmeting, getalsmatige grenzen en meetomgeving |
| B7 | Maximale hersteltijd (RTO), maximaal gegevensverlies (RPO), onderhoudsvenster en herstelverantwoordelijke | Beheer-/DevOps-verantwoordelijke, met PO | Vóór migratieoefening en pilot: concrete tijdsgrenzen en hersteltest |
| B8 | Go/no-go, deelnemers en blokkadecriteria | Product Owner | Vóór pilotstart: vastgelegd besluit op basis van testbewijs |

Het eerdere bord noemt Iteration 3 (7–20 oktober) en Iteration 4 (21 oktober–2 november). Dit is een eerder vastgelegde planning, geen opnieuw gecontroleerde actuele capaciteit. Twee sprints zijn geen garantie voor de volledige migratie. Bij onvoldoende capaciteit wordt de pilot verkleind of verschoven; veiligheid van opslag, rechten en herstel wordt niet overgeslagen.

De eerdere orderlijsttimeout na 15 seconden is een waarneming, geen afgesproken prestatienorm. B6 moet daarom echte grenswaarden bevatten voordat de pilot kan worden beoordeeld.

## 9. Vrijgave en relatie met CH-012

Voor de pilot moeten bron- en doelgegevens na proefimport overeenkomen, met verklaarde verschillen, geldige relaties en juiste voorraadtotalen. Pauzeer alle betrokken schrijvers bij omschakeling. Test back-up én herstel, inclusief hoe wijzigingen na omschakeling behouden blijven bij terugval. Het opnieuw starten van een container is geen herstelbewijs.

CH-012 bevat primary keys, foreign keys, RESTRICT en controles op hoeveelheden/statuswaarden. Na het gebruikersakkoord is de aanvullende migratie `EnforceApprovedPilotRules` toegevoegd voor `allocated <= on_hand` en de unieke combinatie `(transfer_id, item_id)`. Statusovergangen, onveranderlijke verwerkte transfers, ten minste één transferregel en veilige commitverwerking blijven servicelaagwerk in CH-015. De eerdere 64 schemacontroles bewijzen deze procesregels niet.


## Bronnen en herleidbaarheid

Gelezen op 15 oktober 2026, op basis van lokale repositorycommit `74bdb14b34cadba213dcf550477a6de2c7596fdc` en de eerder aangeleverde documenten:

- CH-001: [API-handler](../api/main.py), [dataprovider](../api/providers/data_provider.py), [endpointonderzoek](onderzoek-tests/README.md).
- CH-002: [bestaand ERD](diagrammen/database_erd.png), [voorraadmodel](../api/models/inventories.py), [shipmentmodel](../api/models/shipments.py).
- CH-008: transfercommit in de API-handler, [transfermodel](../api/models/transfers.py), [onderzoeksresultaten](onderzoek-tests/resultaten.json).
- CH-009: `Onderzoeksrapport-CargoHub.docx`, hoofdstukken over technische basis, pilot, migratie en herstel; klantpresentatie van 30 september en feedback over PostgreSQL/Docker.
- [CH-012-uitwerking](CH-012-schema.md) en [nieuw ERD](diagrammen/database_erd_efcore.md): huidige implementatie, geen vervanging van PO-besluiten.
