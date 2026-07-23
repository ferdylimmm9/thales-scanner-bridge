# Thales Scanner Bridge — WebSocket Data Contract (v1)

**Audience:** any developer integrating a web app with the Thales document-scanner
bridge. You do not need this repository's code, the Thales SDK, or C# knowledge —
everything a client needs is on this page.

**Status:** v1, stable. Verified against a QS2000 reader with real passports (2026-07-14).

```
[Thales reader] --USB--> [ThalesBridge.exe on the kiosk PC] --ws://localhost:8765--> [your web app]
```

## 1. Transport

| Property | Value |
| --- | --- |
| Protocol | WebSocket (RFC 6455), text frames only |
| URL | `ws://localhost:<port>/` — default port **8765** (bridge CLI arg 1 overrides) |
| Reachability | **localhost only** by design. Your app must run in a browser on the same PC as the scanner. |
| Direction | **Server → client only.** The bridge accepts no commands in v1; anything the client sends is ignored. |
| Concurrency | Multiple clients may connect at once; every frame is broadcast to all of them. |
| On connect | The bridge immediately sends the current `status` frame, so a new client knows the reader phase without waiting. |

The reader auto-triggers: when a document lands on the glass the bridge starts
reading and pushes frames. There is no "start scan" call.

## 2. Framing

Every WebSocket text frame is **exactly one JSON object** with a `type`
discriminator: `"status"`, `"result"`, or `"error"`. Unknown `type` values must be
ignored by clients (forward compatibility). JSON field names are camelCase.

### 2.1 `status` — reader phase changed

```json
{ "type": "status", "status": "waiting_for_document" }
```

| `status` | Meaning | Suggested UI |
| --- | --- | --- |
| `idle` | Reader not ready (starting up, or errored) | hide scanner UI |
| `waiting_for_document` | Ready — watching the glass | "Place document on the reader" |
| `reading` | Document detected, capture in progress | spinner; takes 1–5 s |

### 2.2 `result` — one completed scan

```json
{
  "type": "result",
  "data": {
    "mrz": {
      "firstName": "ANNA",
      "middleName": null,
      "lastName": "KURNIATY",
      "documentNumber": "X9423995",
      "documentType": "passport",
      "dateOfBirth": "2002-03-08",
      "gender": "F",
      "nationality": "IDN",
      "issuingCountry": "IDN",
      "expiryDate": "2031-07-01"
    },
    "images": {
      "front": "data:image/jpeg;base64,...",
      "back": null,
      "portrait": "data:image/jpeg;base64,..."
    },
    "chip": { "present": true, "verified": true },
    "capturedAt": "2026-07-14T07:37:50.4916396Z"
  }
}
```

#### `mrz` fields

| Field | Type | Semantics |
| --- | --- | --- |
| `firstName` | string | MRZ forenames, uppercase. `""` if unreadable. |
| `middleName` | string \| null | Rarely populated — MRZ does not separate middle names. |
| `lastName` | string | MRZ surname, uppercase. |
| `documentNumber` | string | As printed in the MRZ. |
| `documentType` | `"passport"` \| `"national_id"` \| `"drivers_license"` \| `"other"` | Normalized class. Treat `"other"` as "ask the operator". |
| `dateOfBirth` | string | `yyyy-MM-dd`, or `""` if unreadable. 2-digit MRZ years are windowed (past-biased). |
| `gender` | `"M"` \| `"F"` \| `""` | `""` = unspecified/unreadable. |
| `nationality` | string | **ISO 3166-1 alpha-3** (e.g. `PHL`, `IDN`). May be `""`. |
| `issuingCountry` | string | ISO alpha-3; falls back to `nationality` when the MRZ issuing state is absent. |
| `expiryDate` | string | `yyyy-MM-dd` or `""`. 2-digit years windowed future-biased (a doc expiring "31" = 2031). |

**Guarantee:** a `result` is only emitted when at least one of `lastName` /
`documentNumber` is non-empty. Anything less is reported as a `READ_INCOMPLETE` error
instead. Every other field may legitimately be empty — never assume presence.

#### `images` fields

All images are **data URLs** (`data:image/jpeg;base64,...`), nullable.

| Field | Content | Typical size |
| --- | --- | --- |
| `front` | Full visible-light scan of the page on the glass | 300–500 KB |
| `back` | Rear image — only from double-sided feeders, usually `null` on QS2000 | — |
| `portrait` | Cropped face photo (from the image, or the chip when available) | ~100 KB |

Result frames are large (≈0.5–1 MB). Don't log them wholesale, and don't keep more
than the latest one in memory.

#### `chip` field

`chip` is `null` when no RFID chip was read. When present:
`{ "present": boolean, "verified": boolean }`.

> ⚠️ **v1 caveat:** `verified` is currently hard-coded `true` when chip data arrives —
> real passive-authentication parsing is a known TODO. Treat it as **advisory only**;
> do not gate a business decision on it yet.

#### `capturedAt`

ISO 8601 UTC timestamp assigned by the bridge when the read completed.

### 2.3 `error` — read or reader failure

```json
{ "type": "error", "code": "READ_INCOMPLETE", "message": "Document read produced no usable MRZ data." }
```

| `code` | Origin | Client action |
| --- | --- | --- |
| `READ_INCOMPLETE` | bridge | Ask the operator to re-seat the document and rescan. |
| `INIT_FAILED` | bridge | Reader didn't start (no result frames will ever come). Surface "scanner unavailable". |
| `DATA_HANDLER_FAILED` | bridge | A bridge-internal bug processing one data item; the scan may still complete. Log it. |
| any other (e.g. `ERROR_CAMERA_DRIVER_ERROR`, `ERROR_FEATURE_NOT_SUPPORTED`) | Thales SDK pass-through | Show `message` to the operator/support. |

Errors are **not fatal to the session**: the bridge keeps running and usually returns
to `waiting_for_document`. Clients should surface the message and keep listening.

## 3. Client implementation requirements

1. **Validate every frame** against this contract (JSON Schema below, or the zod
   schema). Drop and log frames that don't parse — never crash the UI on one.
2. **Reconnect with capped backoff** (e.g. 1 s doubling to 15 s). "Bridge not running"
   is a *normal* state on non-kiosk machines — show a quiet hint, never a blocking error.
3. **Never make the scanner a gate.** It is an accelerator; manual entry / upload
   flows must remain fully usable when `disconnected`.
4. **Scans pre-fill, humans verify.** Populate form fields from `result`, but keep
   them editable and keep any mandatory confirmation step.
5. **PII discipline:** result frames contain passport data and face images. Don't
   write them to logs/analytics; clear them from state when the flow ends.
6. Consume a result **once** (e.g. clear your "last scan" slot after applying it) —
   the reader stays armed, and a patron re-seating the document produces a second
   result frame.
7. **Re-scans are authoritative.** When applying a new result over an earlier one,
   renew every scan-owned field — including clearing fields the new scan lacks.
   Merging two scans field-by-field risks mixing two patrons' documents.
8. **Keep the full result** somewhere (state/store), not just the fields your form
   uses — `chip`, `capturedAt`, and unmapped MRZ fields must not be silently lost.

### Minimal vanilla-JS client

```js
const ws = new WebSocket('ws://localhost:8765')
ws.onmessage = (e) => {
  const msg = JSON.parse(e.data)
  switch (msg.type) {
    case 'status': updatePhase(msg.status); break
    case 'result': fillForm(msg.data); break
    case 'error':  showScannerError(msg.message); break
  }
}
ws.onclose = () => scheduleReconnect()
```

### Reference implementations in this repo

| Piece | Path | Notes |
| --- | --- | --- |
| Zod schema + TS types | `src/types/documentScan.ts` | Copy into your app, or extract to a shared package. |
| React hook (connect/reconnect/validate) | `src/hooks/useDocumentScanner.ts` | Framework-specific reference. |
| C# server-side contract | `bridge/ThalesBridge/Contracts.cs` | The producing side — source of truth. |
| Store mapper example | `src/utils/documentScan.ts` | ISO-3 country resolution, data-URL stripping, authoritative re-scan policy (each scan renews all scan-owned fields; missing values clear rather than inherit). |

## 4. Versioning & evolution policy

- v1 frames carry **no version field**. Compatibility rule: **additive changes only**
  (new optional fields, new `error.code` values, new `type` values — which clients
  must ignore when unknown).
- Any breaking change (renamed/removed field, semantics change) requires adding a
  `"v": 2` field to every frame **and** bumping the bridge minor version; v2 clients
  can then detect v1 bridges by the absence of `v`.
- Contract changes update, in the same commit: `Contracts.cs`,
  `src/types/documentScan.ts`, the JSON Schema, and this document.

## 5. Machine-readable schema

A JSON Schema (draft 2020-12) for the frame envelope lives at
[`bridge/contract/scanner-message.schema.json`](contract/scanner-message.schema.json).
Use it to generate types in any language (e.g. `json-schema-to-typescript`,
`quicktype` for Kotlin/Swift/Java/C#) or to validate frames in tests.

For context on what exists **upstream** of this contract (raw SDK fields, some of
which are not forwarded today), see the annotated dummy-data example at
[`bridge/contract/example-raw-sdk-data.json`](contract/example-raw-sdk-data.json).
