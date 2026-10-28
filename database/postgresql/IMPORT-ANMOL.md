# Oude JSON data importeren in Anmols datamodel

Anmols C#-entities en EF Core-migraties bepalen de tabellen en constraints. `import-legacy-json.sql` vult de vijftien zakelijke tabellen in het schema `public` met 80.465 records uit `data/*.json`. De brondata is in het SQL-bestand opgenomen. PostgreSQL hoeft de JSON-bestanden niet zelf te kunnen lezen.

## Uitvoeren

Gebruik een lege doeldatabase en stel de verbinding in voor jullie PostgreSQL-server. Bijvoorbeeld voor een lokale database met de naam `cargohub` en gebruiker `postgres`:

```sh
export ConnectionStrings__CargoHub='Host=localhost;Port=5432;Database=cargohub;Username=postgres;Password=VUL_JE_WACHTWOORD_IN'
dotnet tool restore
dotnet ef database update --project api-csharp
psql -h localhost -p 5432 -U postgres -d cargohub -f database/postgresql/import-legacy-json.sql
```

Voer dit uit vanuit de hoofdmap van de repository. Beide opdrachten moeten naar dezelfde server en database verwijzen. `psql` kan om het databasewachtwoord vragen. Gebruik `psql` voor het importbestand: het bevat psql-opdrachten en `COPY FROM stdin`, die niet in iedere grafische SQL-editor werken.

Het importscript maakt geen database of tabellen aan. De twee EF Core-migraties moeten eerst zijn uitgevoerd. Het script controleert dit voordat het data invoegt. Migraties worden niet automatisch uitgevoerd bij het starten van de API.

## Werking

- Eerst worden de doeltabellen vergrendeld voor andere schrijvers en gecontroleerd op bestaande gegevens. Zodra een van de vijftien tabellen gevuld is, stopt het script zonder data te wijzigen. Het is een eenmalige beginimport en geen synchronisatie of upsert.
- Stamgegevens worden vóór de verwijzende records geladen. De foreign keys, unieke transferregels en overige constraints van Anmols schema blijven actief.
- Alle invoer, controles op aantallen en resets van identity-sequences zitten in één transactie. Bij een fout wordt de import teruggedraaid. Er worden geen records verwijderd, aangepast of stilzwijgend overgeslagen.
- Na invoer worden de identity-sequences ingesteld boven de hoogste bestaande IDs. De laatste uitvoer toont het aantal records per tabel.

## Omzettingen naar het model

- `order_date`, `request_date` en `shipment_date` zijn `DateOnly`/`date` in Anmols model. De import gebruikt de UTC-datum uit de bron. De tijd binnen die dag wordt niet in deze datumkolommen opgeslagen; de originele waarden blijven in de JSON-bronbestanden staan.
- `created_at` en `updated_at` behouden het oorspronkelijke tijdstip en worden als UTC-timestamps ingevoerd.
- `item.commodity_code` is tekst in het C#-model en wordt daarom vanuit het JSON-getal naar tekst omgezet.
- `unit_price` gebruikt numeric(18,4) en `unit_weight` numeric(18,6). De generator weigert waarden die buiten dit bereik vallen of afgerond zouden worden.
- `user.json` met de zes applicaties, API-sleutels en rechten wordt niet geïmporteerd. Daarvoor bestaat geen entity/tabel in Anmols model; dit valt onder CH-020. De import bevat daardoor 80.465 zakelijke records, in plaats van de 80.471 records inclusief de zes applicaties.

De Python-API, JSON-bronbestanden en Anmols model en migraties blijven behouden.

## Opnieuw genereren na wijzigingen aan de JSON

```sh
python3 database/postgresql/generate_legacy_import.py
```

De generator leest de propertynamen en datatypes uit Anmols C#-entities en gebruikt dezelfde snake_case-naamgeving als `CargoHubDbContext`. Bij onverwachte of ontbrekende bronvelden stopt hij. Hij past het datamodel niet aan en maakt geen nieuwe migraties.

`legacy-import-manifest.json` bevat aantallen, bronhashes en de toegepaste omzettingen. Controleer na modelwijzigingen opnieuw of de generator en migratiebasis nog overeenkomen en test de import in een lege database.

Gebruik voor deze C#-database **niet** het oudere `cargohub.sql` of `schema.sql`. Die horen bij het eerdere zelfstandige SQL-model met een apart `cargohub`-schema.

## Uitgevoerde controle

Op 28 oktober 2026 is de import getest in een tijdelijke PostgreSQL 16.14-instantie. De tabellen zijn opgebouwd met het SQL-script dat EF Core uit Anmols twee bestaande migraties genereert.

- Alle 80.465 records zijn per veld met de JSON-bronnen vergeleken, rekening houdend met de hierboven beschreven datum- en tekstomzettingen.
- De identity-sequences zijn gecontroleerd; een nieuw magazijn kreeg correct ID 11 na de tien geïmporteerde magazijnen.
- Opnieuw importeren werd geweigerd zonder bestaande aantallen te veranderen.
- Importeren zonder de vereiste migraties werd geweigerd.
- Een bewust ongeldige orderregel halverwege de import draaide alle eerdere invoer terug. Alle vijftien tabellen bleven leeg en de identity-sequences ongewijzigd. Daarna slaagde de correcte import.
- De bronhashes kwamen na de tests nog overeen met de JSON-bestanden.
