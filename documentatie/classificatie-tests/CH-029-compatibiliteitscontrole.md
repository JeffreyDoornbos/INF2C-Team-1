# Aanvullende controle Python en C#

Datum: 2 november 2026. Branch: `feature/ch-029-item-classifications`.

De normale CRUD-acties werken voor de geteste controllers. De C#-API is nog niet volledig gelijk aan de oorspronkelijke Python-API. Hieronder staan ook daadwerkelijk aangetroffen tekortkomingen; een geslaagde auditcontrole betekent dat een waarneming is bevestigd, niet dat het betreffende gedrag correct is.

## Uitvoering

De oorspronkelijke Python-API en de echte C#-API zijn lokaal naast elkaar gestart. Python gebruikte een kopie van de JSON-bestanden en een tijdelijke testsleutel. C# gebruikte een afzonderlijke PostgreSQL 16.14-testdatabase met de bestaande EF Core-migraties en de legacy-import. De bronbestanden bleven ongewijzigd. Er zijn geen tests op de Ubuntu-server of een bestaande database uitgevoerd.

De aanvullende audit bevestigde 47 functionele controles en waarnemingen:

- De zeven collecties warehouses, locations, items, inventories, item_groups, item_lines en item_types geven dezelfde aantallen bronrecords terug. Dit bewijst op zichzelf geen volledige gelijkheid van alle velden.
- Aanmaken, ophalen, wijzigen en verwijderen van magazijnen, locaties en artikelen werkt in C#. Wijzigingen zijn zichtbaar via volgende HTTP-aanvragen; `created_at` blijft bij updates behouden. Voor het artikel was aanpassing van `commodity_code` naar tekst nodig.
- Voorraad aanmaken, updaten via POST, wijzigen via PUT en verwijderen werkt op de samengestelde sleutel artikel/locatie. De voorraadtotalen en gekoppelde locatie-, voorraad- en classificatieroutes verwerken de nieuwe records.
- De onderstaande afwijkingen en foutgevallen zijn via echte HTTP-aanvragen bevestigd.

## Bevindingen

| Onderdeel | Python | Huidige C# | Beoordeling |
| --- | --- | --- | --- |
| `GET /api/v1/warehouses` zonder sleutel | `401` | `200` met gegevens | Authenticatie ontbreekt in C#. Dit geldt als apart openstaand werk, ook voor de nieuwe classificatiecontrollers. |
| `commodity_code` van een artikel | Getal in de brondata en responses | Tekst | Dezelfde oorspronkelijke JSON-body geeft bij POST in Python `201`, in C# `400`. Dit is een compatibiliteitsprobleem. |
| Onbekend magazijn, locatie of artikel ophalen | `200` met `null` | `404` | Veranderd API-gedrag. `404` kan een bewuste verbetering zijn, maar is geen exacte overzetting. |
| Onbekend magazijn, locatie of artikel wijzigen/verwijderen | `200`, lege body | `404` | Veranderd API-gedrag. |
| POST magazijn, locatie, artikel of voorraad | `201`, lege body | `201`, object | Status gelijk, responsebody verschilt. |
| PUT bestaand magazijn, locatie of artikel | `200`, lege body | `200`, object | Status gelijk, responsebody verschilt. |
| Magazijn, locatie of artikel verwijderen met bestaande relaties | `200` | `500` | PostgreSQL beschermt de relaties, maar de fout wordt niet afgehandeld. Geef bijvoorbeeld `409`. |
| Dubbel magazijn-ID aanmaken | `201` | `500` | Databasefout wordt niet afgehandeld. Geef bijvoorbeeld `409`. |
| Locatie met onbekend magazijn aanmaken | `201` | `500` | Databasefout wordt niet afgehandeld. Valideer de verwijzing en geef een gecontroleerde `400` of `409`. |
| Negatieve voorraad opslaan | `201` | `500` | Constraint weigert de invoer terecht, maar een gecontroleerde `400` ontbreekt. |

## CH-029-classificaties

Voor item_groups, item_lines en item_types zijn daarnaast de 71 bestaande classificatiecontroles opnieuw uitgevoerd. Die controleren CRUD, alle oorspronkelijke classificatierecords, veldnamen, timestamps, opslag na een procesherstart, foutafhandeling en de aansluiting op Arturs bestaande `/items`-routes.

De classificatiecontrollers behouden de oorspronkelijke succesresponses, inclusief `200` met `null` bij een onbekend ID en lege bodies bij schrijven. Constraintconflicten geven gecontroleerde `409`-antwoorden. Authenticatie ontbreekt nog op applicatieniveau en wordt door deze controllers dus ook niet afgedwongen.

De oude Python-schrijfafhandeling maakt providers opnieuw aan, waardoor een succesvol antwoord niet garandeert dat een wijziging is opgeslagen. De C#-tests controleren opslag apart; deze oude fout hoeft niet te worden overgenomen.

## Vervolg

1. Voeg authenticatie en autorisatie toe volgens de afgesproken aparte taak.
2. Beslis welk JSON-type `commodity_code` moet hebben en behoud zo nodig ondersteuning voor bestaande numerieke invoer.
3. Handel databaseconflicten en ongeldige invoer gecontroleerd af.
4. Leg bewuste wijzigingen van statuscodes en responsebodies vast en pas de clienttests daarop aan.

De API-code is tijdens deze aanvullende audit niet aangepast.
