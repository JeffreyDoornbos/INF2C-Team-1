# CargoHub PostgreSQL database

## CH-013: gecontroleerde proefimport

Gebruik voor de CH-013-acceptatie [import_test_json.py](import_test_json.py), met een lege testdatabase en een verslag van geaccepteerde/afgewezen records. Zie de [CH-013-handleiding en testresultaten](../../documentatie/CH-013-import.md). De bestaande generator en SQL hieronder blijven beschikbaar voor de eerdere strikte beginimport; zij dwingen niet dezelfde testdatabasebeperking af.

## Eerdere strikte import voor Anmols EF Core model

Gebruik **[import-legacy-json.sql](import-legacy-json.sql)** na het uitvoeren van Anmols EF Core-migraties. Dit bestand importeert de oude JSON-data in zijn bestaande `public`-tabellen. Zie **[IMPORT-ANMOL.md](IMPORT-ANMOL.md)** voor de stappen en omzettingen. Dit is de leidende databasebasis voor de C#-API.

## Oud zelfstandig SQL model

De onderstaande beschrijving en de bestanden `cargohub.sql`, `schema.sql` en `generate_sql.py` horen bij het eerdere zelfstandige model. Gebruik deze niet om de nieuwe C#-database op te bouwen; ze blijven alleen als historische bestanden behouden.

`cargohub.sql` maakt de database `cargohub` aan als deze nog niet bestaat. Daarna maakt het script het schema `cargohub` met 16 tabellen aan en importeert het alle 80.471 bestaande records uit de JSON-bestanden. De data zit in het SQL-bestand zelf; PostgreSQL hoeft de JSON-bestanden dus niet te kunnen benaderen.

## Uitvoeren

Gebruik `psql` vanuit de hoofdmap van de repository:

```sh
psql -h localhost -p 5432 -U postgres -d postgres -f database/postgresql/cargohub.sql
```

Pas de host, poort en gebruikersnaam aan voor jullie PostgreSQL-server. De databasegebruiker moet databases mogen aanmaken, of de database `cargohub` moet vooraf zijn aangemaakt. Het script gebruikt psql-opdrachten zoals `\connect` en `COPY FROM stdin` en moet daarom met `psql` worden uitgevoerd.

De tabellen en gegevens worden in één transactie geïmporteerd. Bij een fout wordt deze import teruggedraaid; de vooraf aangemaakte database kan wel leeg blijven bestaan. Het script verwijdert of overschrijft geen bestaande tabellen. Opnieuw uitvoeren in een database met een bestaand schema `cargohub` geeft een fout en laat de bestaande gegevens intact.

## Schema en brongegevens

- `schema.sql` bevat alleen het schema en de tabellen, zonder database-aanmaak of voorbeelddata.
- `cargohub.sql` bevat het volledige schema en alle brongegevens.
- `import-manifest.json` bevat de aantallen en SHA256-hashes per bronbestand.
- `generate_sql.py` genereert de SQL-bestanden opnieuw vanuit de actuele `data/*.json`.

```sh
python3 database/postgresql/generate_sql.py
```

Tabelnamen en kolomnamen volgen de JSON-bestanden. Gebruik dubbele aanhalingstekens bij de tabellen `order` en `user`, omdat deze woorden een speciale betekenis hebben in SQL:

```sql
SELECT * FROM cargohub."order" LIMIT 10;
SELECT * FROM cargohub.warehouse;
```

De tabellen hebben primaire sleutels, foreign keys en controles op positieve aantallen en niet-negatieve voorraad. Voor `inventory` is de primaire sleutel `(item_id, location_id)`. Bestaande IDs, timestamps, nullwaarden, decimalen en relaties worden behouden. De ID-sequences worden na de import ingesteld zodat nieuwe IDs boven de geïmporteerde IDs beginnen.

Locatiecodes en orderreferenties zijn niet uniek in de brongegevens en krijgen daarom geen unieke constraint. Een order kan meerdere zendingen hebben. Het script legt geen unieke constraint op `shipment.order_id` op. `updated_at` wordt tijdens de import behouden; bij latere wijzigingen moet de C#-API dit veld bijwerken.

De zes applicaties uit `user.json` worden ook geïmporteerd, inclusief de bestaande API-sleutels en rechten in `endpoint_access` als JSONB. De sleutels blijven hiermee leesbaar, net als in de brondata. Behandel dit SQL-bestand als een bestand met inloggegevens; vervang deze sleutels voor productie en implementeer veilige opslag in de nieuwe API.

De Python-API en de JSON-bestanden worden niet aangepast. De databasekoppeling in de C#-API kan daarna worden toegevoegd.

## Controle van de import

Het volledige script is op 12 oktober 2026 uitgevoerd in een tijdelijke PostgreSQL 16.14-database. Alle 80.471 records zijn per veld vergeleken met de JSON-bronnen. Timestamps zijn voor de vergelijking naar UTC genormaliseerd. De ID-sequences zijn gecontroleerd. Ontbrekende foreign keys, negatieve regelaantallen en dubbele voorraadkeys werden door de database afgewezen. Opnieuw uitvoeren werd afgewezen doordat het schema al bestaat, zonder de bestaande records te vervangen.
