# CH-029 Artikelclassificaties migreren naar C#

## Resultaat

De CRUD-functionaliteit van `api/models/item_groups.py`, `item_lines.py` en `item_types.py` is overgezet naar drie ASP.NET Core-controllers. Ze gebruiken dezelfde dependency injection, asynchrone EF Core-opslag en globale snake_case-JSON-instellingen als Arturs controllers.

De bestaande `GET .../{id}/items`-routes blijven in `ItemsController`. Er zijn geen dubbele routes toegevoegd en er zijn geen wijzigingen aan Arturs controllers nodig. Het bestaande PostgreSQL-model en de migraties blijven leidend; er is geen nieuwe migratie nodig.

## Koppeling met de Python-functies

| Python | C# |
| --- | --- |
| `get_item_groups`, `get_item_lines`, `get_item_types` | De lijstacties op de drie controllers; EF Core-query met `AsNoTracking` en sortering op ID. |
| `get_item_group`, `get_item_line`, `get_item_type` | De detailacties met een integer-ID uit het pad. |
| `add_item_group`, `add_item_line`, `add_item_type` | De POST-acties; timestamps worden op de server aangemaakt en `SaveChangesAsync` slaat de records op. |
| `update_item_group`, `update_item_line`, `update_item_type` | De PUT-acties; naam en omschrijving worden bijgewerkt, evenals `updated_at`. |
| `remove_item_group`, `remove_item_line`, `remove_item_type` | De DELETE-acties; verwijderen wordt met `SaveChangesAsync` vastgelegd. |
| `load` en constructor | Het bestaande `CargoHubDbContext` en de drie toegevoegde `DbSet`-properties vervangen het laden van losse JSON-bestanden. |
| `save` | `SaveChangesAsync` op dezelfde context die de wijziging uitvoert. De eerdere fout met opnieuw aangemaakte providers wordt niet overgenomen. |
| De debuglijsten `ITEM_GROUPS`, `ITEM_LINES`, `ITEM_TYPES` | Geïsoleerde testfixtures in een tijdelijke PostgreSQL-database, buiten de productiecode. |
| Artikel-ID's per classificatie | Arturs bestaande acties in `ItemsController`, gekoppeld aan `ItemGroupId`, `ItemLineId` en `ItemTypeId`. |

## Routes en HTTP-antwoorden

De onderstaande routes gelden voor elk van de drie resources: `item_groups`, `item_lines` en `item_types`. Dit zijn achttien methode-padcombinaties, waarvan drie bestaande `/items`-routes.

| Methode | Pad | Succesantwoord |
| --- | --- | --- |
| GET | `/api/v1/{resource}` | `200`, JSON-array met classificatieobjecten; `[]` als er geen records zijn. |
| GET | `/api/v1/{resource}/{id}` | `200`, classificatieobject. Bij onbekend ID: `200` met JSON `null`, zoals de oorspronkelijke Python-route. |
| GET | `/api/v1/{resource}/{id}/items` | `200`, JSON-array van artikel-ID's. Geen volledige artikelobjecten. Onbekende classificatie: `[]`. |
| POST | `/api/v1/{resource}` | `201`, lege body. Een opgegeven ID wordt behouden; zonder ID genereert PostgreSQL een ID. |
| PUT | `/api/v1/{resource}/{id}` | `200`, lege body. Bij onbekend ID blijft de aanvraag een no-op met `200`. |
| DELETE | `/api/v1/{resource}/{id}` | `200`, lege body. Bij onbekend of al verwijderd ID blijft dit `200`. |

Voorbeeld van een classificatieobject, met dezelfde vijf veldnamen als in Python:

```json
{
  "id": 1,
  "name": "Vers",
  "description": "Temperature/handling regime: Vers",
  "created_at": "2025-08-01T15:30:26+00:00",
  "updated_at": "2025-08-15T20:20:03+00:00"
}
```

`+00:00` en `Z` geven hetzelfde UTC-tijdstip aan. De tests vergelijken timestamps op tijdstip, niet op de precieze schrijfwijze.

## Aansluiting en foutafhandeling

- De drie nieuwe controllers zijn bewust expliciet opgebouwd zoals de bestaande controllers. Er is geen nieuwe generieke controllerbasis of opslaglaag toegevoegd.
- De nieuwe classificatieroutes behouden de oorspronkelijke Python-succesresponses. Arturs bestaande warehouse- en itemcontrollers retourneren op sommige plaatsen andere antwoorden, bijvoorbeeld `404` bij een onbekend ID en een object bij POST. Die routes worden in deze taak niet aangepast.
- Een POST genereert `created_at` en `updated_at` op de server, ook als een client eigen timestamps meestuurt. Een PUT bewaart het ID uit het pad en de oorspronkelijke `created_at`; een meegestuurd ander ID verandert dus geen ander record.
- De bestaande PostgreSQL-constraints blijven actief. Een dubbel ID geeft `409 Conflict`. Een classificatie die nog door artikelen wordt gebruikt, kan niet worden verwijderd en geeft `409`. De record en relaties blijven behouden.
- Ongeldige JSON, een array of `null` als body en expliciet `null` voor een verplicht tekstveld geven `400`, via de bestaande `[ApiController]`-validatie. Dezelfde entity-invoer als bij de bestaande controllers wordt gebruikt; extra regels voor bijvoorbeeld niet-lege namen zijn geen onderdeel van deze taak.
- Niet-numerieke IDs passen niet op `{id:int}` en geven `404`, zoals bij Arturs controllers. Niet-ondersteunde methodes geven `405` en onbekende subroutes `404`.
- Authenticatie en API-sleutelbeheer worden in deze branch niet toegevoegd. De huidige C#-opstartcode bevat hiervoor nog geen middleware; dit is apart werk in CH-020. De oorspronkelijke Python-API blijft behouden.

## Testopzet

De console-verifier `tests/CargoHub.ClassificationChecks` volgt de bestaande schemaverifier. Hij vereist alleen de bestaande projectdependencies en een aparte PostgreSQL-instantie met CREATEDB-rechten.

De verifier:

1. Maakt een database met een unieke naam `ch029_<guid>` aan en controleert dat het model en de migrationsnapshot gelijk zijn.
2. Past de bestaande EF Core-migraties toe en laadt fixtures uit de ongewijzigde JSON-bestanden: 3 artikelgroepen, 14 artikellijnen, 3 artikeltypen, 600 artikelen, 21 leveranciers, 10 magazijnen, 400 locaties en 4.800 voorraadregels.
3. Start het echte API-programma in een apart proces op een vrije lokale poort. Er is geen nagebouwde testhost of in-memory database.
4. Voert de HTTP-aanvragen uit en controleert ook opslag via een onafhankelijke PostgreSQL-verbinding. Voor iedere classificatieresource wordt het API-proces echt opnieuw gestart om blijvende opslag te bewijzen.
5. Controleert de bestaande classificatie-`/items`-routes, de vier bestaande collectieroutes en de drie bestaande artikelfilters.
6. Stopt de gestarte API-processen en verwijdert alleen de eigen tijdelijke database, ook bij een fout. Bronbestanden en bestaande databases worden niet gewijzigd.

## Zelf uitvoeren

Gebruik de bestaande wegwerpcontainer, of een eigen uitsluitend voor tests bestemde lokale PostgreSQL-instantie:

```sh
docker compose -f compose.schema-tests.yml up -d --wait
export CARGOHUB_TEST_ADMIN_CONNECTION='Host=localhost;Port=55432;Database=postgres;Username=cargohub_test;Password=ch012-local-disposable'
dotnet run --project tests/CargoHub.ClassificationChecks
dotnet run --project tests/CargoHub.SchemaChecks
docker compose -f compose.schema-tests.yml down
```

Dit wachtwoord hoort alleen bij de bestaande lokale wegwerpcontainer. Gebruik geen productieverbinding voor de verificatie. De controles staan in een uitvoerbaar consoleproject, zoals de huidige schemacontroles; ze worden uitgevoerd met `dotnet run`.

## Uitgevoerde resultaten

Datum: 2 november 2026. Omgeving: .NET SDK 10.0.401, EF Core 10.0.13, Npgsql EF-provider 10.0.3 en een tijdelijke lokale PostgreSQL 16.14-instantie. Docker was op deze laptop niet gestart; voor deze run is PostgreSQL rechtstreeks met `initdb` en `pg_ctl` gestart.

| Controle | Uitkomst |
| --- | --- |
| Build van API en classificatieverifier | Geslaagd, 0 waarschuwingen en 0 fouten. |
| Nieuwe classificatiecontroles | 71 van 71 geslaagd, via echte HTTP en PostgreSQL. |
| Bestaande schemacontroles | 72 van 72 geslaagd, inclusief migraties, foreign keys en constraints. |
| Alle originele classificatierecords lezen | Groepen, lijnen en typen komen qua veldnamen, waarden en tijdstippen overeen met de JSON-fixtures. |
| CRUD en timestamps | Alle drie de resources geslaagd; updates en verwijderingen zijn ook via volgende aanvragen zichtbaar. |
| API-processen opnieuw starten | Gegevens blijven voor alle drie de resources behouden. |
| Foutgevallen en foreign keys | Gecontroleerde 400/404/405/409-antwoorden; geen onbedoelde gegevenswijzigingen. |
| Aansluiting op Arturs werk | Drie bestaande classificatie-`/items`-routes en zeven regressiecontroles voor collecties en filters geslaagd. |

De 71 controles bevatten per resource een aantal samengestelde scenario's; dit aantal is geen telling van unieke endpoints. De uitvoer staat in [classificatie-testresultaten](classificatie-tests/CH-029-resultaten.txt) en [schemacontroles](classificatie-tests/CH-029-schema-resultaten.txt).
