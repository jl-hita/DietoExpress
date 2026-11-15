# Public exercise image audit feed

The read-only endpoint provides the approved global exercise catalog and identifies records that do not yet have an available illustration.

## Endpoint

`GET /api/exercise-catalog/images/audit`

No authentication or API key is required.

The endpoint returns the complete approved **global** exercise catalog (including bilingual names, descriptions, instructions, muscle/equipment metadata, review provenance and source image metadata), plus a `missing` array with approved global exercises that have no available image.

It intentionally excludes pending/rejected records and professional-owned contributions. It does not return image bytes or create a bulk RepDB image export. RepDB image licensing permits in-app use, not redistribution as a dataset.

The endpoint is read-only. Generated illustrations should be reviewed and added to Trazo's curated image seed, not written directly into either production database. Responses use `Cache-Control: no-store`.

> Temporary operational choice: this feed is public to simplify the image-generation workflow. Reassess access controls before using it as a general-purpose public API.
