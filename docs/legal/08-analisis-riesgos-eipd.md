# Análisis inicial de riesgos y EIPD — DietoExpress

> Documento de trabajo para decisión de cumplimiento. No constituye una EIPD formal ni una conclusión jurídica definitiva.

## Alcance

Se consideran especialmente:

- datos de salud y documentación nutricional;
- multi-tenancy;
- portal de pacientes;
- comunicaciones;
- documentos y aceptaciones;
- automatizaciones asistenciales;
- integraciones externas;
- menores;
- proveedores y transferencias;
- futuras funcionalidades de IA.

## Riesgos principales

| Riesgo | Impacto potencial | Controles existentes | Trabajo pendiente |
|---|---|---|---|
| Acceso entre tenants | Alto | Autorización + pruebas IDOR | Mantener regresiones |
| Acceso indebido al expediente | Alto | Roles + auditoría clínica | Revisiones periódicas |
| Exposición de documentos | Alto | Control de acceso + eventos | Revisión de almacenamiento |
| Cuenta comprometida | Alto | Autenticación + rate limiting | MFA/controles adicionales según evolución |
| Borrado incorrecto | Alto | Archivado lógico | Flujo formal de retención |
| Brecha de proveedor | Variable | Inventario pendiente | Catálogo y evaluación de subencargados |
| Comunicación indebida | Alto | Separación funcional parcial | Preferencias por finalidad/canal |
| Menor sin representación adecuada | Alto | Pendiente | Flujo específico |
| Automatización incorrecta | Variable/alto | Automatizaciones controladas | Revisión por caso de uso |
| IA | Variable/alto | No utilizada a corto plazo | Evaluación previa antes de activación |

## Decisión provisional

No se declara aquí que una EIPD sea necesaria o innecesaria de forma definitiva. Antes de cerrar el módulo debe completarse el análisis con:

- volumen real de pacientes;
- categorías y escala de datos;
- tratamientos de cada cliente;
- proveedores reales;
- transferencias;
- automatizaciones activas;
- uso futuro de IA;
- medidas técnicas y organizativas definitivas.

La decisión final debe quedar documentada y revisarse ante cambios significativos.
