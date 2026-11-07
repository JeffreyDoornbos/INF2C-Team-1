# Sprint 2 — Transfers en transfer_items migreren naar C#

## Resultaat en afbakening

De transferfunctionaliteit is overgezet naar `TransfersController`, `TransferService` en `TransferItemStore`, volgens dezelfde controller-, DTO- en serviceopzet als de shipmentmigratie. De zeven bestaande methode-padcombinaties zijn aanwezig. Gegevens worden via EF Core in PostgreSQL opgeslagen; Python- en JSON-bronnen worden niet aangepast.

Het normale v1-contract blijft behouden: snake_case, transferobjecten inclusief `items`, itemlijsten met alleen `item_id` en `amount`, POST met 201 en lege body en PUT/DELETE/commit met 200 en lege body. Bekende verlies-van-opslagfouten en onveilige commits worden niet opnieuw geïntroduceerd. Die verschillen zijn hieronder expliciet benoemd.

Er is geen nieuwe EF-migratie nodig. Transfer, TransferItem, hun sleutels, relaties en statusconstraints stonden al in CH-012. Alleen de twee DbSet-properties zijn toegevoegd; EF meldt geen verschil met de migrationsnapshot.

## Python naar C#

| Python | C# |
|---|---|
| `get_transfers()` | `TransferService.GetTransfersAsync`: headers en regels in twee queries, met items in ieder antwoord |
| `get_transfer()` | `GetTransferAsync`, inclusief `_with_items`-vorm |
| `get_items_in_transfer()` | `GetItemsInTransferAsync`: `item_id` en `amount` |
| `add_transfer()` | `AddTransferAsync`: status altijd Scheduled, server-timestamps en gekoppelde regels |
| `update_transfer()` | `UpdateTransferAsync`: Scheduled-header wijzigen en optioneel regels vervangen |
| `remove_transfer()` | `RemoveTransferAsync`: eerst eigen regels verwijderen, daarna Scheduled-header |
| `_with_items()` | Expliciete `TransferResponse` uit `WithItems` |
| `TransferItems.get_items_for_transfer()` | `TransferItemStore.GetItemsForTransferAsync` |
| `set_items_for_transfer()` | `SetItemsForTransferAsync`: alleen regels van de gevraagde transfer vervangen |
| `remove_items_for_transfer()` | `RemoveItemsForTransferAsync`: alleen eigen regels verwijderen |
| `_next_id()` | Bestaande PostgreSQL identity in plaats van `max(id)+1`; interne regel-IDs zijn niet publiek |
| constructor, `load`, `save`, debuglijsten | Dependency injection, EF-query’s, `SaveChangesAsync` en geïsoleerde testfixtures |
| commit in `main.py` | `CommitTransferAsync`: bron afboeken, bestemming bijboeken/aanmaken, status Processed en regels behouden |
| `notification_processor.push` bij POST/commit | Dezelfde berichttekst via ILogger, pas na databasecommit; geen extra processing-log bij herhaalde commit |

De oude JSON-save op een nieuw aangemaakte providerinstantie werd niet behouden. Succes betekent nu dat dezelfde context zijn wijzigingen werkelijk heeft opgeslagen.

## Routes en responses

Basis: `/api/v1/transfers`.

| Methode | Pad | Succes en ontbreken |
|---|---|---|
| GET | collectie | 200 met een array van transfers inclusief items; zonder records `[]` |
| GET | `/{id}` | 200 met een transfer inclusief items; onbekend ID: 200 met JSON `null` |
| GET | `/{id}/items` | 200 met `[{"item_id":1,"amount":10}]`; onbekend ID: `[]` |
| POST | collectie | 201, lege body; header en items opgeslagen als Scheduled |
| PUT | `/{id}` | 200, lege body; onbekend ID blijft een no-op |
| DELETE | `/{id}` | 200, lege body; onbekend ID blijft een no-op |
| PUT | `/{id}/commit` | 200, lege body; een reeds Processed-transfer blijft een no-op; onbekend ID geeft 404 |

Een transferresponse bevat `id`, `reference`, `from_location_id`, `to_location_id`, `transfer_status`, `created_at`, `updated_at` en `items`. Interne transfer-item-IDs en `transfer_id` worden niet extra in de itemresponse opgenomen.

POST zet de status altijd op Scheduled, ook wanneer de client Processed meestuurt, zoals `add_transfer()` in Python. Bestaande expliciete IDs blijven behouden; zonder ID genereert PostgreSQL er een. Server-timestamps vervangen meegestuurde timestamps. PUT gebruikt het ID uit het pad en bewaart de oorspronkelijke `created_at`.

Bij PUT betekent een ontbrekende of null `items`-property: huidige regels behouden. Een aangeleverde lijst vervangt alle eigen regels en laat andere transfers ongemoeid. Een lege lijst wordt volgens CH-010 geweigerd: een nieuwe of gewijzigde transfer moet minstens één positieve artikelregel hebben.

## Statussen en verwerking

| Situatie | Gedrag |
|---|---|
| Nieuwe transfer | Scheduled; voorraad verandert nog niet |
| Geldige wijziging van Scheduled | Header en optionele regels wijzigen samen; voorraad verandert nog niet |
| Geldige commit | Alle bron- en bestemmingsupdates en de status Processed in één transactie |
| Herhaalde commit op Processed | 200 zonder nieuwe voorraadmutatie, timestampwijziging of processing-log |
| Gewone PUT wil Processed zetten | 409; verwerken kan alleen via commit |
| PUT/DELETE op Processed | 409; verwerkte transfer en itemhistorie blijven behouden |
| Tekort of ontbrekende bronvoorraad | 409; volledige beginstand en Scheduled-status blijven behouden |
| Ongeldige locaties, items, aantallen of dubbele artikelregels | 400; geen gedeeltelijke header/regels |

Voorraadberekening bij een geldige commit blijft die van de oorspronkelijke handler: `source.quantity_on_hand -= amount` en `destination.quantity_on_hand += amount`. Ontbreekt de bestemmingsregel, dan wordt die gemaakt met de ontvangen on_hand en nul voor expected, ordered en allocated. Bestaande expected-, ordered- en allocated-velden blijven gelijk.

Voorbeeld: bron on_hand 100, allocated 20; bestemming on_hand 20; transfer 10. Resultaat: bron 90, allocated nog 20; bestemming 30; status Processed. De regels blijven aan de transfer gekoppeld.

Beschikbare voorraad is volgens CH-010 `on_hand - allocated`. Bron 10 met allocated 8 kan geen transfer van 3 uitvoeren; een transfer van 2 kan wel. Negatieve voorraad, fictieve bijboeking zonder bron en integer-overflow aan de bestemming worden geweigerd.

## Bewuste correcties op de oude Python-versie

De Python-handler controleert de status vóór commit niet. Daardoor kan een herhaald verzoek opnieuw afboeken. Hij kan ook bijboeken terwijl de bronregel ontbreekt, en bewaakt geen gezamenlijke transactie of beschikbare voorraad. De nieuwe implementatie volgt hiervoor de al goedgekeurde CH-010-afspraken.

Ook geldt:

- De Python-modellen laten lege of dubbele transferregels toe; CH-010 en de bestaande unieke database-index vereisen minimaal één positieve regel en unieke artikel-IDs binnen een transfer.
- Python kan verwerkte records wijzigen/verwijderen. C# beschermt de verwerkingshistorie conform CH-010.
- Ongeldige JSON en ongeldige invoer geven 400 in plaats van een oude 500 of stille mislukking. Dubbele transfer-IDs geven 409. Fouten gebruiken het bestaande ASP.NET Core ProblemDetails-formaat.
- Niet-numerieke route-IDs geven 404 door de bestaande routeconstraint. Niet-ondersteunde methodes geven 405; onbekende subroutes 404.
- De bestaande C#-API heeft nog geen API_KEY-middleware. Authenticatie en rechten blijven CH-020. De functionele migratie is geen beveiligingsacceptatie of toestemming voor productie-uitrol.

Normale succesresponses zijn vergeleken met de oorspronkelijke Python-modellen en de originele commit-handler. Onveilige foutgevallen worden afzonderlijk tegen de afgesproken correcte uitkomst getest; er wordt niet beweerd dat ieder oud foutgedrag identiek is gebleven.

## Transacties en gelijktijdigheid

Writes vergrendelen de betrokken transferheader. Bij commit worden vervolgens per artikel transactiegebonden advisory locks in oplopende artikelvolgorde genomen, plus rijvergrendelingen op de betrokken voorraad in vaste item-/locatievolgorde. Dit serialiseert ook twee transfers die tegelijk een ontbrekende bestemmingsregel willen aanmaken.

Pas na alle controles worden bron, bestemming en status samen vastgelegd. Een tekort op een latere regel laat ook de eerdere regels ongewijzigd. Fouten bij het opslaan van nieuwe itemregels of bestemmingupdates draaien alle onderdelen terug. Twee gelijktijdige commits op dezelfde transfer boeken maar eenmaal. Verschillende transfers kunnen gezamenlijk niet meer boeken dan de beschikbare bronvoorraad.

Een Scheduled-DELETE en commit concurreren om dezelfde headerlock: wint verwijderen, dan vindt commit niets en wordt geen voorraad verplaatst; wint commit, dan beschermt de Processed-status de historie tegen verwijderen.

Loggerberichten na commit vervangen de eenvoudige dummy-notificatiequeue. Dit is geen duurzame berichtenbus of gegarandeerde aflevering bij procesuitval. De test bewijst dat normale en gelijktijdige herhaling geen tweede processing-log oplevert en dat een teruggedraaide commit geen processing-log verstuurt.

## Testopzet en bewijs — 7 november 2026

`tests/CargoHub.TransferChecks` maakt één uniek benoemde tijdelijke PostgreSQL-database (`transfers_<guid>`), voert alle bestaande migraties uit en start het echte C#-API-programma op een vrije localhostpoort. Alleen synthetische fixtures worden ingevoerd. De verifier stopt zijn eigen processen en verwijdert alleen zijn eigen testdatabase na afloop.

Een afzonderlijk Python-proces gebruikt de ongewijzigde `Transfers`, `TransferItems` en `Inventories`-modellen. De normale commit wordt uitgevoerd door de echte `ApiRequestHandler.handle_put_version_1`. Alleen de providerlevensduur en save-methodes zijn binnen dat testproces vervangen, zodat de oude verloren-savefout niet het verwachte rekenresultaat verstoort en geen echte JSON-bestanden worden geschreven.

De tests vergelijken de collectie-, detail- en itemresponses met Python en lezen voorraad via een onafhankelijke PostgreSQL-context. Timestamps worden apart op serverbeheer, behoud, rollback en herhaling gecontroleerd. De rollbacktests vergelijken ook alle timestamps exact.

Resultaten:

- **35 transferacceptatiecontroles geslaagd**: normale CRUD en commit, itemkoppelingen, statusovergangen, voorraadtekort, gereserveerde voorraad, ontbrekende bron/bestemming, integer-overflow, latere fout/tekort, herhaalde commit, procesherstart, rollback en concurrency.
- **31 shipmentcontroles geslaagd**.
- **72 schemacontroles geslaagd**.
- **71 classificatie-/bestaande HTTP-regressiecontroles geslaagd**.
- **25 bestaande API-unittests geslaagd**; deze gebruiken de bestaande EF InMemory-opzet en zijn aanvullend op de echte PostgreSQL/HTTP-tests.
- Build: 0 fouten, 0 waarschuwingen. Het EF-model is gelijk aan de migrationsnapshot.

Deze aantallen zijn afzonderlijke controlereeksen, geen optelling bij historische 88/92 HTTP-metingen.

Bewijs: [transferresultaten](transfer-tests/transfers.txt), [shipmentregressie](transfer-tests/shipments.txt), [schemaregressie](transfer-tests/schema.txt), [classificatieregressie](transfer-tests/classifications.txt) en [API-unittests](transfer-tests/api-unit.txt).

## Zelf uitvoeren in PyCharm

Vereist: Python 3.10+, .NET 10 SDK en Docker Desktop. Open een lokale terminal in de repositoryroot:

```sh
docker compose -f compose.schema-tests.yml up -d --wait
export CARGOHUB_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Port=55432;Database=postgres;Username=cargohub_test;Password=ch012-local-disposable'
dotnet run --project tests/CargoHub.TransferChecks
```

Het wachtwoord hoort uitsluitend bij de bestaande lokale wegwerpcontainer. Gebruik geen productieverbinding voor testverifiers. De verifier bouwt zelf zijn eigen testdatabase op; er is geen handmatige import nodig.

Verwacht onderaan: `PASS: 35 transfer acceptance checks.` Daarna kan de compose-testomgeving worden gestopt met `docker compose -f compose.schema-tests.yml down`.