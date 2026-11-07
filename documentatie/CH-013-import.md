# CH-013 — Gecontroleerde JSON-proefimport

Afhankelijkheid: CH-012. De C#-API en het bestaande EF Core-model blijven de databasebasis. De proefimport is een apart beheerhulpmiddel in Python, aansluitend op de al aanwezige importgenerator. Hij gebruikt alleen de standaardbibliotheek en psql, rechtstreeks of via Docker.

## Wat is toegevoegd?

`database/postgresql/import_test_json.py` valideert een representatieve gegevenskopie en importeert alleen geaccepteerde records. Voor de uitgevoerde proef is de volledige lokale bronset van 80.465 zakelijke records gebruikt; deze bevat alle vijftien tabellen en hun relaties. De bronbestanden zijn alleen gelezen en niet vervangen, opgeschoond of herschreven.

Het bestaande `import-legacy-json.sql` is een eerdere, strikte alles-of-niets-import. Dat bestand en zijn generator blijven behouden. Gebruik voor CH-013 het nieuwe hulpmiddel: dat levert ook afwijzingen per record en begrenst het doel tot een testdatabase. De uitvoering in dit document heeft geen productie- of VM-database geraakt.

## Controles en afwijzingen

- De verwachte velden en typen worden gelezen uit de C#-entities. Ontbrekende of onbekende velden, null in verplichte velden, onjuiste typen, ongeldige datums en numerieke afronding worden geweigerd.
- Primary keys worden gecontroleerd, inclusief `(item_id, location_id)` voor voorraad. Alle exemplaren van een dubbele sleutel worden afgewezen: er wordt geen willekeurige winnaar gekozen.
- Een artikel mag niet twee keer in dezelfde transfer voorkomen, ook niet met verschillende regel-IDs.
- Alle foreign keys moeten wijzen naar een geaccepteerd ouderrecord. Afwijzing van een ouder wordt dus doorgegeven aan afhankelijke records, met een eigen reden in het verslag.
- Hoeveelheden, voorraadallocatie, statuswaarden, transferlocaties en prijzen volgen de CH-012-constraints.
- PostgreSQL houdt de constraints actief als onafhankelijke laatste controle. Een onverwachte databasefout draait de gehele importtransactie terug.

Deze import valideert het opslagmodel. Procesregels zoals minimaal één transferregel, toegestane statusovergangen en veilige transfercommit blijven bij CH-015; het importverslag bewijst geen werkende HTTP-operatie.

Een ontbrekend bestand, onleesbare JSON, dubbel benoemde JSON-velden of een bronbestand dat geen array is, stopt de preflight vóór een databaseverbinding. Bij zulke structurele fouten kan geen betrouwbaar recordverslag worden gemaakt; de opdracht geeft exitcode 2 en een melding. Corrigeer daarvoor een werkkopie, nooit automatisch de originele bron.

## Vastgelegde omzettingen

De omzettingen volgen de bestaande generator en het EF-model:

- `order_date` en `request_date` worden een UTC-kalenderdatum voor DateOnly; het verslag telt deze omzettingen. Sinds Sprint 3 behoudt `shipment_date` het volledige UTC-tijdstip. Eerdere importverslagen dateren van vóór deze schemawijziging. Een bestaande ingekorte shipmentdatum kan alleen met de oorspronkelijke bron worden hersteld; de nieuwe migratie verzint geen ontbrekende tijd.
- `created_at` en `updated_at` behouden het tijdstip en worden naar UTC genormaliseerd. Een timestamp zonder tijdzone wordt afgewezen.
- Een numerieke `item.commodity_code` wordt tekst. Dat sluit aan op de C#-entity.
- Geld en gewichten gebruiken exact Decimal. Getallen die buiten de precisie vallen of afgerond zouden worden, worden afgewezen.
- `user.json` wordt niet gelezen of geïmporteerd. API-sleutels en rechten vallen onder CH-020 en staan niet in deze importverslagen.

## Bescherming van de testomgeving

De opdracht accepteert alleen databasenamen van de vorm `cargohub_import_test_<naam>`. Zowel Python als het SQL-script controleren die naam. Zonder `--database` wordt alleen gevalideerd. Rechtstreeks verbinden kan alleen via localhost; de Docker-optie gebruikt de lokale testcontainer. Gebruik hiervoor geen Docker-context die naar een andere omgeving verwijst.

De vijftien zakelijke doeltabellen moeten leeg zijn en alle actuele EF-migraties moeten aanwezig zijn. De import vergrendelt de tabellen, controleert leegte en schrijft alles binnen één transactie. Hij maakt geen database aan, voert geen reset of upsert uit en verwijdert geen bestaande records. Het verslag moet buiten de bronmap worden opgeslagen.

IDs en samengestelde sleutels blijven behouden. Identity-sequences worden transactioneel boven de hoogste geïmporteerde ID gezet, zodat een later gegenereerde ID niet op geïmporteerde gegevens botst.

## Zelf uitvoeren vanuit PyCharm

Open een lokale terminal in de repositoryroot. Python 3.10+, Docker Desktop en .NET 10 SDK zijn nodig voor onderstaande complete proef. Alle wachtwoorden in de compose-testomgeving zijn uitsluitend voor de tijdelijke testdatabase.

### 1. Alleen broncontrole, zonder database

```sh
python3 database/postgresql/import_test_json.py \
  --source data \
  --report documentatie/import-tests/eigen-validatie.json
```

Dit schrijft JSON én een leesbaar Markdown-verslag. `imported=0` is bij deze stap correct: er is nog niets geïmporteerd. Exitcode 0 betekent hier dat de validatie afgerond is, niet dat ieder record geldig was. Controleer ook het aantal afwijzingen.

### 2. Nieuwe lege testdatabase en EF-schema

```sh
docker compose -f compose.schema-tests.yml up -d --wait
IMPORT_CONTAINER=$(docker compose -f compose.schema-tests.yml ps -q schema-postgres)
docker exec "$IMPORT_CONTAINER" createdb -U cargohub_test cargohub_import_test_run1

dotnet restore api-csharp
dotnet tool restore
dotnet ef migrations script --project api-csharp --output /tmp/cargohub-import-schema.sql
docker exec -i "$IMPORT_CONTAINER" psql -X -v ON_ERROR_STOP=1 \
  -U cargohub_test -d cargohub_import_test_run1 < /tmp/cargohub-import-schema.sql
```

Gebruik een nog niet bestaande testnaam. De tijdelijke .NET SDK uit eerdere werkzaamheden hoeft na een herstart niet meer aanwezig te zijn; `dotnet --version` moet een werkende .NET 10 SDK tonen.

### 3. Importeren en rapport lezen

```sh
python3 database/postgresql/import_test_json.py \
  --source data \
  --container "$IMPORT_CONTAINER" \
  --database cargohub_import_test_run1 \
  --report documentatie/import-tests/eigen-import.json
```

Lees `eigen-import.md` en `eigen-import.json`. Het verslag bevat per bestand de bronhash, gelezen, geaccepteerde, geïmporteerde en afgewezen aantallen. Elke afwijzing bevat een 1-gebaseerd rijnummer, technische sleutel en reden. Het bevat geen volledige bronrecords. De voorraadreconciliatie vergelijkt geaccepteerde brontotalen met gemeten databasetotalen voor on_hand, expected, ordered, allocated en available.

Exitcode 0 met status `imported` betekent dat alle geaccepteerde records zijn vastgelegd en gereconcilieerd. Dat kan nog steeds afgewezen records omvatten: beoordeel die expliciet. Exitcode 2 geeft een geblokkeerde of mislukte uitvoering aan. Bij een onduidelijke verbindingsbreuk wordt `commit_not_confirmed` gemeld en blijft het geïmporteerde aantal onbekend; neem dan geen succes of volledige rollback aan zonder de testdatabase te controleren.

### 4. Opnieuw uitvoeren, reset en hervatting

Dezelfde import naar een gevulde database geeft exitcode 2 en verandert geen bestaande records. Dit is bewust geen synchronisatie of gedeeltelijke hervatting.

- Na een normale SQL-fout is de transactie teruggedraaid. De database blijft leeg en opnieuw proberen is mogelijk nadat de oorzaak is opgelost.
- Na een geslaagde import, gewijzigde bronkopie of onduidelijke verbindingsbreuk: gebruik een **nieuwe lege testdatabase**, bijvoorbeeld `cargohub_import_test_run2`, bouw het EF-schema opnieuw op en voer de volledige import uit.
- Er is geen automatische reset. Een bestaande import wordt nooit overschreven. De compose-testservice heeft geen permanent volume; `docker compose -f compose.schema-tests.yml down` beëindigt de wegwerptestomgeving.

## Testbewijs van 2 november 2026

- **15 validatietests geslaagd.** Onder andere duplicaten, afwijzing van afhankelijke records, ongeldige waarden, expliciete tijdzones, precisie, bronhashes en bescherming tegen een verkeerde doelomgeving.
- **74 PostgreSQL-acceptatiecontroles geslaagd**, op vier nieuw aangemaakte testdatabases met het schema uit de echte EF-migraties. De testdatabases zijn daarna verwijderd.
- Volledige lokale bronkopie: **80.465 gelezen, 80.465 geïmporteerd, 0 afgewezen** over vijftien tabellen.
- Alle geaccepteerde records zijn **per veld** met PostgreSQL vergeleken. Alle aantallen en de vijf voorraadtotalen sluiten aan.
- Een tweede import werd geweigerd. Alle aantallen én velden bleven gelijk.
- In een afzonderlijke, bewust ongeldige fixture: **18 gelezen, 12 geïmporteerd, 6 afgewezen**. Duplicaten, een ontbrekende verwijzing, een ongeldige hoeveelheid/status en een afhankelijke regel zijn met redenen vastgelegd.
- Een bewust veroorzaakte fout bij de laatste tabel draaide alle eerdere invoer terug. Alle vijftien tabellen bleven leeg en de sequences bleven ongewijzigd; na herstel slaagde opnieuw uitvoeren.
- Alle gebruikte bronhashes waren na de proef ongewijzigd. Bestaande lokale wijzigingen in de JSON-bestanden zijn behouden.

Bewijs: [volledig importverslag](import-tests/full-import.md), [verslag met afwijzingen](import-tests/mixed-rejections.md), [validatietests](import-tests/unit-tests.txt), [PostgreSQL-controles](import-tests/postgresql-tests.txt) en [controlelijst](import-tests/acceptance.json).

Tests opnieuw uitvoeren:

```sh
python3 -m unittest discover -s tests/import-json -p 'test_*.py' -v
python3 tests/import-json/check_postgresql.py \
  --container "$IMPORT_CONTAINER" \
  --schema /tmp/cargohub-import-schema.sql \
  --source data \
  --output /tmp/cargohub-ch013-checks
```

De PostgreSQL-test maakt uitsluitend willekeurig benoemde `cargohub_import_test_...`-databases en verwijdert alleen die eigen databases na afloop. De schema-SQL moet vooraf uit de actuele EF-migraties zijn gegenereerd.

## Acceptatiecriteria

| Criterium | Bewijs |
|---|---|
| Bron ongewijzigd, alleen testdatabase | Hashcontrole, naamcontrole in Python/SQL, lokale verbinding en afzonderlijke testdatabases |
| Aantallen en afwijzingsredenen in verslag | Full-import- en mixed-rejections-verslagen, per bestand en per afgewezen record |
| Aantallen en voorraadtotalen sluiten aan | SQL-controle binnen de transactie, meetwaarden in verslag en onafhankelijke vergelijking van alle velden |
| Herhaling zonder stille duplicaten | Herhaalde import geweigerd, alle velden en aantallen vergeleken; nieuwe lege database als beschreven herstartstrategie |

De implementatie is gereed voor teamreview. Schatting en eigenaar blijven onderdeel van jullie sprintplanning. Deze proef importeert niets naar productie en is geen vrijgave voor de pilot.
