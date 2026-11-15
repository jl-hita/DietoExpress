# Protected exercise image audit feed

The superadmin-only browser export is useful for manual curation, but image-generation workflows use a separate read-only endpoint protected by a server-side API key.

## Endpoint

`GET /api/exercise-catalog/images/audit`

Header: `X-Exercise-Audit-Key: <secret>`

The endpoint returns the complete approved **global** exercise catalog (including bilingual names, descriptions, instructions, muscle/equipment metadata, review provenance and source image metadata), plus a `missing` array with approved global exercises that have no available image.

It intentionally excludes pending/rejected records and professional-owned contributions. It does not return image bytes or create a bulk RepDB image export. RepDB image licensing permits in-app use, not redistribution as a dataset.

## Configure

Generate a random secret of at least 32 characters on the server:

```bash
openssl rand -hex 32
```

Store it outside the repository in the API environment, for example:

```ini
ExerciseImages__AuditApiKey=<generated-secret>
```

Restart the API after changing the environment. Send the same value in the request header from the trusted audit client. Do not put the key in source control, URLs, logs, browser code, or issue comments. Rotate it after the audit is complete.

If the key is absent or shorter than 32 characters, the endpoint returns 503. Invalid keys return 401. Responses use `Cache-Control: no-store`.

## Example

```bash
curl --fail --silent --show-error \
  -H "X-Exercise-Audit-Key: $EXERCISE_IMAGES_AUDIT_KEY" \
  https://<dietoexpress-host>/api/exercise-catalog/images/audit \
  -o exercise-image-audit.json
```

The endpoint is intentionally read-only. Generated illustrations should be reviewed and added to Trazo's curated image seed, not written directly into either production database.
