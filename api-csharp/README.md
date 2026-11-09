# CargoHub C# API

ASP.NET Core (.NET 10) met EF Core en Npgsql voor PostgreSQL. De databasebasis is toegevoegd in CH-012. De API bevat controllers voor warehouses, locations, items, inventories en de artikelclassificaties item_groups, item_lines en item_types.

## Starten en migreren

Vereist: .NET 10 SDK en een bereikbare PostgreSQL-database. Stel `ConnectionStrings__CargoHub` in via de omgeving, met host, database, gebruikersnaam en wachtwoord. Commit geen echte verbindingsgegevens.

Je kunt de verbinding voor lokale ontwikkeling ook eenmalig bewaren met .NET User Secrets. Zowel de API in Development als de EF-migratiecommando's lezen die instelling. Omgevingsvariabelen hebben voorrang.

```sh
dotnet user-secrets set 'ConnectionStrings:CargoHub' 'Host=localhost;Port=5432;Database=cargohub;Username=JOUW_GEBRUIKER;Password=JOUW_WACHTWOORD' --project api-csharp
```

Vervang de voorbeeldwaarden door je eigen verbinding. De waarde wordt buiten Git opgeslagen. Na wijzigen van de verbinding moet je de API herstarten. De fout `Configure ConnectionStrings__CargoHub` betekent dat de verbinding nog niet is ingesteld; Swagger heeft dezelfde database nodig als iedere andere API-client.

Vanuit de repositoryroot:

```sh
dotnet tool restore
dotnet ef database update --project api-csharp
dotnet run --project api-csharp --launch-profile http
```

De API gebruikt `http://localhost:5268`. Migraties worden bewust apart uitgevoerd, niet automatisch bij iedere API-start. De bestaande Python-API staat in `api/` en kan apart worden gestart met `./start-system.sh`.

## Oude JSON data laden

Voer na de migraties de eenmalige beginimport uit tegen dezelfde database:

```sh
psql -h localhost -p 5432 -U postgres -d cargohub -f database/postgresql/import-legacy-json.sql
```

Pas de verbindingsgegevens aan jullie server aan. Alle vijftien zakelijke tabellen moeten leeg zijn. De import volgt dit EF Core-model en vult 80.465 records, inclusief de oude IDs en timestamps. Order- en verzenddatums worden volgens de `DateOnly`-properties naar een UTC-datum omgezet. `user.json` valt buiten dit model en wordt niet geladen. Zie [importinstructies](../database/postgresql/IMPORT-ANMOL.md).

## Databasecontroles

Start de lokale, uitsluitend voor tests bedoelde PostgreSQL-service:

```sh
docker compose -f compose.schema-tests.yml up -d --wait
```

Stel daarna `CARGOHUB_TEST_ADMIN_CONNECTION` in op:

```text
Host=localhost;Port=55432;Database=postgres;Username=cargohub_test;Password=ch012-local-disposable
```

Dit is uitsluitend het bekende wachtwoord van de geïsoleerde, tijdelijke testcontainer. Gebruik deze configuratie niet voor productie.

```sh
dotnet run --project tests/CargoHub.SchemaChecks
docker compose -f compose.schema-tests.yml down
```

De verifier maakt een unieke lege database en verwijdert die na de test. Hij controleert migraties, sleutels, relaties, statuswaarden en constraints met echte PostgreSQL-opdrachten. De testcontainer heeft geen permanent volume: hij bevat alleen wegwerpgegevens.

Zie [CH-012: schema, besluiten en verificatie](../documentatie/CH-012-schema.md) en het [ERD](../documentatie/diagrammen/database_erd_efcore.md). De goedgekeurde schemaregels en de nog te bouwen procesregels staan expliciet in dat document.

## Artikelclassificaties controleren

Met dezelfde aparte PostgreSQL-testinstantie en `CARGOHUB_TEST_ADMIN_CONNECTION`:

```sh
dotnet run --project tests/CargoHub.ClassificationChecks
```

Deze verifier maakt een eigen tijdelijke database, past de bestaande migraties toe en start de echte API. Hij controleert de classificatie-CRUD, snake_case-JSON, blijvende opslag na API-herstarts en de aansluiting op de bestaande controllers en filters. De database en API-processen worden na afloop opgeruimd. Er zijn geen extra NuGet-testpakketten nodig. Zie [CH-029: routes en testresultaten](../documentatie/CH-029-item-classifications.md).

## Swagger en presentatiedemo

Start de API met de ingestelde PostgreSQL-verbinding en de bestaande migraties:

```sh
dotnet run --project api-csharp --launch-profile http
```

Open **http://localhost:5268/swagger**. De startprofielen openen deze pagina automatisch. Alle 77 huidige methode-padcombinaties staan gegroepeerd per onderwerp en auteur, inclusief de oude voorraadroute die altijd 404 geeft. Swagger toont de parameters en invoerschema’s. De oorspronkelijke demoacties en Anmols zending- en transferroutes bevatten daarnaast ingevulde voorbeelden en uitleg van het gedrag. Met **Try it out** en **Execute** stuur je echte aanvragen en zie je de statuscode en het antwoord van de server. Het OpenAPI-bestand staat op `/swagger/v1/swagger.json`.

Swagger staat standaard aan in Development. Voor een expliciete demo buiten Development stel je `Swagger__Enabled=true` in. Met `Swagger__Enabled=false` kun je het uitschakelen. De documentatie voegt geen authenticatie toe; die ontbreekt momenteel nog in de API.

### Korte demonstratie voor de docent

Gebruik een aparte demodatabase. Controleer eerst met GET dat ID **900001** nog vrij is. De voorbeelden zijn fictief en worden pas opgeslagen wanneer je een schrijfactie uitvoert.

1. Open **Artikelgroepen**, kies `POST /api/v1/item_groups` en klik **Try it out**. Laat de voorbeeldbody staan en klik **Execute**. Verwacht **201**, zonder responsebody.
2. Kies `GET /api/v1/item_groups/{id}`, vul **900001** in en voer uit. Verwacht **200**, met de groep en timestamps van de server.
3. Kies `PUT /api/v1/item_groups/{id}`, gebruik **900001** en de ingevulde voorbeeldbody met de gewijzigde naam. Verwacht **200**, zonder responsebody. Haal de groep opnieuw op om de wijziging te tonen.
4. Kies `DELETE /api/v1/item_groups/{id}` met **900001**. Verwacht **200**. Een volgende GET geeft **200** met `null`, zoals in Python.
5. Laat eventueel een fout zien: voer dezelfde POST tweemaal uit voordat je de groep verwijdert. De tweede aanvraag geeft **409**, omdat het ID al bestaat. Ruim daarna je eigen demogroep op.

Voorbeelden voor magazijnen, locaties, artikelen en voorraad staan er ook in. Maak bij een gekoppelde demo eerst de bovenliggende records aan: magazijn, locatie, artikel en daarna voorraad. Het locatievoorbeeld verwijst standaard naar magazijn **1**; pas dit aan naar **900001** als je het demomagazijn gebruikt. Het artikelvoorbeeld verwijst naar bestaande classificaties en leverancier **1**; kies IDs uit jullie eigen database. Het voorraadvoorbeeld gebruikt demoartikel en demolocatie **900001**. Verwijder na afloop eerst voorraad, daarna artikel en locatie, en als laatste magazijn. `commodity_code` moet in de huidige C#-API als tekst worden verstuurd.

De documentatie toont ook de bestaande beperkingen: enkele databasefouten geven nu nog **500**. Swagger verandert deze afhandeling en de API-logica niet.

### Wie heeft welk onderdeel gemaakt?

De Swagger-groepen vermelden de auteur op basis van de controllercommits. Magazijnen, locaties, artikelen, voorraad en de leveranciers-artikelroute zijn van Artur. De CRUD voor artikelgroepen, artikellijnen en artikeltypen is van Jeffrey Doornbos. Klanten, leveranciers-CRUD, orders en orderregels zijn van Ihssane, inclusief `GET /clients/{id}/orders`. De gekoppelde `GET /{id}/items`-routes binnen de drie classificatiegroepen en bij leveranciers zijn van Artur. Elk endpoint vermeldt daarom ook afzonderlijk **Gemaakt door**. Zendingen en transfers zijn van Anmol Haribhajan; beide groepen en hun 16 acties vermelden zijn naam.

## Transfers (Sprint 2)

De zeven transferroutes zijn toegevoegd, inclusief commit, gekoppelde itemregels en transactionele voorraadverwerking volgens CH-010. Zie [gedrag, compatibiliteit en testbewijs](../documentatie/Sprint-2-transfers.md). Er is geen nieuwe database-migratie nodig. Test met `dotnet run --project tests/CargoHub.TransferChecks` op de afzonderlijke testdatabase.

## Shipments (Sprint 3)

De negen shipmentroutes zijn toegevoegd, inclusief orderlijsten en voorraadcorrecties bij wijziging van regels. Zie [gedrag, migratie en testbewijs](../documentatie/Sprint-3-shipments.md). Voer de nieuwe EF-migratie uit vóór het starten; `shipment_date` behoudt nu het volledige tijdstip.
### Zendingen en transfers demonstreren

Voer eerst `dotnet ef database update --project api-csharp` uit tegen jullie eigen lokale database. De zendingmigratie bewaart `shipment_date` voortaan als tijdstip. Voorbeeldverzoeken staan onder **Zendingen - Anmol** en **Transfers - Anmol**. Gebruik een vrij demo-ID en controleer vooraf de relaties en beschikbare voorraad. Het transfervoorbeeld gebruikt artikel **119**, bronlocatie **124** en bestemming **1** uit de oude importdata. Met `PUT /transfers/{id}/commit` wordt voorraad echt verplaatst; herhalen verplaatst niet opnieuw. Een verwerkte transfer kan niet meer worden gewijzigd of verwijderd. Gebruik hiervoor een aparte demodatabase.
