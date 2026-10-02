# Auditoría de seguridad y robustez

He realizado una auditoría progresiva de DietoExpress, centrada principalmente en seguridad, aislamiento multi-tenant, autorización, validación de entradas y operaciones sensibles.

## Cambios realizados

- Reforcé el aislamiento multi-tenant en clientes, dietas, recetas, alimentos, asignaciones y biometrías.
- Revisé y protegí endpoints frente a accesos IDOR mediante IDs de otros usuarios o tenants.
- Reforcé el acceso a alimentos locales para que solo sean utilizables por su creador dentro de su tenant, salvo administradores autorizados.
- Apliqué ese mismo control a generación automática de dietas, validación, PDFs, recetas y portal del paciente.
- Reforcé el acceso a biometrías comprobando siempre el cliente padre, tenant, permisos y estado no archivado.
- Bloqueé operaciones de biometrías sobre pacientes archivados y añadí límites para importaciones y valores numéricos.
- Limité recursos del parser de bioimpedancia para evitar entradas excesivamente grandes.
- Endurecí el portal del paciente: los tokens de acceso se almacenan hasheados y ya no se devuelve el hash almacenado.
- El consumo de magic links ahora compara contra el hash del token recibido.
- Reforcé la revocación de sesiones del portal mediante versión de token.
- Validé el passcode del paciente para que tenga exactamente 6 dígitos.
- Reforcé los permisos de administradores, nutricionistas y administradores de clínica, incluyendo las operaciones de activación, suspensión, reasignación y borrado.
- Reforcé la normalización y unicidad de emails y usernames, incluyendo comparación case-insensitive.
- El login permite usernames sin distinguir mayúsculas/minúsculas.
- Añadí restricciones y validaciones de tamaño en distintos DTOs y endpoints para reducir abusos de recursos.
- Añadí límites a consultas/listados potencialmente grandes.
- Reforcé la validación de URLs y configuración administrativa.
- Revisé CORS, JWT, sesiones, recuperación de contraseña, setup inicial y endpoints administrativos.
- Añadí protecciones y pruebas sobre operaciones concurrentes relacionadas con límites de licencia, creación de clientes/dietas y asignaciones.
- Reforcé la facturación con Stripe: idempotencia de Checkout, serialización por tenant, historial de suscripciones y control de eventos duplicados o antiguos.
- Reforcé la unicidad e identidad de alimentos externos y las operaciones concurrentes de OpenFoodFacts.
- Añadí protección a enlaces externos abiertos en nuevas pestañas (noopener noreferrer).
- Revisé el uso de credenciales y tokens en el frontend y eliminé el almacenamiento del token del portal del paciente en localStorage.
- Añadí una batería creciente de pruebas de regresión de seguridad para evitar que estas protecciones se pierdan en cambios futuros.

## Estado

La auditoría sigue en curso. Hasta ahora el foco principal ha sido el aislamiento entre tenants y los controles de autorización. Cada vulnerabilidad o debilidad relevante encontrada se ha corregido y, cuando ha sido posible, se ha añadido una prueba de regresión.

También estoy revisando los despliegues y corrigiendo automáticamente los fallos de compilación o tests que aparecen durante la auditoría.

## Revisión adicional — altas concurrentes de usuarios

- Se endureció la comprobación final de unicidad de nombres de usuario en el alta administrativa para que la revalidación dentro del bloqueo transaccional también sea *case-insensitive*.
- Esto mantiene coherente el comportamiento con el login y con las comprobaciones previas de unicidad, evitando que una variante de mayúsculas/minúsculas llegue a la fase de persistencia durante una carrera concurrente.


## Revisión adicional — límites de creación y edición de dietas

- Se limitaron los días, comidas, elementos por comida y número total de alimentos/intercambios que puede aceptar una dieta.
- También se limitaron las longitudes de nombres y notas y los valores de gramos/intercambios.
- Las mismas validaciones se aplican tanto al alta como a la edición para evitar que un payload grande pueda amplificarse en múltiples registros.
- Se añadió una prueba de regresión específica para estos límites.
