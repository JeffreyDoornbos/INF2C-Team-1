# Sprint 3 — Shipments en shipment_items migreren naar C#

Datum: 2 november 2026. Bron: Python-modellen `api/models/shipments.py`, `shipment_items.py` en de shipmentroutes in `api/main.py`, op repositorybasis `79a50b8` (inclusief de tijdens dit werk gemergede client-, leveranciers- en ordercode).

## Resultaat

De shipmentfunctionaliteit is overgezet naar `ShipmentsController`, `ShipmentService` en `ShipmentItemStore`. Headers, regels en eventuele voorraadcorrecties worden via dezelfde EF-context in één PostgreSQL-transactie opgeslagen. De Python-broncode en JSON-bestanden blijven behouden.

Het transportcontract gebruikt snake_case, dezelfde collectie/detailvormen en lege succesbodies. Het eerdere CH-010-voorstel om meerdere order-IDs te weigeren is voor deze route vervangen door het oorspronkelijke Python-gedrag, conform deze nieuwe migratieopdracht: de eerste ID wordt gebruikt.

## Python naar C#

| Pythonfunctie | C#-equivalent |
|---|---|
| `get_shipments()` | `ShipmentService.GetShipmentsAsync`: headers en gekoppelde regels in twee queries; response bevat `items` |
| `get_shipment()` | `GetShipmentAsync` met dezelfde header en geneste itemvorm |
| `get_items_in_shipment()` | `GetItemsInShipmentAsync`: alleen `item_id` en `amount` |
| `get_order_ids_in_shipment()` | `GetOrderIdsInShipmentAsync`: `[]` of `[order_id]` |
| `add_shipment()` | `AddShipmentAsync`: timestamps, header en regels; geen voorraadcorrectie |
| `update_shipment()` | `UpdateShipmentAsync`: header en optionele itemvervanging, transactioneel |
| `update_items_in_shipment()` | `UpdateItemsInShipmentAsync` en `ReplaceItemsAndAdjustOrderedAsync` |
| `_apply_ordered_delta()` | `ApplyOrderedDelta`: hoogste on_hand, ordered plus delta, ondergrens nul |
| `update_orders_in_shipment()` | `UpdateOrdersInShipmentAsync`: eerste ID gebruiken, lege lijst ontkoppelt |
| `remove_shipment()` | `RemoveShipmentAsync`: eerst regels verwijderen, daarna header; geen voorraadcorrectie |
| `_with_items()` | `WithItems` maakt het expliciete responseobject |
| `ShipmentItems.get_items_for_shipment()` | `ShipmentItemStore.GetItemsForShipmentAsync` |
| `set_items_for_shipment()` | `SetItemsForShipmentAsync`: regels vervangen en nieuwe identity-IDs gebruiken |
| `remove_items_for_shipment()` | `RemoveItemsForShipmentAsync` |
| `_next_id()` | PostgreSQL identity in plaats van globale max-ID-berekening; voorkomt concurrerende ID-botsingen |
| constructor, `load`, `save` en debuglijsten | Dependency injection, EF-query’s en `SaveChangesAsync`; testdata staat alleen in een geïsoleerde fixture |

Opslag is bewust aangepast: de bekende Python-fout waarbij een nieuwe providerinstantie de wijziging verloor, wordt niet gereproduceerd. Iedere geslaagde write is aantoonbaar blijvend.

## Routes en contract

Alle paden beginnen met `/api/v1/shipments`. Negen methode-padcombinaties:

| Methode | Pad | Antwoord |
|---|---|---|
| GET | collectie | 200 met array van headers inclusief items; lege collectie geeft `[]` |
| GET | `/{id}` | 200 met object inclusief items; onbekend ID geeft JSON `null` |
| GET | `/{id}/items` | 200 met `[{"item_id":1,"amount":2}]`; onbekend ID geeft `[]` |
| GET | `/{id}/orders` | 200 met `[]` of `[order_id]`; onbekend ID geeft `[]` |
| POST | collectie | 201 met lege body; header en regels opgeslagen |
| PUT | `/{id}` | 200 met lege body; onbekend ID blijft een no-op |
| PUT | `/{id}/items` | 200 met lege body; vervangt alle regels en past ordered-delta’s toe |
| PUT | `/{id}/orders` | 200 met lege body; lege lijst ontkoppelt, anders eerste ID; onbekende shipment blijft een no-op |
| DELETE | `/{id}` | 200 met lege body; header en regels verwijderd; onbekend ID blijft een no-op |

POST met een expliciete ID behoudt die ID; zonder ID genereert PostgreSQL er een. Bij PUT identificeert de route het record; het body-ID kan geen ander record overschrijven. De oorspronkelijke `created_at` blijft behouden en header-PUT/order-PUT vernieuwen `updated_at`. De losse items-PUT verandert de headertimestamp niet, zoals in Python. De server beheert timestamps; clienttimestamps worden niet vertrouwd.

De header bevat `id`, `reference`, `order_id`, `shipment_date`, `shipment_type`, `shipment_status`, `carrier_name`, `shipping_method`, `payment_type`, `created_at`, `updated_at` en `items`. Interne itemregel-IDs worden niet aan de API-response toegevoegd.

Ongeldige JSON, null/wrong-shape-bodies, ongeldige hoeveelheden, onbekende artikel-/orderverwijzingen of ongeldige status geven 400; dubbele shipment-IDs geven 409. De foutbody volgt het bestaande ASP.NET Core ProblemDetails-formaat. Een items-PUT naar een onbekende shipment geeft 400 in plaats van onmogelijke orphanregels aan te maken. Dit zijn expliciete reparaties van foutgevallen; oude 500-responses of ongeldige JSON-opslag worden niet nagebouwd. Niet-ondersteunde methodes geven 405 en onbekende subroutes 404.

Authenticatie blijft de bestaande C#-situatie: er is nog geen API_KEY-middleware. Die ontbrekende v1-beveiliging valt onder CH-020 en is niet door deze shipmenttaak opgelost. De huidige routes zijn daarom functioneel getest, niet als volledige productie- of beveiligingsacceptatie vrijgegeven.

## Bestaande berekeningen en relaties

Bij itemvervanging wordt per artikel de oude hoeveelheid van de nieuwe afgetrokken. De delta wordt verwerkt op de voorraadregel met de hoogste `quantity_on_hand`. Ontbreekt voorraad voor het artikel, dan worden de shipmentregels wel bijgewerkt en de voorraad overgeslagen. `quantity_ordered` wordt nooit lager dan nul. De overige hoeveelheden veranderen niet.

Bij gelijke fysieke voorraad kiest C# stabiel de laagste location_id. Python kiest de eerste regel in de geladen JSON-lijst; dat lijstvolgnummer bestaat niet in het relationele model. De fixture test een gelijke stand met dezelfde keuze. Als een ander historisch JSON-volgnummer doorslaggevend moet blijven, is daarvoor een expliciete sorteersleutel in het datamodel nodig.

Dubbele shipmentregels worden behouden, zoals in Python. Voor de delta telt alleen de laatste hoeveelheid per artikel, omdat Python daarvoor een dictionary gebruikt. Dit is dus niet dezelfde regel als de unieke artikelcombinatie binnen een transfer. Voorbeeld: oude regels voor artikel 1 bedragen 2 en 6; bij leegmaken wordt 6 afgetrokken, niet 8. Het gedrag is bewust in de compatibiliteitstest opgenomen.

Aanmaken via POST schrijft de initiële regels zonder ordered-delta. Verwijderen verwijdert header en regels zonder ordered terug te boeken. Een header-PUT zonder `items` of met `items:null` behoudt de bestaande regels; `items:[]` maakt ze leeg met de bijbehorende delta. Voorbeelden van deze verschillen staan in de Python-vergelijking.

Een shipment heeft nul of één order; een order kan meerdere shipments hebben. De foreign key blijft actief. Bij een lijst `[2,1]` wordt order 2 gebruikt; bij `[1,999]` wordt alleen de geldige eerste ID 1 verwerkt. Een niet-bestaande eerste ID wordt geweigerd zonder de bestaande relatie te verliezen.

De shipmentheader wordt bij writes vergrendeld. Voorraadregels worden in vaste item-/locatievolgorde vergrendeld. Daardoor kunnen twee shipmentwijzigingen dezelfde ordered-voorraad niet ongemerkt overschrijven. Vergrendeling en transactie worden alleen tijdens de write vastgehouden.

## Migratie: het volledige shipmenttijdstip behouden

Het oorspronkelijke C#-schema gebruikte `DateOnly` voor `shipment_date`. Python bewaart een volledig ISO-tijdstip. De nieuwe migratie `PreserveShipmentTimestamp` wijzigt daarom de kolom naar `timestamp with time zone`; de entity gebruikt `DateTimeOffset`.

```sh
dotnet tool restore
dotnet ef database update --project api-csharp
```

Gebruik de ingestelde doelverbinding en maak bij een bestaande omgeving eerst de gebruikelijke databaseback-up. De test heeft de migratie uitsluitend op een eigen wegwerpdatabase uitgevoerd.

Bestaande datumwaarden worden expliciet UTC-middernacht, onafhankelijk van de servertijdzone. Eerder weggegooide tijdinformatie kan niet uit een datum worden herleid. Herstel daarvan vereist een aparte, gecontroleerde vergelijking met de oorspronkelijke JSON. De migratie leest of herschrijft de JSON niet.

Nieuwe requests accepteren een timestamp met `Z` of een expliciete offset; het tijdstip blijft behouden en wordt als UTC teruggegeven. Een datum zonder tijd blijft geaccepteerd als UTC-middernacht. Een tijd zonder tijdzone wordt geweigerd. De Down-migratie weigert gegevens af te kappen zodra er niet-middernachtelijke tijdstippen zijn opgeslagen.

Het gecontroleerde importhulpmiddel leest het gewijzigde entitytype en behoudt nieuwe shipmenttijdstippen. De bestaande SQL-generator controleert nu alle actuele EF-migraties. **Het eerder gegenereerde `import-legacy-json.sql` blijft een historisch bestand: genereer het opnieuw vóór gebruik**, anders bevat het nog ingekorte shipmentdatums. De volledige gecontroleerde import is met het nieuwe schema opnieuw getest; historische CH-013-verslagen zijn niet overschreven.

## Testopzet en resultaten

De console-verifier `tests/CargoHub.ShipmentChecks` start de echte API in een apart proces op een vrije localhostpoort, met PostgreSQL en de echte EF-migraties. Geen controller-mocks of in-memory database. Een onafhankelijk Python-proces voert dezelfde scenario’s uit op de originele Python-modellen met geïsoleerde, stabiele providerinstanties. Alleen die testinstanties vervangen de verloren-savefout; de berekeningen in de Python-bron zijn niet aangepast.

De HTTP-collectie, detail-, item- en orderresponses worden vergeleken met de Python-resultaten. Gekoppelde order-IDs worden ook opgehaald via de nieuw gemergede `/api/v1/orders/{id}`-route. De voorraad wordt daarnaast via een onafhankelijke databasecontext gecontroleerd. De tests gebruiken uitsluitend synthetische fixtures en verwijderen hun eigen tijdelijke database en API-processen na afloop.

Vastgelegd op 2 november 2026:

- **31 shipmentacceptatiecontroles**: Python-vergelijking, alle negen routes, wijzigingen in headers/regels/orders, positieve en negatieve delta’s, ondergrens nul, ontbrekende voorraad, duplicaatregels, gelijke fysieke voorraad, herhaling, timestamps, procesherstart, concurrency en volledige rollback.
- **71 bestaande classificatie-/HTTP-regressiecontroles**, opnieuw uitgevoerd na samenvoegen met de nieuwste main.
- **25 nieuw gemergede client-/leverancier-/orderunittests** geslaagd. Deze bestaande tests gebruiken EF InMemory en zijn aanvullend op de echte PostgreSQL/HTTP-shipmenttests.
- **72 bestaande schemacontroles**.
- **15 importvalidatietests** en **74 PostgreSQL-importcontroles**, inclusief vergelijking van alle 80.465 zakelijke bronrecords en velden onder het nieuwe schema.
- Build: geen fouten of waarschuwingen na de geslaagde NuGet-hercontrole.

Deze reeksen blijven apart van de eerdere 88/92 HTTP-onderzoeksmetingen en mogen niet als dezelfde meting worden gepresenteerd.

Zie [shipmentresultaten](shipment-tests/shipments.txt), [classificatieregressie](shipment-tests/classifications.txt), [client-/leverancier-/orderunittests](shipment-tests/orders-clients-suppliers.txt), [schemaregressie](shipment-tests/schema.txt), [importvalidatie](shipment-tests/import-unit.txt) en [PostgreSQL-importregressie](shipment-tests/import-postgresql.txt).

Zelf testen met de bestaande wegwerpdatabase:

```sh
docker compose -f compose.schema-tests.yml up -d --wait
export CARGOHUB_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Port=55432;Database=postgres;Username=cargohub_test;Password=ch012-local-disposable'
dotnet run --project tests/CargoHub.ShipmentChecks
```

Het wachtwoord hierboven hoort uitsluitend bij de lokale compose-testcontainer. De verifier maakt zijn eigen database met een unieke `shipments_<guid>`-naam en verwijdert alleen die database. Python 3.10+, .NET 10 en PostgreSQL zijn nodig. Gebruik geen productieverbinding voor testverifiers.

## Acceptatie en review

- De functies uit beide Python-bestanden hebben een C#-equivalent of een expliciet gedocumenteerde opslagvervanging.
- Shipment-/order-/itemrelaties en de bestaande ordered-berekeningen zijn getest.
- Succescontracten zijn via echte HTTP-responses vergeleken; afwijkingen in foutgevallen, tijdzoneverwerking, deterministische tie-breaking en ontbrekende authenticatie zijn benoemd.
- Testresultaten zijn opgeslagen. De wijziging is gereed voor teamreview; er is geen automatische productie-uitrol of GitHub-merge uitgevoerd.

Technische bronnen, geraadpleegd op 2 november 2026: [PostgreSQL: AT TIME ZONE](https://www.postgresql.org/docs/17/functions-datetime.html#FUNCTIONS-DATETIME-ZONECONVERT) en [Microsoft: geparameteriseerde EF SQL-query’s](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries).
