# Private exercise-library transfer to TRAZO

The superadmin dashboard exposes **Enviar biblioteca a TRAZO**. This is a private server-to-server operation; the archive is created in a temporary file and is never downloaded by the browser or committed to the repository.

## Configuration

Set these values in the DietoExpress server environment (never in source control):

- `TrazoTransfer__Endpoint`: HTTPS base URL of the TRAZO API, for example `https://trazo.example/`.
- `TrazoTransfer__ApiKey`: a long random secret shared only with TRAZO.

Configure the same secret in TRAZO as `DietoExpressImport__ApiKey`. The DietoExpress service account must be able to reach the TRAZO endpoint over HTTPS.

## Scope and integrity

The export contains only global exercises with `owner_user_id IS NULL`, `review_status = approved` and `reviewed = true`. Pending/rejected exercises and professional-owned contributions are excluded. The effective reviewed metadata, source record and available image slots are transferred. RepDB image slots are fetched through the existing single-image read-through method before inclusion.

The ZIP contains `manifest.json`, `exercises.json` and image files. SHA-256 checksums and image sizes are validated by TRAZO before any database transaction starts. Transfer failure is reported in the dashboard; no local archive is retained.

RepDB attribution is preserved in the import metadata and exposed by TRAZO. Keep this workflow private and do not publish, redistribute or offer the imported RepDB data/assets as a dataset, archive or bulk-download service. Confirm that the exact RepDB licence accepted for DietoExpress permits this private cross-application transfer.

## Receiving exercises created in TRAZO (reverse direction)

DietoExpress also accepts the exercises that were created in TRAZO. TRAZO builds the archive and posts it to
`POST /api/internal/trazo-exercise-library/import` with the header `X-Trazo-Transfer-Key`. The key is the same
secret as the transfer to TRAZO: `TrazoTransfer__ApiKey` (no new setting is needed on this side). Without it the
endpoint answers `503`; with a wrong key, `401`. The check runs before the archive is read.

- The archive (`manifest.json`, `exercises.json`, `images/...`, schemaVersion 1, source `TRAZO`) is validated completely
  before any database work: SHA-256 hashes, identifiers, image signatures and sizes, and the same rules as the curated
  additions (valid pattern, muscles and goals; at least three instructions).
- New exercises are inserted as approved, reviewed global exercises with source `dietoexpress-curated` and the reviewer
  label `TRAZO transfer v{n}`. A later transfer refreshes an exercise only if it carries that label with a lower version,
  so RepDB exercises, DietoExpress additions and anything edited by a person are never overwritten.
- Images are stored as `custom` images, and only in empty slots.
- The response reports `exercisesInserted`, `exercisesUpdated`, `exercisesSkipped`, `imagesStored` and `imagesSkipped`.
