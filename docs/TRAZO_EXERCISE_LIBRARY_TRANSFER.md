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
