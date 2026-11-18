# CargoHub beheerdashboard

URL: **https://145.24.237.22/beheer/**

HTTP `/beheer` verwijst naar HTTPS. De bestaande HTTP-API blijft werken.
Ook Swagger is via HTTPS bereikbaar op `/api/v2/swagger/index.html`.

## Inloggen

Gebruikersnaam: `groep1`. Het willekeurig gegenereerde initiële wachtwoord is
uitsluitend door root op de VPS te lezen:

```bash
ssh ubuntu-1098684@145.24.237.22
sudo cat /root/cargohub-dashboard-initial-password.txt
```

Kopieer dit niet naar Git. Een eigen wachtwoord instellen:

```bash
sudo python3 /opt/cargohub-dashboard/set-password.py
```

Dit vraagt het wachtwoord interactief, slaat uitsluitend een salted scrypt-hash
op, verwijdert het bestand met het initiële wachtwoord en logt bestaande sessies uit.
Na een wijziging staat het nieuwe wachtwoord dus niet meer in dat bestand.

## Functies

- Host: CPU-percentage, RAM, schijfruimte, load en uptime sinds de serverstart.
- Services: Python V1, actieve C# V2, PostgreSQL, Nginx, GitHub-runner en V2-reserveslot.
- Docker-containers: CPU, geheugen, processtatus en uptime sinds de containerstart.
- C# V2 en Python V1 starten, stoppen en herstarten met een bevestigingsvenster.
- PostgreSQL, Nginx, runner en reserveslot zijn alleen leesbaar via het dashboard.
- De actuele V2-slot en release worden uit `deployment.json` gelezen; geen hardcoded blue/green-keuze.

De browser haalt om de vijf seconden status op. Het serverproces verzamelt een
meting en wacht daarna vijf seconden; Docker-stats voegt enkele seconden meettijd
toe. De CPU-grafiek toont metingen tijdens de huidige browsersessie, geen
blijvende historische monitoring. `Actief` betekent dat het proces draait;
dit is geen bewijs dat alle zakelijke API-functies correct werken.

## Inrichting

```text
Browser
  -> HTTPS :443 met publiek vertrouwd Let's Encrypt IP-certificaat
  -> aparte Nginx-container cargohub-admin-https-1
  -> dashboard op het private Docker-gateway-adres :8765
  -> beperkt sudo-commando cargohub-dashboard-control
  -> uitsluitend toegestane Docker- en systemd-acties
```

Het dashboard draait als `cargohub-monitor` zonder algemene sudo- of
Docker-groepsrechten. Sudo staat zeven exacte opdrachten toe: status en
start/stop/restart voor V1 en V2. De broker is root-owned en accepteert geen
arbitraire commando's, container-ID's of bestandspaden. Beheeracties delen een
lock met de releaseontvanger, zodat ze niet tegelijk met een deployment lopen.
Stoppen van de actieve V2 schakelt niet naar het reserveslot: de API blijft
dan uit totdat iemand hem weer start of een nieuwe versie uitrolt.

Login gebruikt een salted scrypt-hash en een willekeurige serversessie van
acht uur. De cookie heeft Secure, HttpOnly en SameSite=Strict. Mutaties vereisen
de juiste HTTPS Origin en een CSRF-token. Zes loginpogingen per vijf minuten
zijn toegestaan per client-IP. Uitloggen of een serverprocesherstart beëindigt
de sessie. Er is momenteel één gedeeld teamaccount, geen rollen of MFA.

Alleen de ACME-challenge is zonder login publiek leesbaar; die bevat geen
applicatiegegevens. De backend luistert op de Docker-bridge, niet op het
publieke serveradres. Configuratie staat in `/etc/cargohub-dashboard/config.json`
met root/monitor-rechten 640. De root-only initiële login staat nooit in de repo.

## HTTPS en certificaatvernieuwing

Let's Encrypt ondersteunt publieke IP-certificaten met het profiel `shortlived`.
Certbot 5.4+ ondersteunt hiervoor HTTP-01 via webroot. De bestaande HTTP-proxy
stuurt alleen `/.well-known/acme-challenge/` door naar de challenge-handler.
Zie [de officiële Certbot-uitleg](https://letsencrypt.org/2026/03/11/shorter-certs-certbot).

De IP-certificaten zijn circa zes dagen geldig. De systemd-timer
`cargohub-certificate-renew.timer` controleert tweemaal per dag of vernieuwing
nodig is. Een deploy-hook controleert en herlaadt de HTTPS-proxy. Port 80 moet
bereikbaar blijven voor de HTTP-01-challenge en poort 443 voor HTTPS.

```bash
sudo systemctl list-timers cargohub-certificate-renew.timer
sudo /opt/cargohub-certbot/bin/certbot renew --dry-run --run-deploy-hooks
```

## Installeren of bijwerken

De installatie staat los van het C# releasepakket en verandert geen API-code of
databasegegevens. Kopieer deze map via SSH naar de VPS en voer als beheerder uit:

```bash
sudo bash /tmp/cargohub-dashboard-install/install.sh
sudo python3 /tmp/cargohub-dashboard-install/configure-proxy.py
sudo bash /tmp/cargohub-dashboard-install/enable-https.sh
```

`install.sh` bewaart een bestaande login. `configure-proxy.py` maakt een back-up,
voegt alleen de HTTPS-redirect en certificaatchallenge toe en valideert Nginx
voor een reload. De route staat ook in het C# Nginx-template voor toekomstige
serverinstallaties. Het HTTPS-Compose-project is `cargohub-admin`; de bestaande
API-containers blijven in `cargohub-v2`.

```bash
sudo systemctl status cargohub-dashboard --no-pager
sudo journalctl -u cargohub-dashboard -n 30 --no-pager
sudo journalctl -t cargohub-dashboard -n 20 --no-pager
sudo docker compose -f /opt/cargohub-dashboard/https-compose.yml ps
```

## Testen

```bash
python3 -m unittest discover -s deployment/dashboard/tests -v
node --check deployment/dashboard/static/app.js
```

De tests controleren login, sessies, rate limiting, Origin, CSRF, verboden acties,
uitloggen, afgeschermde status en padtraversal. Acties zijn gemockt: de tests
stoppen geen echte applicaties. De eigen Actions-workflow voert deze tests uit
op een GitHub-hosted runner; hij installeert geen dashboard op PR-branches.

Bij de eerste uitrol zijn HTTPS, echte login, live metingen, de onschadelijke
startactie op de al draaiende V2 en het weigeren van een database-stop gecontroleerd.
De browsercontrole test desktop, mobiel, uitloggen en het annuleren van een stopactie.
