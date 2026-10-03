# Política técnica de conservación y eliminación — DietoExpress

> Documento de diseño. Los plazos definitivos deben validarse según el tratamiento, el responsable (profesional/clínica), la legislación aplicable y las obligaciones legales concretas.

## Principio

No existe un TTL único para DietoExpress. La eliminación debe depender de la finalidad, la relación contractual, la naturaleza del dato, las obligaciones de conservación y las instrucciones del responsable.

## Matriz inicial

| Tratamiento | Evento inicial | Acción al finalizar | Excepciones |
|---|---|---|---|
| Cuenta SaaS | Cierre de cuenta | Supresión según condiciones contractuales | Facturación/obligaciones legales |
| Paciente/expediente | Instrucción del responsable o cierre de clínica | Exportación y supresión según instrucción y normativa aplicable | Conservación clínica/legal |
| Documentos de paciente | Fin de relación / instrucción | Exportación y supresión | Obligación legal o bloqueo |
| Aceptaciones legales | Revocación/cierre | Conservar cuando sea necesario acreditar la relación | Obligación legal |
| Facturación | Fin del ejercicio/relación | Conservación conforme a normativa fiscal/mercantil | Obligación legal |
| Logs de seguridad | Generación | Rotación/supresión según política técnica | Incidente o investigación |
| Backups | Caducidad del backup | Expiración automática | Restauración/incidente documentado |

## Reglas técnicas

1. Archivar un paciente no equivale automáticamente a borrar su expediente.
2. Una solicitud de supresión debe comprobar primero si existe obligación de conservación o necesidad de bloqueo.
3. Los datos de salud no deben reutilizarse para finalidades incompatibles.
4. Las copias de seguridad deben tener un ciclo de vida documentado independiente del dato operativo.
5. Toda eliminación administrativa relevante debe generar evidencia sin copiar innecesariamente el contenido clínico.
6. La eliminación debe respetar dependencias entre expediente, documentos, dietas, citas, mensajes y auditoría.

## Estado de implementación

- Archivado lógico de pacientes: existente.
- Auditoría de acceso clínico: existente.
- Política técnica de retención por categoría: pendiente de implementación.
- Flujo formal de solicitud de supresión: pendiente.
- Exportación integral del expediente: pendiente de completar.
- Purga de backups documentada: pendiente de documentar según infraestructura real.

## Regla para futuras funcionalidades

Ningún nuevo dato personal debe introducirse sin indicar explícitamente su evento de inicio de conservación y su mecanismo de eliminación/bloqueo.
