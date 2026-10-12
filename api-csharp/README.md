# CargoHub C# API

Leeg ASP.NET Core API-project voor CargoHub, met .NET 10 en ondersteuning voor controllers. Er zijn nog geen endpoints of databasekoppelingen toegevoegd.

## Starten

Voer vanuit de hoofdmap van de repository uit:

```sh
dotnet run --project api-csharp/CargoHub.Api.csproj --launch-profile http
```

De API draait op `http://localhost:5268`. Zolang er geen controllers met endpoints zijn toegevoegd, geeft een aanvraag `404 Not Found` terug.

Voeg nieuwe controllers toe in deze projectmap. De bestaande Python-API staat in `api/` en kan apart worden gestart met `./start-system.sh`.

## Bouwen

```sh
dotnet build api-csharp/CargoHub.Api.csproj
```
