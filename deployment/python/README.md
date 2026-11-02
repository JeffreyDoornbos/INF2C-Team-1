# Python API op Ubuntu installeren

Open een nieuw **lokaal** terminalvenster op je Mac. Voer vanuit de repository uit:

```sh
bash deployment/python/deploy.sh ubuntu-1098684@145.24.237.22
```

Je voert je SSH-wachtwoord rechtstreeks in je terminal in. Bij installatie kan de server daarnaast om je sudo-wachtwoord vragen. Er is een Ubuntu-account met sudo-rechten nodig. Het script stopt als de installatie al bestaat of poort 3000 bezet is.

De bestanden `api/` en `data/` worden via SCP gekopieerd. De oorspronkelijke Python-code wordt niet aangepast. De server heeft Python 3.10 of nieuwer nodig; externe Python-pakketten zijn voor deze API niet nodig.

De API start via een bashscript als systemd-service `cargohub-python`. Deze blijft draaien na het sluiten van SSH, herstart bij een crash en start na een serverreboot automatisch. De data staat in `/opt/cargohub-python/data`. Alleen het serviceaccount kan deze bestanden lezen en schrijven.

Het script controleert op de server een GET met en zonder API-sleutel. Vervolgens controleert het vanaf je Mac of de API bereikbaar is. Een actieve UFW-firewall krijgt een regel voor TCP-poort 3000. Een firewall van de VPS-aanbieder of het schoolnetwerk moet die poort ook toestaan.

## Bereikbaarheid controleren

De URL is:

```text
http://145.24.237.22:3000/api/v1/warehouses
```

Zonder header `API_KEY` geeft de oorspronkelijke API `401 Unauthorized`. Gebruik in Bruno een bestaande sleutel uit je lokale `data/user.json` om de gegevens op te vragen. Een GET naar alleen `/` is geen geschikte gezondheidscontrole voor deze oorspronkelijke router.

## Beheer op de server

```sh
sudo systemctl status cargohub-python
sudo journalctl -u cargohub-python -n 50 --no-pager
sudo systemctl restart cargohub-python
```

De installatie is pas bevestigd nadat het script meldt dat de externe controle is geslaagd. Post daarna zelf de API-link in jullie teamkanaal.
