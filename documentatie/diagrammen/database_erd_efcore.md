# CargoHub ERD — migratiebasis

Gegenereerd uit het EF Core-model. Niet handmatig aanpassen; zie CH-012-schema.md.

```mermaid
erDiagram
    client {
        int id PK
        text address
        text city
        text contact_email
        text contact_name
        text contact_phone
        text country
        timestamptz created_at
        text name
        text province
        timestamptz updated_at
        text zip_code
    }
    inventory {
        int item_id PK,FK
        int location_id PK,FK
        timestamptz created_at
        int quantity_allocated
        int quantity_expected
        int quantity_on_hand
        int quantity_ordered
        timestamptz updated_at
    }
    item ||--o{ inventory : "item_id / RESTRICT"
    location ||--o{ inventory : "location_id / RESTRICT"
    item {
        int id PK
        text barcode
        int case_size
        text code
        text commodity_code
        timestamptz created_at
        text description
        int item_group_id FK
        int item_line_id FK
        int item_type_id FK
        int min_purchase_qty
        text model_number
        int order_multiple
        text packaging_type
        int supplier_id FK
        text supplier_sku
        decimal unit_weight
        timestamptz updated_at
    }
    item_group ||--o{ item : "item_group_id / RESTRICT"
    item_line ||--o{ item : "item_line_id / RESTRICT"
    item_type ||--o{ item : "item_type_id / RESTRICT"
    supplier ||--o{ item : "supplier_id / RESTRICT"
    item_group {
        int id PK
        timestamptz created_at
        text description
        text name
        timestamptz updated_at
    }
    item_line {
        int id PK
        timestamptz created_at
        text description
        text name
        timestamptz updated_at
    }
    item_type {
        int id PK
        timestamptz created_at
        text description
        text name
        timestamptz updated_at
    }
    location {
        int id PK
        text code
        timestamptz created_at
        text name
        timestamptz updated_at
        int warehouse_id FK
    }
    warehouse ||--o{ location : "warehouse_id / RESTRICT"
    order {
        int id PK
        int bill_to_client_id FK
        int client_id FK
        timestamptz created_at
        text customer_po_number
        date order_date
        text order_status
        text reference
        date request_date
        int ship_to_client_id FK
        text shipping_notes
        timestamptz updated_at
        int warehouse_id FK
    }
    client ||--o{ order : "bill_to_client_id / RESTRICT"
    client ||--o{ order : "client_id / RESTRICT"
    client ||--o{ order : "ship_to_client_id / RESTRICT"
    warehouse ||--o{ order : "warehouse_id / RESTRICT"
    order_item {
        int id PK
        int amount
        int item_id FK
        int order_id FK
        decimal unit_price
    }
    item ||--o{ order_item : "item_id / RESTRICT"
    order ||--o{ order_item : "order_id / RESTRICT"
    shipment {
        int id PK
        text carrier_name
        timestamptz created_at
        int order_id FK
        text payment_type
        text reference
        date shipment_date
        text shipment_status
        text shipment_type
        text shipping_method
        timestamptz updated_at
    }
    order |o--o{ shipment : "order_id / RESTRICT"
    shipment_item {
        int id PK
        int amount
        int item_id FK
        int shipment_id FK
    }
    item ||--o{ shipment_item : "item_id / RESTRICT"
    shipment ||--o{ shipment_item : "shipment_id / RESTRICT"
    supplier {
        int id PK
        text address
        text city
        text code
        text contact_name
        text country
        timestamptz created_at
        text name
        text phone_number
        text province
        text reference
        timestamptz updated_at
        text zip_code
    }
    transfer {
        int id PK
        timestamptz created_at
        int from_location_id FK
        text reference
        int to_location_id FK
        text transfer_status
        timestamptz updated_at
    }
    location ||--o{ transfer : "from_location_id / RESTRICT"
    location ||--o{ transfer : "to_location_id / RESTRICT"
    transfer_item {
        int id PK
        int amount
        int item_id FK
        int transfer_id FK
    }
    item ||--o{ transfer_item : "item_id / RESTRICT"
    transfer ||--o{ transfer_item : "transfer_id / RESTRICT"
    warehouse {
        int id PK
        text address
        text city
        text code
        text contact_email
        text contact_name
        text contact_phone
        text country
        timestamptz created_at
        text name
        text province
        timestamptz updated_at
        text zip_code
    }
```

Alle kolommen zijn verplicht behalve `order.shipping_notes` en `shipment.order_id`. `inventory` heeft één samengestelde primary key: `(item_id, location_id)`.

## Check constraints

- `inventory.ck_inventory_allocation_within_stock`: `quantity_allocated <= quantity_on_hand`
- `inventory.ck_inventory_nonnegative`: `quantity_on_hand >= 0 AND quantity_expected >= 0 AND quantity_ordered >= 0 AND quantity_allocated >= 0`
- `order.ck_order_status`: `order_status IN ('Pending', 'Shipped')`
- `order_item.ck_order_item_nonnegative_price`: `unit_price >= 0`
- `order_item.ck_order_item_positive_amount`: `amount > 0`
- `shipment.ck_shipment_status`: `shipment_status IN ('Scheduled', 'Transit', 'Delivered')`
- `shipment_item.ck_shipment_item_positive_amount`: `amount > 0`
- `transfer.ck_transfer_different_locations`: `from_location_id <> to_location_id`
- `transfer.ck_transfer_status`: `transfer_status IN ('Scheduled', 'Processed')`
- `transfer_item.ck_transfer_item_positive_amount`: `amount > 0`

## Unieke combinaties

- `transfer_item`: `(transfer_id, item_id)`

## Standaardwaarden

- Enkelvoudige `id`-kolommen: PostgreSQL identity by default.
- `created_at` en `updated_at`: `CURRENT_TIMESTAMP` bij invoegen; updates wijzigen `updated_at` niet automatisch.
- `transfer_status`: `Scheduled`.
