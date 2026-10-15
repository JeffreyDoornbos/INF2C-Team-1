# CargoHub C# API

ASP.NET Core (.NET 10) met EF Core en Npgsql voor PostgreSQL. De databasebasis is toegevoegd in CH-012. Er zijn nog geen controllers: een HTTP-aanvraag geeft voorlopig 404.

## Starten en migreren

Vereist: .NET 10 SDK en een bereikbare PostgreSQL-database. Stel `ConnectionStrings__CargoHub` in via de omgeving, met host, database, gebruikersnaam en wachtwoord. Commit geen echte verbindingsgegevens.

Vanuit de repositoryroot:

```sh
dotnet tool restore
dotnet ef database update --project api-csharp
dotnet run --project api-csharp --launch-profile http
```

De API gebruikt `http://localhost:5268`. Migraties worden bewust apart uitgevoerd, niet automatisch bij iedere API-start. De bestaande Python-API staat in `api/` en kan apart worden gestart met `./start-system.sh`.

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
