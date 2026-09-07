# CargoHub endpointonderzoek

Uitgevoerd op 7 september 2026. Start vanuit de projectmap:

```sh
python3 documentatie/onderzoek-tests/test_endpoints.py
```

Python 3.10 of nieuwer, geen externe pakketten nodig. Het script start de echte API-handler op een tijdelijke localhost-poort en gebruikt tijdelijke JSON-bestanden en eigen testsleutels. Projectdata wordt uitsluitend gelezen. Orders, shipments en transfers zijn beperkt tot de eerste twee records met hun regels; stamgegevens en voorraad worden volledig gekopieerd.

`resultaten.json` bevat 92 HTTP-metingen met responses, 35 inhoudelijke GET-vergelijkingen en twee modelcontroles. Alle 73 gedocumenteerde methode-padcombinaties worden aangeroepen. De overige aanvragen controleren ontbrekende routes, fouten, toegang, JSON-vormen en opnieuw lezen na schrijven.

Uitkomst: 35 van 35 positieve leesresponses komen overeen met de fixture. Bij 34 standaard schrijfopdrachten en vier aanvullende PUT-opdrachten blijven de gegevens ongewijzigd ondanks 200/201. Een object zonder timestamps geeft 201; een array, string en lege body geven 500. Modelcontrole bevestigt automatisch aangemaakte timestamps en verlies van de wijziging bij een nieuwe providerinstantie.

Dit is een herhaalbare onderzoeksmeting van het bestaande gedrag. Exitcode 0 betekent dat het script voltooid is; het betekent niet dat de API voldoet aan acceptatiecriteria. Validatie, alle combinaties van rechten, herstarts, concurrency en belasting zijn niet volledig getest. Voer voor CI afzonderlijke acceptatietests in die het gewenste gedrag afdwingen.

De UML-diagrammen en hun bewerkbare Python-renderer staan in `../diagrammen/onderzoek-uml/`. De renderer gebruikt Pillow en de macOS Arial-lettertypen.
