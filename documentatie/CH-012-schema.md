# CH-012 — PostgreSQL-schema en EF Core-migratie

## Doel en scope

De eerste stap is het bestaande CargoHub-datamodel overzetten naar PostgreSQL als basis voor de C#-API. Deze wijziging bevat het schema, de databasekoppeling, de EF Core-migratie, het ERD en controleerbare databasetests. De controllers, JSON-import en transferverwerking worden in hun eigen backlogtaken gebouwd. Een werkend schema betekent dus nog niet dat alle API-operaties zijn gemigreerd.

Het model bevat de vijftien bestaande zakelijke resources: warehouse, location, supplier, item_line, item_group, item_type, item, inventory, transfer, transfer_item, client, order, order_item, shipment en shipment_item. De bestaande JSON-veldnamen worden als snake_case-kolomnamen behouden. API-sleutels en rechten blijven onderdeel van CH-020; deze migratie bevat geen echte sleutels of andere seedgegevens.

## Sleutels en relaties

- Iedere tabel heeft een primary key. `inventory` gebruikt samen `item_id` en `location_id`; er is geen extra inventory-ID.
- Alle foreign keys verwijzen naar bestaande records. Een locatie hoort bij een magazijn. Voorraad verwijst naar een artikel en locatie. Transferregels verwijzen naar een transfer en artikel; een transfer heeft een bron- en bestemmingslocatie.
- Artikelen verwijzen naar leveranciers, artikelgroepen, artikellijnen en artikeltypen.
- Orders verwijzen naar een magazijn en naar de klant, afleverklant en factuurklant. Order- en zendingsregels verwijzen naar hun ouderrecord en artikel.
- Een shipment heeft nul of één order. Eén order kan meerdere shipments hebben. Dit volgt de opslag en het ERD; de opmerking `1:1` in de oude Python-code is geen afgedwongen unieke relatie. De bestaande v1-lijstweergave kan de API later blijven aanbieden.
- ID-kolommen gebruiken identity by default. De latere JSON-import moet na expliciete IDs de sequences bijwerken; anders kan een volgende automatisch gegenereerde ID botsen.
- `shipping_notes` en `shipment.order_id` zijn nullable. Datums zonder tijd gebruiken `date`; tijdstempels gebruiken `timestamp with time zone`. Het importproces moet de tijdzone van oude tijdstempels expliciet bepalen.
- Geldbedragen en gewichten gebruiken decimal/numeric. De gekozen precisie is respectievelijk (18,4) en (18,6); het importproces moet waarden buiten dat bereik rapporteren.

## Goedgekeurde schemaregels — CH-010

- Alle foreign keys gebruiken RESTRICT; verwezen records kunnen niet automatisch verdwijnen.
- Transferstatus: Scheduled (standaard) of Processed. Orderstatus: Pending of Shipped. Shipmentstatus: Scheduled, Transit of Delivered.
- Voorraadhoeveelheden zijn niet-negatief en allocated mag on_hand niet overschrijden.
- Transferregels zijn positief; een artikel komt maximaal eenmaal per transfer voor, afgedwongen door een unieke index op `(transfer_id, item_id)`.
- Bron en bestemming verschillen. Order- en zendingsregels hebben positieve aantallen; unit_price is niet-negatief.
- Shipment.order_id blijft nullable en niet uniek: één order kan meerdere zendingen hebben.

Een bestaande database met overallocatie of dubbele transferregels weigert de tweede migratie. Er wordt niets stilzwijgend aangepast of verwijderd.
**Afbakening:** databasecontroles bewaken waarden en relaties. Statusovergangen, minimaal één transferregel, verbod op wijzigen/verwijderen van Processed-transfers, HTTP-foutcodes en atomische, idempotente verwerking vallen onder CH-015/CH-016. Deze schemataak claimt die procesregels niet te implementeren. Capaciteit en prestatie-/hersteldoelen blijven open in CH-010; ze verhinderen geen schematest maar wel een onderbouwd pilotbesluit.

## ERD onderhouden

`diagrammen/database_erd_efcore.md` wordt rechtstreeks uit het EF Core-model gegenereerd, inclusief kolommen, sleutels, relaties, nullability en check constraints. Het oude PNG-bestand blijft als historische bron behouden.

Vanuit de repositoryroot:

```sh
dotnet run --project tests/CargoHub.SchemaChecks -- --erd documentatie/diagrammen/database_erd_efcore.md
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project api-csharp
```

Een wijziging in het model vereist een nieuwe migratie én opnieuw genereren van het ERD. De verificatie vergelijkt het model met de migrationsnapshot.

## Database opbouwen

Vereist: .NET 10 SDK en PostgreSQL. Zet `ConnectionStrings__CargoHub` in de lokale omgeving op de verbindingsstring van de doelomgeving. Bewaar het wachtwoord buiten de repository.

```sh
dotnet tool restore
dotnet ef database update --project api-csharp
```

De API past migraties niet automatisch bij het starten toe. Migraties zijn een bewuste beheerhandeling. `CreatedAt` en `UpdatedAt` krijgen bij invoegen een standaardtijd; de latere opslaglaag moet `UpdatedAt` bij wijzigingen aanpassen.

## Onafhankelijke schemacontroles

Gebruik een afzonderlijke testinstantie, geen productiedatabase. Stel `CARGOHUB_TEST_ADMIN_CONNECTION` in op een account met CREATEDB-rechten. De test maakt een uniek benoemde lege database aan en verwijdert alleen die database na afloop, ook bij een fout.

```sh
dotnet run --project tests/CargoHub.SchemaChecks
```

De controles voeren echte INSERT/UPDATE/DELETE-opdrachten uit tegen PostgreSQL. Ze controleren lege-databasemigratie, herhaald toepassen, alle primary keys, alle foreign keys, RESTRICT-verwijderregels, verplichte sleutels, hoeveelheden, statuswaarden, nullable shipment-orderkoppeling en het terugdraaien en opnieuw toepassen van de migratie. De test is geen test van de toekomstige HTTP-endpoints.

## Uitgevoerde verificatie — 15 oktober 2026

- Build van API en verifier: geslaagd, 0 waarschuwingen en 0 fouten.
- .NET SDK 10.0.402; EF Core en dotnet-ef 10.0.13; Npgsql EF-provider 10.0.3.
- Echte PostgreSQL 17-container, image digest `sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3`.
- **72 schemacontroles geslaagd vanuit het PyCharm-project**, waaronder lege-databasemigratie, upgrades met bestaande data, weigering van overallocatie en dubbele transferregels, en terugdraaien/opnieuw toepassen. De eerdere run met 64 controles blijft als historisch bewijs bewaard.
- Iedere testrun gebruikte een eigen tijdelijke database, die na afloop is verwijderd.
- [Actuele controle-uitvoer](schema-tests/resultaten-goedgekeurde-regels.txt).
- Dit zijn nieuwe schemacontroles; ze worden niet opgeteld bij de historische 88 HTTP-aanvragen of andere eerdere testtellingen.

## Acceptatie

1. **Lege database:** beide migraties slagen op een lege PostgreSQL-database.
2. **Unieke voorraadcombinatie, PK's en FK's:** bewezen met echte PostgreSQL SQLSTATE-controles.
3. **CH-010-regels:** gebruikersakkoord geregistreerd; statuswaarden, verwijderregels en aanvullende schemacontroles geïmplementeerd. Procesregels blijven expliciet bij CH-015.
4. **ERD en migratie gelijk:** ERD opnieuw uit het model gegenereerd; EF meldt geen modelwijzigingen sinds de laatste migratie.

## Technische bronnen
- [Npgsql EF Core-provider](https://www.npgsql.org/efcore/)
- [Microsoft: sleutels en relaties](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/foreign-and-principal-keys)
- [Microsoft: EF Core-migraties](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [CH-010 op GitHub](https://github.com/JeffreyDoornbos/INF2C-Team-1/issues/10)

