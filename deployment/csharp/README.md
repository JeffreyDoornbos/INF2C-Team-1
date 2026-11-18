# CargoHub V2 met Docker Compose en SSH

De C# API draait naast de bestaande Python API. PostgreSQL 16 bewaart de data
in een Docker-volume. Een Nginx-container stuurt `/api/v1` naar de bestaande
Python-service op de hostpoort 8400 en `/api/v2` naar C#.

## Adressen

- V2: `http://145.24.237.22/api/v2`
- Voorbeeld: `http://145.24.237.22/api/v2/warehouses`
- Swagger: `http://145.24.237.22/api/v2/swagger/index.html`
- OpenAPI: `http://145.24.237.22/api/v2/swagger/v2/swagger.json`
- Dezelfde proxy luistert ook op poort 3000 en 8401. Gebruik poort 80 wanneer
  de schoolfirewall poort 3000 tegenhoudt.
- V1 blijft daarnaast rechtstreeks op de bestaande poort 8400 beschikbaar.

De C# API gebruikt momenteel geen API-sleutelauthenticatie. De V1-sleutelcontrole
blijft bij de Python-service. De testdata is bedoeld voor het schoolproject.
PostgreSQL heeft geen publiek gepubliceerde poort.

## Pipeline

`.github/workflows/csharp-tests.yml` voert bij pushes en pull requests een
Release-build, unittests en PostgreSQL-integratietests uit. GitHub toont de echte
testaantallen en coverage per testproject in de job summary. TRX en Cobertura
zijn downloadbare artifacts.

Alleen een geslaagde run op `main` maakt vervolgens een releasepakket, bijvoorbeeld
`CargoHub_API_version_2.0.42.1.zip`. Het pakket bevat de Linux-publicatie,
idempotente EF-migraties, de actuele JSON-beginimport en een checksummanifest.
De pipeline publiceert het pakket als GitHub Release en artifact.

Build en tests draaien op GitHub-hosted runners. De uiteindelijke deployjob draait
op de aparte `cargohub-vps`-runner op de VPS. Deze worker maakt een uitgaande
HTTPS-verbinding met GitHub, omdat de schoolfirewall inkomende SSH-verbindingen
vanaf GitHub-cloudrunners tegenhoudt. De worker downloadt het geteste artifact en
uploadt het pakket **via SSH op poort 8422** naar `cargohub-deploy@145.24.237.22`.
De sleutel staat uitsluitend in het repository-secret `VPS_DEPLOY_KEY`.
`known_hosts` bevat de via de bestaande SSH-verbinding gecontroleerde publieke
hostkey. De deploysleutel mag alleen de release-ontvanger uitvoeren: geen
interactieve shell, poortforwarding of algemene sudo-toegang. De aparte listener
accepteert alleen deze deploygebruiker en sleutel, geen wachtwoorden. Hij gebruikt
poort 8422 omdat de GitHub-runner poort 22 niet kan bereiken. De bestaande
beheerdersverbinding op poort 22 blijft ongewijzigd. De worker draait als de
afzonderlijke gebruiker `cargohub-runner`, zonder Docker-groep of sudo-rechten.
Alleen de beperkte SSH-releaseontvanger mag de daadwerkelijke uitrol uitvoeren.
De workflow en een job-start-hook beperken de VPS-worker tot de eigen repository
en `main`; builds en pull-request-tests draaien niet op de VPS-worker.

## Uitrol en behoud van data

1. De ontvanger verifieert paden, pakketgrootte en SHA-256 van ieder bestand.
2. Compose start PostgreSQL en wacht tot deze gereed is.
3. Bij een bestaande database maakt de uitrol eerst een `pg_dump`-backup.
4. EF-migraties worden toegepast. De 80.465 zakelijke JSON-records worden
   uitsluitend bij de eerste beginimport geladen. Latere deployments bewaren data.
5. Docker bouwt het runtime-image en start de inactieve blauwe of groene API-slot.
6. Databaseverbinding, magazijnen en V2 Swagger worden via HTTP gecontroleerd.
7. Pas daarna herlaadt Nginx de configuratie naar de nieuwe slot. De vorige
   container blijft beschikbaar om lopende requests af te ronden.

De proxy blijft tijdens een update draaien. Als de kandidaat niet gereed komt,
blijft de oude proxyconfiguratie actief. Bij een mislukte proxycontrole wordt de
vorige configuratie teruggezet. Databasemigraties worden niet automatisch
teruggedraaid: gebruik voor toekomstige releases achterwaarts compatibele
migraties. Destructieve migraties vereisen een afzonderlijk onderhoudsplan.

## Beheer via SSH

```bash
ssh ubuntu-1098684@145.24.237.22
sudo tail -n 100 -f /var/log/cargohub-deploy.log
cd /opt/cargohub-v2
sudo docker compose ps
sudo docker compose logs --tail 50 api-blue api-green
sudo cat /opt/cargohub-v2/deployment.json
sudo systemctl status 'actions.runner.*'
```

De server bewaart `.env` met een willekeurig gegenereerd databasewachtwoord als
root-only bestand. Commit dit bestand en de private SSH-sleutel nooit.
Backups staan in `/opt/cargohub-v2/backups`; releasebestanden in
`/opt/cargohub-v2/releases`. Gebruik geen `docker compose down -v`: daarmee
zou je de persistente database verwijderen.

## Eerste installatie en handmatige uitrol

Installeer op Ubuntu Docker en Compose, kopieer deze deploymentmap naar de VPS
en voer via de bestaande beheerders-SSH-verbinding uit:

```bash
sudo bash deployment/csharp/install-server.sh /pad/naar/deploy-key.pub
```

Bouw daarna lokaal of op een Linux-builder een release en upload die:

```bash
bash deployment/csharp/build-release.sh 2.0.0.1 /tmp/cargohub-release
ssh -T -p 8422 -i /pad/naar/deploy-key cargohub-deploy@145.24.237.22 \
  < /tmp/cargohub-release/CargoHub_API_version_2.0.0.1.zip
```

De eerste installatie maakt nieuwe V2-bestanden en wijzigt de bestaande
Python-code, JSON-data en systemd-service niet. Docker start de containers na
een herstart opnieuw met `restart: unless-stopped`.

Voor een nieuwe VPS registreert een repositorybeheerder ook de deployrunner met
`install-runner.py`, een kortlevend GitHub-runnerregistratietoken en de officiële
runner-downloadmetadata inclusief SHA-256. Kopieer geen persoonlijk GitHub-token
of VM-wachtwoord naar de worker. GitHub toont de worker onder Settings > Actions
> Runners. De runner-service start automatisch na een herstart.
