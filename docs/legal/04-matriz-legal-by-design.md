# Matriz legal-by-design — DietoExpress

> Documento de trabajo. Vincula requisitos de privacidad con funcionalidades y evidencias técnicas. No sustituye revisión jurídica.

## Criterio

Cada nueva funcionalidad que trate datos personales debe identificar antes de activarse: finalidad, responsable/encargado, categorías de datos, acceso, minimización, conservación, proveedores, transferencias, derechos y evidencia.

| Área | Requisito | Implementación/evidencia | Estado |
|---|---|---|---|
| Tenancy | Aislar datos entre responsables | tenant_id + autorización + pruebas IDOR | Implementado / mantener |
| Salud | Restringir acceso a expediente | autorización + auditoría READ_MEDICAL_CHART | Implementado |
| Documentos | Evidenciar aceptación | legal_documents + legal_acceptances + hash/versionado | Implementado |
| Derechos | Facilitar acceso/exportación/corrección/supresión | Registro RGPD + exportación + flujo de supresión controlada | Parcial |
| Conservación | No aplicar TTL global | Matriz por tratamiento | Pendiente |
| Brechas | Registrar y evaluar incidentes | Registro `privacy_incidents` + auditoría + procedimiento | Parcial |
| Subencargados | Inventario y transferencias | Catálogo de proveedores | Pendiente |
| Menores | Representación y consentimiento cuando proceda | Flujo específico | Pendiente |
| Comunicaciones | Separar asistencial y marketing | Preferencias por finalidad/canal | Parcial |
| Nuevas integraciones | Evaluar acceso a datos y ubicación | Checklist previo al alta | Pendiente |
| IA | Evaluación específica antes de activarla | Revisión de tratamiento/proveedor | Futuro; fuera del alcance corto plazo |

## Checklist previo a nueva funcionalidad

- [ ] Finalidad documentada.
- [ ] Base jurídica validada.
- [ ] Responsable y encargado identificados.
- [ ] Datos estrictamente necesarios.
- [ ] Datos de salud identificados.
- [ ] Roles y permisos definidos.
- [ ] Aislamiento por tenant comprobado.
- [ ] Conservación y eliminación definidas.
- [ ] Derechos afectados identificados.
- [ ] Auditoría/evidencia definida.
- [ ] Proveedores y subencargados identificados.
- [ ] Transferencias internacionales revisadas.
- [ ] Riesgo documentado.
- [ ] Revisión jurídica cuando corresponda.

La implementación técnica no implica por sí misma cumplimiento jurídico: los campos de estado anteriores deben revisarse antes de declarar un tratamiento conforme.
