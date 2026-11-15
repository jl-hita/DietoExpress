# Public exercise image audit feed

The read-only endpoint returns **only exercises that have no available illustration**, irrespective of review status, ownership or source.

## Endpoint

`GET /api/exercise-catalog/images/audit`

No authentication or API key is required.

The `items` array contains the complete catalog metadata for every exercise with neither a stored image nor a derivable RepDB image. Pending, approved and rejected records are included, as are professional-owned contributions. Exercises that already have an image are excluded.

The endpoint does not return image bytes or create a bulk RepDB image export. RepDB image licensing permits in-app use, not redistribution as a dataset.

The endpoint is read-only. Generated illustrations should be reviewed and added to Trazo's curated image seed, not written directly into either production database. Responses use `Cache-Control: no-store`.

> Temporary operational choice: this feed is public to simplify the image-generation workflow. Reassess access controls before using it as a general-purpose public API.
