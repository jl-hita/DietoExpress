# Catálogo legal de documentación del paciente — España

> Catálogo funcional para DietoExpress. Las plantillas definitivas deben adaptarse al profesional/centro, actividad y comunidad autónoma y revisarse jurídicamente antes de presentarlas como documentos definitivos.

## Principio

No todos los documentos son “consentimientos” ni todos requieren firma.

Cada plantilla tendrá:
- ámbito/jurisdicción;
- finalidad;
- tipo;
- cuándo se genera;
- si es obligatoria;
- si requiere aceptación;
- quién debe aceptarla;
- versión;
- fecha de entrada en vigor;
- hash;
- auditoría;
- política de sustitución de versión.

## 1. Alta del paciente

### 1.1 Información de protección de datos
Tipo: informativa. Momento: alta. Firma: no necesariamente.
Objetivo: informar sobre responsable, finalidades, bases jurídicas, derechos, destinatarios/encargados, conservación y contacto.

### 1.2 Condiciones de prestación del servicio
Tipo: contractual. Momento: alta/contratación. Aceptación: sí, cuando sea parte de la contratación.
Contenido: servicio, alcance, precio, citas, cancelaciones, pagos y condiciones aplicables.

### 1.3 Información/aceptación de la intervención nutricional
Tipo: información/aceptación según el servicio y marco aplicable. Momento: alta. Aceptación: configurable.
Nota: no convertir automáticamente cualquier documento en consentimiento informado escrito obligatorio.

## 2. Comunicaciones

### 2.1 Comunicaciones asistenciales
Tipo: configuración/preferencia. Momento: alta.
Finalidad: facilitar comunicaciones necesarias para la atención.

### 2.2 Comunicaciones comerciales
Tipo: consentimiento/preferencia independiente. Momento: opcional.
Regla: nunca mezclarlo con la aceptación necesaria para prestar el servicio.

## 3. Consulta online

### 3.1 Información y condiciones de teleconsulta
Tipo: informativa/contractual. Momento: cuando se habilite modalidad online.
Debe cubrir la naturaleza de la consulta, limitaciones, canal utilizado, incidencias de conexión y forma alternativa de contacto cuando proceda.

## 4. Fotografías y medidas

### 4.1 Uso asistencial de fotografías
Tipo: autorización específica cuando resulte necesaria. Momento: antes de obtener/utilizar las imágenes para la finalidad correspondiente.

### 4.2 Uso publicitario de fotografías/testimonios
Tipo: consentimiento específico. Momento: opcional.
Regla: separado del tratamiento asistencial.

## 5. Investigación y estadísticas

### 5.1 Uso de datos con fines de investigación
Tipo: tratamiento específico. Momento: solo cuando exista el proyecto/base jurídica correspondiente.

### 5.2 Uso estadístico/analítico
Debe diseñarse preferentemente con minimización y, cuando sea posible, anonimización o pseudonimización. No habilitarlo automáticamente por aceptar la privacidad.

## 6. Menores

### 6.1 Representación legal
Tipo: documento de representación/autorización. Momento: cuando el paciente sea menor y resulte necesario.

### 6.2 Información y permisos del representante
Debe quedar separada la identidad del menor de la del representante y documentarse quién puede acceder a qué información.

## 7. Derechos y gestión del expediente

Plantillas/flows:
- solicitud de acceso;
- rectificación;
- supresión;
- oposición;
- limitación;
- portabilidad cuando proceda;
- revocación de consentimientos;
- solicitud de copia/exportación;
- baja del paciente.

Estos documentos no deben sustituir al mecanismo funcional para ejercer derechos.

## 8. Baja

### 8.1 Confirmación de baja del servicio
Debe distinguirse entre baja comercial y obligaciones de conservación de documentación.

### 8.2 Exportación del expediente
El profesional/centro debe poder obtener la documentación necesaria antes del cierre, conforme a las obligaciones aplicables.

## 9. Documentos internos futuros

Fuera del flujo habitual del paciente:
- contrato profesional-clínica;
- incorporación de nutricionista;
- confidencialidad;
- políticas internas;
- documentación de responsables/administradores.

## 10. Metadatos técnicos

La infraestructura existente debe soportar:
template → version → tenant → client → type → requiredOnClientCreation → requiredBeforeConsultation → requiresSignature → status → hash → createdAt → acceptedAt → audit.

## 11. Reglas de flujo

### Alta
1. Crear paciente.
2. Provisionar plantillas marcadas para alta.
3. Notificar al paciente.
4. Mostrar documentación pendiente.
5. Registrar visualización/aceptación.
6. Avisar al profesional/clínica cuando corresponda.
7. Mantener historial de versiones.

### Antes de consulta
1. Provisionar documentos marcados requiredBeforeConsultation.
2. Bloquear inicio solo cuando exista un documento que realmente requiera aceptación previa.
3. Recordatorios configurados.
4. Al completar documentación, cancelar recordatorios pendientes.
5. Avisar al profesional/clínica.

### Nueva versión
- No sobrescribir la versión histórica.
- Crear nueva versión.
- Invalidar/revocar la anterior cuando corresponda.
- Exigir nueva aceptación únicamente cuando la naturaleza del cambio lo justifique.
- Conservar evidencia de la aceptación anterior.

## 12. Catálogo inicial de activación

| Documento | Alta | Preconsulta | Aceptación | Configurable |
|---|---:|---:|---:|---:|
| Información privacidad | Sí | No | No necesariamente | Sí |
| Condiciones servicio | Sí | No | Sí | Sí |
| Información/aceptación nutricional | Sí | No | Según configuración | Sí |
| Comunicaciones asistenciales | Sí | No | Según configuración | Sí |
| Marketing | No | No | Sí | Sí |
| Teleconsulta | Según modalidad | No | Según configuración | Sí |
| Fotografías asistenciales | No | No | Según finalidad | Sí |
| Fotografías/testimonios publicitarios | No | No | Sí | Sí |
| Investigación | No | No | Según proyecto | Sí |
| Representación de menor | Según caso | No | Sí | Sí |
| Derechos/solicitudes | Bajo demanda | No | No | Sí |
| Baja/exportación | Bajo demanda | No | No | Sí |

## 13. Estado

Este catálogo define el producto. Las plantillas jurídicas concretas son el siguiente entregable y deben revisarse antes de publicación.
