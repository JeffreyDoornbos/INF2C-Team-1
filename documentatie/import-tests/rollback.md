# CH-013 — Importverslag

Status: **not_imported**. Tijd: 2026-11-02T13:29:32.211262+00:00

| Bestand | Gelezen | Geaccepteerd | Geïmporteerd | Afgewezen |
|---|---:|---:|---:|---:|
| warehouse.json | 10 | 10 | 0 | 0 |
| client.json | 300 | 300 | 0 | 0 |
| supplier.json | 21 | 21 | 0 | 0 |
| item_line.json | 14 | 14 | 0 | 0 |
| item_group.json | 3 | 3 | 0 | 0 |
| item_type.json | 3 | 3 | 0 | 0 |
| location.json | 400 | 400 | 0 | 0 |
| item.json | 600 | 600 | 0 | 0 |
| inventory.json | 4800 | 4800 | 0 | 0 |
| order.json | 4854 | 4854 | 0 | 0 |
| order_item.json | 26498 | 26498 | 0 | 0 |
| shipment.json | 6132 | 6132 | 0 | 0 |
| shipment_item.json | 33660 | 33660 | 0 | 0 |
| transfer.json | 800 | 800 | 0 | 0 |
| transfer_item.json | 2370 | 2370 | 0 | 0 |

**Totaal:** read=80465, accepted=80465, imported=0, rejected=0

Bronhashes ongewijzigd: **True**.

## Voorraadreconciliatie

| Hoeveelheid | Geaccepteerde bron | Database |
|---|---:|---:|
| quantity_on_hand | 1191191 | niet bevestigd |
| quantity_expected | 487761 | niet bevestigd |
| quantity_ordered | 355960 | niet bevestigd |
| quantity_allocated | 599322 | niet bevestigd |
| quantity_available | 591869 | niet bevestigd |

## Afwijzingen

Rijnummers beginnen bij 1 in de JSON-array; keys bevatten alleen technische IDs.
Geen records afgewezen.

## Uitvoeringsmelding

SQL import refused or rolled back; no records committed by this run.

Bij herhaling: gebruik een nieuwe lege testdatabase; er is geen automatische reset of upsert.
Bronhashes, omzettingen en volledige details staan in het bijbehorende JSON-verslag.
