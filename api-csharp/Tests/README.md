# C# tests voor CI/CD

Deze map bevat 40 xUnit-integratietests. Ze starten de bestaande API in een
ASP.NET Core-testhost en sturen requests door de HTTP-pipeline. De opslag gebruikt
echte PostgreSQL, zodat relaties, constraints en transacties ook getest worden.
Boven iedere test staat een Nederlandse XML-`summary` met het doel van de test.

## Wat wordt getest

| Bestand | Controle |
| --- | --- |
| `Integration/ApiSmokeTests.cs` | Leesroutes van alle 12 hoofdonderdelen, JSON-veldnamen, 404-responses, migraties en Swagger |
| `Integration/ClassificationTests.cs` | CRUD van groepen, lijnen en typen, timestamps, opslag via een nieuwe API-host, ongeldige JSON en 409-conflicten |
| `Integration/TransferTests.cs` | Voorraadverplaatsing, reserveringen, herhaalde en gelijktijdige commits, rollback en ongeldige hoeveelheden |
| `Infrastructure/` | Testhost en tijdelijke PostgreSQL-database |

De bestaande 25 unittests in `tests/CargoHub.Api.Tests` blijven apart bestaan.
`CargoHub.sln` bevat de API en beide testprojecten. Samen draaien er 65 tests.
Dit is een gerichte regressieset, geen volledige dekking van alle schrijfroutes.
De uitgebreidere bestaande consolechecks in `tests/CargoHub.*Checks` blijven
beschikbaar en maken geen onderdeel uit van deze solution.

## Lokaal starten

Voer deze commando's uit vanuit de hoofdmap van de repository. Nodig: .NET 10
en Docker met Compose.

```bash
docker compose -f api-csharp/Tests/compose.yml up -d --wait
export CARGOHUB_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Port=55439;Database=postgres;Username=cargohub_ci;Password=local_test_password'
dotnet test CargoHub.sln --configuration Release
```

Alleen de nieuwe integratietests:

```bash
dotnet test api-csharp/Tests/CargoHub.Api.IntegrationTests.csproj --configuration Release
```

Alleen de bestaande unittests, zonder PostgreSQL:

```bash
dotnet test tests/CargoHub.Api.Tests/CargoHub.Api.Tests.csproj --configuration Release
```

Gebruik je al een aparte PostgreSQL-testserver, dan kun je het Docker-commando
overslaan en de omgevingsvariabele daarop instellen. De gebruiker moet databases
kunnen aanmaken. Zonder deze instelling falen de integratietests met een uitleg;
ze worden niet stilzwijgend overgeslagen.

De fixture maakt een unieke database `cargohub_ci_<guid>`, past de bestaande
EF-migraties toe en voegt kleine testgegevens toe. Na afloop verwijdert hij alleen
deze zelf aangemaakte database, ook bij normale testfouten. Bij een abrupt gestopt
proces kan de tijdelijke database achterblijven. De tests gebruiken nooit
`ConnectionStrings__CargoHub` of de lokale demo-data en laden geen legacy-JSON.

Na afloop kun je de aparte Docker-testserver met zijn testopslag verwijderen:

```bash
docker compose -f api-csharp/Tests/compose.yml down -v
```

## Pipeline

`.github/workflows/csharp-tests.yml` draait bij pull requests, pushes naar `main`
en handmatige starts in GitHub Actions:

1. Start PostgreSQL 16 als tijdelijke service en installeer .NET 10.
2. Restore en build de solution in Release.
3. Voer beide testprojecten uit met `dotnet test`.
4. Bewaar de `.trx`-testresultaten als downloadbaar artifact.

Een mislukte test laat de job falen. De workflow bouwt en test uitsluitend.
Een latere deploymentjob kan `needs: test` gebruiken en zo pas na geslaagde tests
starten. Er zijn geen productiegegevens of deploymentwachtwoorden nodig.

De testmap is uitgesloten van de API-build en publicatie. Testresultaten,
`bin` en `obj` worden niet opgenomen in Git. Responstijden en percentielen zijn
geen harde CI-grens: gedeelde runners kunnen sterk wisselende prestaties hebben.
