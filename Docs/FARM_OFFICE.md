# Farm office

A breadth-first farm-management layer beside guidance. The guidance host is unchanged:
the 100 Hz loop is not asked to parse yield, soil, or chat, and this layer does not call
`IAutoSteerService`, does not call `ISectionControlService`, and does not send PGN 254,
239, 229, or 241.

Open it from the running host at `http://<host>:5174/farm/` (phone or browser). The
guidance screen’s file menu has **Farm summary** (same numbers, and a once-a-second
read-only log of the status frame while that panel is open) and **Open farm office**.

The book is `Farm/farm.json` under the AgOpenWeb data root (`AGOPENWEB_DATA`, otherwise
Documents). Sample data is labeled `sample-fixture`. **Reset the sample book** on the
Import screen restores it. Field folders created by import stay under `Fields/`.

## What is not connected to a machine

These screens say so in the page, and the API refuses the matching actions:

- Passive strip-till / next-year lines. Stored and drawn. No GPS offset is sent to any display.
- Grain-cart even fill. A replay of fixture levels and a suggested side. The cart is not commanded.
- Machine roles. Read-only logger, implement guidance, and full guidance are described
  below. Every command flag this module stores is false. Full guidance remains the
  existing AgOpen host; this module does not arm it.
- Marketing scenarios. They do not sell.
- Inventory refill lines. They do not order.
- Push-to-talk and messages stay on this host. They do not replace the radio.
- The assistant is local rules. No API key is read and no model is called.
- Cameras, drone frames, GPR, weather, bids, and the yield cells ship as labeled fixtures.
  There is no decoder for a Case IH, Trimble, John Deere, or Precision Planting binary.
  ISOXML support is not brand support. Precision Planting’s monitor and modules are
  still unnamed; the intake is the JSON row record in `PrecisionPlantingRows`.

## Machine roles

| Role | May read | May forward logs | May record or draw guidance | May command |
| --- | --- | --- | --- | --- |
| `read-only-logger` | GPS, inbound PGN 250 / 253 / 211 | yes | no | no |
| `implement-guidance` | GPS, inbound PGN 250 / 253 / 211 | yes | yes, as a plan only | no |
| `full-guidance` | GPS, inbound PGN 250 / 253 / 211 | yes | draw only | no, not from here |

PGN 250 is sensor data from the module, 253 is steer data from the module, 211 is the
external IMU. The office logs the values the host has already parsed (`LastSensorData`,
`LastSteerData`, `VehicleState.Roll`) when a browser asks for a snapshot. That read is
not on the 100 Hz loop. Virtual-terminal keypresses are bus writes and are refused if
posted as telemetry.

## Non-destructive geometry import

`FarmGeometryImporter` reads a `TASKDATA.XML` or a KML/KMZ with the existing
`IsoXmlParserHelpers` and `KmlGeometryReader` (the same coordinate rules as the guidance
KML import). It writes a **new** field folder:

- `field.geojson` through `GeoJsonFieldService` (unknown roles, if a later tool added
  any, stay put; this importer only writes the roles it parsed)
- `import-provenance.json`
- `farm-record.json` (crop year and crop, empty until someone sets them)

`FieldService` is not called, so AgOpenGPS files are not deleted on open. A source
directory that already contains `Field.txt` or `Boundary.txt` is refused. Copy the
TASKDATA or KML out of that folder first.

`import-provenance.json`:

| Field | Meaning |
| --- | --- |
| `sourceFileName` | File name only |
| `sourceByteLength` | Length before import |
| `sha256` | SHA-256 of those bytes, lowercase hex |
| `sha256AfterImport` | Same digest, checked again after the write |
| `sourceUnchanged` | True when those digests match |
| `importedAtUtc` | UTC timestamp |
| `importerVersion` | `26.7.2-farm-office` |
| `sourceLabel` | Free text the operator typed |
| `imported` | Which parts were written: `outer-boundary`, `inner-boundary`, `headland`, `guidance-line` |
| `format` | `isoxml-taskdata`, `kml`, or `kmz` |

Headings written into `field.geojson` are radians, 0 = north, clockwise. The field
origin is the centroid chosen at import and is not moved afterwards. Coverage tiles,
if a job is later recorded, hang off that origin.

### LocalPlane round-trip budget

A boundary exported by `IsoXmlExporter` and imported by `FarmGeometryImporter` is
compared on the **original** `LocalPlane` (the ellipsoid series in
`Docs/FIELD_FILE_FORMATS.md`: latitude scale from the origin, longitude scale at each
point’s own latitude). Each vertex must land within **0.05 m**. That budget is
`FarmGeometryImporter.LocalPlaneRoundTripToleranceMeters`. It covers decimal degrees
in the ISOXML file. A swapped axis or a dropped corner does not.

## Grain and inventory

Moisture is a mass fraction, kg/kg. Dry mass at a target is

`wet * (1 - moisture) / (1 - target)`

when the grain is wetter than the target. At or below the target the wet mass is kept;
the formula is not allowed to invent water. A calibration factor then scales the
measured mass (1 = the instrument matches the reference).

The yield map, the cart tickets (plus what is still in the tank), and the bin inventory
are three ledgers. The report does not set one equal to another. Marketable kilograms
are bin dry kilograms minus the remaining contract commitment (committed minus already
delivered). A sell scenario uses only that marketable figure.

Stock available is on hand minus committed, and it may be negative.

## Sidecars

Farm layers for the sample live in `farm.json`, not in `field.geojson` and not in
`job.json`. Roles such as yield, soil, and scout are not stuffed into the guidance
file. A guidance save that uses `GeoJsonFieldService` already keeps roles it does not
own; this layer simply does not depend on that for its own books.
