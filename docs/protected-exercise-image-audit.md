# Auditoría protegida de imágenes del catálogo de ejercicios

## Endpoint

`GET /api/exercise-catalog/images/audit`

**Acceso restringido:** requiere autenticación y el rol `superadmin`. No es un feed público y no debe marcarse como `AllowAnonymous`.

Devuelve únicamente los ejercicios que no tienen imagen almacenada ni imagen RepDB derivable. Incluye todos los estados de revisión y propietarios porque la auditoría controlada necesita detectar también registros pendientes/rechazados y aportaciones profesionales.

La respuesta contiene metadatos completos del catálogo, incluidos campos de revisión, contribuyente y `source_payload`. Por ese motivo debe tratarse como información interna y no publicarse, compartirse ni redistribuirse.

El endpoint es de solo lectura, no devuelve bytes de imagen ni crea una exportación masiva de imágenes RepDB. Usa `Cache-Control: no-store`. Las ilustraciones generadas deben revisarse e incorporarse al seed curado de Trazo, no escribirse directamente en ninguna base de producción.
