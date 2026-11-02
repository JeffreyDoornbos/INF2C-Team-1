# CH-013 — Importverslag

Status: **imported**. Tijd: 2026-11-02T13:29:27.898578+00:00

| Bestand | Gelezen | Geaccepteerd | Geïmporteerd | Afgewezen |
|---|---:|---:|---:|---:|
| warehouse.json | 1 | 1 | 1 | 0 |
| client.json | 1 | 1 | 1 | 0 |
| supplier.json | 1 | 1 | 1 | 0 |
| item_line.json | 1 | 1 | 1 | 0 |
| item_group.json | 1 | 1 | 1 | 0 |
| item_type.json | 1 | 1 | 1 | 0 |
| location.json | 2 | 2 | 2 | 0 |
| item.json | 1 | 1 | 1 | 0 |
| inventory.json | 2 | 0 | 0 | 2 |
| order.json | 1 | 1 | 1 | 0 |
| order_item.json | 2 | 1 | 1 | 1 |
| shipment.json | 1 | 1 | 1 | 0 |
| shipment_item.json | 1 | 0 | 0 | 1 |
| transfer.json | 1 | 0 | 0 | 1 |
| transfer_item.json | 1 | 0 | 0 | 1 |

**Totaal:** read=18, accepted=12, imported=12, rejected=6

Bronhashes ongewijzigd: **True**.

## Voorraadreconciliatie

| Hoeveelheid | Geaccepteerde bron | Database |
|---|---:|---:|
| quantity_on_hand | 0 | 0 |
| quantity_expected | 0 | 0 |
| quantity_ordered | 0 | 0 |
| quantity_allocated | 0 | 0 |
| quantity_available | 0 | 0 |

## Afwijzingen

Rijnummers beginnen bij 1 in de JSON-array; keys bevatten alleen technische IDs.
- inventory.json, rij 1, key ['1', '1']: duplicate_primary_key
- inventory.json, rij 2, key ['1', '1']: duplicate_primary_key
- order_item.json, rij 2, key ['2']: missing_or_rejected_reference:item_id->item
- shipment_item.json, rij 1, key ['1']: amount_must_be_positive
- transfer.json, rij 1, key ['1']: unknown_status:transfer_status
- transfer_item.json, rij 1, key ['1']: missing_or_rejected_reference:transfer_id->transfer

Bij herhaling: gebruik een nieuwe lege testdatabase; er is geen automatische reset of upsert.
Bronhashes, omzettingen en volledige details staan in het bijbehorende JSON-verslag.
