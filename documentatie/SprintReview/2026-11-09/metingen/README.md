# Metingen CargoHub 9 november 2026

`resultaten.json` bevat de omgeving, methode, 24 samenvattingen, 72 rondes en 7.200 individuele meetrequests. `metingen.csv` en `samenvatting.csv` bieden dezelfde meetgegevens als tabel.

Vier routes zijn getest bij 1, 10 en 25 clients, per combinatie 3 rondes met 100 requests, na 100 opwarmrequests per implementatie en route. Beide implementaties zijn om de beurt op dezelfde laptop gemeten. Alle responses zijn op status en een vooraf gevalideerde inhoudshash gecontroleerd. Medianen en P95 zijn over 300 requests berekend; P95 gebruikt nearest rank. Doorvoer is 300 gedeeld door de totale ronde-duur, inclusief clientoverhead.

Python: oorspronkelijke TCPServer, backlog 5, JSON-data en validatie van de tijdelijke testsleutel. C#: Release/Production, Kestrel, EF Core en PostgreSQL, zonder sleutelvalidatie. Geen TLS, nieuwe TCP-verbinding per request. Dit is een vergelijking van deze implementaties met warme caches, geen geïsoleerde taalbenchmark.

`benchmark-bron.py` is de bron van deze specifieke meetrun, met machinegebonden paden en tijdelijke clusterconfiguratie. Niet direct uitvoeren: de gebruikte tijdelijke cluster is na afloop gestopt. `uitvoer.log` bevat de runuitvoer. `import.log` bevat de import van 80.465 zakelijke records. De oorspronkelijke JSON-bronbestanden bleven ongewijzigd.

`demo-resultaten.json` bevat een aanvullende geautomatiseerde HTTP-test met eigen voorraadrecords in een afzonderlijke tijdelijke database. `demo-voorbereiding.json` beschrijft die fixture. De voorraad is ook na een API-procesherstart gecontroleerd. Deze uitgebreide controle is geen vereiste voor de korte Swagger-demonstratie.

Validatie: alle 7.200 requests hebben status 200 en geldige inhoud; alle 24 medianen, P95-waarden en doorvoercijfers zijn onafhankelijk uit de ruwe meetgegevens herberekend.
