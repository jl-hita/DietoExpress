# DietoExpress

DietoExpress es una plataforma web SaaS de gestión profesional para nutricionistas y clínicas, desarrollada con ASP.NET Core, Angular y PostgreSQL.

El proyecto ha evolucionado desde una aplicación de gestión de dietas hacia una plataforma integral que cubre el ciclo de trabajo del profesional: gestión de pacientes, dietas y recetas, seguimiento nutricional, portal del paciente, comunicación, documentación y cumplimiento, automatizaciones, agenda, suscripciones y facturación, además de un directorio público de profesionales orientado a evolucionar hacia un marketplace de nutrición.

## Funcionalidades

### Gestión profesional y clínica

- Gestión multi-tenant de nutricionistas, clínicas y pacientes.
- Gestión de expedientes, datos antropométricos, seguimiento y evolución.
- Creación y gestión de dietas, recetas, alimentos y listas de la compra.
- Bibliotecas de alimentos y recetas compartidas para clínicas.
- Gestión de asignaciones entre pacientes y profesionales.
- Herramientas para la consulta y seguimiento de pacientes.

### Portal del paciente

- Portal web responsive para pacientes.
- Consulta de dietas, recetas y listas de la compra.
- Seguimiento de evolución y check-ins.
- Documentación y consentimiento.
- Notificaciones y comunicación con el profesional.
- Acceso mediante enlaces seguros sin necesidad de exponer credenciales tradicionales al paciente.

### Agenda y automatizaciones

- Gestión de citas y agenda profesional.
- Soporte para consultas presenciales y online.
- Motor de automatizaciones y tareas programadas.
- Notificaciones y recordatorios asociados a eventos del paciente.
- Base preparada para conectar la agenda con reservas públicas.

### Suscripciones y modelo SaaS

- Planes Free, Professional y Enterprise/Clinic.
- Integración con Stripe para suscripciones.
- Gestión de clientes, suscripciones, renovaciones y cancelaciones.
- Límites de uso asociados al plan y a la capacidad contratada de las clínicas.

### Directorio público y futuro marketplace

- Perfiles públicos voluntarios de nutricionistas.
- Búsqueda por ciudad, especialidad y consulta online.
- Fichas públicas con información profesional.
- Disponibilidad pública reutilizando la agenda profesional.
- Evolución prevista hacia reservas online sin exigir pago para la primera versión.
- Posterior incorporación del pago online de consultas y liquidación al profesional.

### Seguridad, privacidad y cumplimiento

- Aislamiento de datos por tenant.
- Autorización por roles y ámbito profesional.
- Auditoría de operaciones sensibles.
- Protección de endpoints públicos y privados.
- Pruebas automatizadas de regresión de seguridad.
- Gestión de documentación legal, consentimientos y operaciones de privacidad.
- Diseño orientado al cumplimiento de protección de datos, sin sustituir la revisión jurídica profesional.

## Información nutricional

DietoExpress mantiene una base de datos local de alimentos y combina distintas fuentes para ampliar el catálogo:

- **BEDCA (Base de Datos Española de Composición de Alimentos)**: importación inicial del catálogo.
- **Open Food Facts (OFF)**: consulta de alimentos que no están disponibles localmente y almacenamiento posterior.
- **USDA FoodData Central**: fuente adicional de información nutricional.

Este enfoque reduce dependencias de consultas externas y permite mantener un catálogo local optimizado para el uso diario de la aplicación.

## Arquitectura

La plataforma está organizada como una aplicación cliente-servidor:

**Angular SPA**  
↓  
**ASP.NET Core Web API**  
↓  
**Entity Framework Core / Npgsql**  
↓  
**PostgreSQL**

Servicios externos y de infraestructura:

- BEDCA
- Open Food Facts API
- USDA FoodData Central API
- Stripe
- Email / notificaciones web
- Nginx + HTTPS

La aplicación se despliega actualmente sobre Linux mediante servicios systemd y utiliza automatización CI/CD para validación y despliegue.

## Tecnologías

### Backend

- .NET 8 / ASP.NET Core
- Entity Framework Core
- PostgreSQL
- Npgsql
- JWT Authentication
- APIs REST
- Servicios de dominio y automatizaciones

### Frontend

- Angular
- TypeScript
- Angular Material
- RxJS
- SPA con lazy loading

### Infraestructura

- Linux
- PostgreSQL
- Nginx
- HTTPS / Let's Encrypt
- GitHub Actions
- systemd
- Docker para servicios auxiliares del entorno

## Instalación

### Requisitos

- .NET 8 SDK
- Node.js
- PostgreSQL

### Configuración

1. Configurar las variables de entorno necesarias para la API y la base de datos.
2. Configurar PostgreSQL y las credenciales correspondientes.
3. Instalar las dependencias del frontend.
4. Ejecutar el backend y el frontend.
5. En la primera ejecución, el sistema puede inicializar el catálogo de alimentos mediante BEDCA.

Para producción se recomienda utilizar variables de entorno y secretos fuera del repositorio.

## Calidad y seguridad

El proyecto incorpora validaciones automáticas en CI/CD que incluyen:

- Build del frontend Angular.
- Auditoría de dependencias frontend y backend.
- Build del backend .NET.
- Suite automatizada de pruebas de seguridad.
- Validaciones de aislamiento multi-tenant y autorización.

El objetivo es mantener una base técnica preparada para evolucionar desde una herramienta de gestión nutricional hacia una plataforma SaaS profesional.

## Roadmap

### Estado actual

DietoExpress ya cubre el núcleo de una plataforma SaaS profesional para nutricionistas y clínicas:

- Gestión multi-tenant de profesionales, clínicas y pacientes.
- Dietas, recetas, alimentos y bibliotecas compartidas.
- Seguimiento y biometrías.
- Portal del paciente.
- Documentación, consentimiento y privacidad.
- Comunicación y automatizaciones.
- Agenda y citas.
- Suscripciones y modelo SaaS con Stripe.
- Roles normalizados: `superadmin`, `nutritionist` y `clinic_admin`.
- Integraciones de alimentos con BEDCA, Open Food Facts y USDA.
- Directorio público y reservas iniciales.
- Soporte y comunicación con SuperAdmin.
- Integración funcional con Google Calendar.
- Despliegue Linux y documentación de instalación en evolución continua.

### Módulo 16 — Directorio público y reservas: COMPLETADO

Primera versión cerrada funcionalmente:

- Perfiles públicos voluntarios.
- Búsqueda por ciudad, especialidad y consulta online.
- Fichas profesionales.
- Disponibilidad pública reutilizando la agenda.
- Reserva pública sin pago online.
- Revalidación server-side y protección frente a dobles reservas.
- Creación/reutilización del paciente y asociación al profesional.
- Provisión de documentación inicial.
- Acceso seguro al portal del paciente.
- Aislamiento multi-tenant y rate limiting.
- Regresiones de seguridad.

El pago online de consultas queda como evolución posterior.

### Módulo 17 — Soporte y comunicación con SuperAdmin: COMPLETADO

Primera versión cerrada funcionalmente:

- Tickets y conversaciones de soporte.
- Aislamiento por tenant y autorización por rol.
- Estados y prioridades.
- Asignación y seguimiento por SuperAdmin.
- Notas internas no visibles para el cliente.
- Historial y auditoría.
- Notificaciones.
- Reapertura.
- Filtros y búsqueda.
- Protecciones y regresiones de seguridad.

Las mejoras futuras del soporte se consideran evolución y no deuda funcional del módulo base.

### Google Calendar: COMPLETADO EN SU PRIMERA VERSIÓN

- Integración funcional con la agenda.
- Correcciones de configuración y despliegue.
- Endurecimiento.
- Guía de instalación actualizada.

### Fase actual — Estabilización

1. **Hardening y auditoría rápida**
   - Flujos críticos recientes.
   - Autenticación, autorización y roles.
   - Aislamiento multi-tenant.
   - Portal y magic links.
   - Reservas públicas y concurrencia.
   - Soporte y notas internas.
   - Stripe, webhooks y límites de planes.
   - Agenda y Google Calendar.
   - Endpoints públicos y rate limiting.
   - Mantener regresiones automatizadas para cada defecto corregido.

2. **Producción y operación**
   - Guía completa para desplegar desde cero en Linux.
   - PostgreSQL, permisos y comprobaciones de esquema.
   - Directorios, logs y permisos.
   - Secrets y configuración.
   - systemd.
   - Nginx, HTTPS y renovación de Let's Encrypt.
   - CI/CD, rollback y comprobaciones post-despliegue.
   - Automatización progresiva de tareas actualmente manuales.

### Próximas evoluciones de producto

3. **Consulta guiada y herramientas avanzadas de gestión de consulta**
   - Historial clínico estructurado.
   - Notas, patologías, alergias, intolerancias y medicación.
   - Formularios y check-ins.
   - Seguimiento entre consultas.
   - Plantillas y herramientas para reducir trabajo repetitivo.

4. **Estadísticas y gestión profesional**
   - Dashboard.
   - Evolución de pacientes.
   - Métricas de actividad.
   - Estadísticas de citas, pacientes y dietas.
   - Indicadores de negocio.
   - Exportaciones e informes.

5. **Pagos puntuales y pago online de consultas**
   - Pago de citas del directorio.
   - Integración con Stripe separada de las suscripciones SaaS.
   - Estados de pago y reserva.
   - Cancelaciones y reembolsos.
   - Liquidación/comisiones al profesional en una fase posterior.

6. **Documentación legal y cumplimiento avanzado**
   - Versionado de documentos.
   - Trazabilidad de consentimientos.
   - Exportación y borrado.
   - Retención y minimización.
   - Herramientas de soporte RGPD.
   - Revisión jurídica externa cuando corresponda.

7. **Gestión profesional y VERI*FACTU**
   - Ingresos y gastos.
   - Registros de IVA.
   - Informes orientados a Renta.
   - Preparación para VERI*FACTU.
   - Trazabilidad e integridad de registros.
   - Sin sustituir asesoramiento fiscal.

### Evolución profesional

8. **Nuevos módulos profesionales**
   - Funcionalidades inspiradas en herramientas profesionales de nutrición.
   - Prioridad a ahorro de tiempo y reducción de tareas administrativas.

9. **Especializaciones**
   - Plantillas y flujos específicos por especialidad.
   - Parámetros configurables sin acoplar el núcleo a una única especialidad.

9. **Especializaciones**

   La arquitectura de especializaciones queda preparada para perfiles estructurados por paciente y reglas específicas de generación/validación.

   - **Nutrición deportiva avanzada**
     - Perfil de disciplina, objetivo, frecuencia y duración del entrenamiento.
     - Parámetros configurables de proteína, hidratos e hidratación.
     - Integración de los parámetros del perfil con la generación de dietas.
     - Indicaciones contextuales para entrenamiento, rendimiento y recuperación.
     - Evolución prevista hacia periodización de hidratos, timing nutricional y estrategias específicas por deporte.

   - **Pérdida de peso / obesidad**
     - Perfil específico de objetivo, peso objetivo y ritmo de pérdida.
     - Déficit energético configurable y mínimo energético de seguridad definido por el profesional.
     - Objetivo de proteína configurable para favorecer el mantenimiento de masa magra.
     - Revisión periódica y seguimiento de respuesta.
     - Integración de parámetros con la generación de dietas y validaciones.
     - Evolución prevista hacia fases de pérdida, mantenimiento, recomposición y estrategias conductuales.

   - Plantillas y parámetros configurables para otras especialidades.
   - Evitar acoplar el núcleo a una única especialidad.

10. **Multiidioma**
    - Español.
    - Inglés como siguiente prioridad.
    - Traducción de frontend, emails, documentos y contenidos públicos.
    - Formatos localizables.

### Evolución del directorio

11. **Marketplace completo**
    - Clínicas y perfiles públicos.
    - Distancia y búsquedas geográficas.
    - Precio y disponibilidad avanzada.
    - Valoraciones.
    - Verificación profesional.
    - Reserva con pago online.
    - Monetización y visibilidad destacada.
    - Controles contra abuso, fraude y scraping.

### Automatización e IA

12. **Automatización e IA avanzada**
    - Generación asistida de dietas.
    - La especialización avanzada se integra con este bloque como contexto estructurado para futuras funciones de asistencia, sin delegar el criterio profesional en la IA.
    - Sugerencias de alimentos y recetas.
    - Asistencia durante la consulta.
    - Resúmenes y seguimiento.
    - Automatizaciones inteligentes.
    - Control de costes y cuotas de proveedores.
    - Revisión de privacidad, seguridad y marco legal.
    - Supervisión profesional del resultado.

### Líneas futuras

13. **Integraciones de salud y dispositivos**
    - Garmin, Fitbit, Apple Health y Google Fit/Health Connect.
    - Importación autorizada de actividad y otros datos.

14. **Evolución de la experiencia del paciente**
    - Check-ins configurables.
    - Registro de peso y medidas.
    - Adherencia y hábitos.
    - Comunicación contextual.

15. **Escalabilidad y operación**
    - Observabilidad, métricas y alertas.
    - Backups y restauración verificada.
    - Gestión de logs y migraciones.
    - Rate limiting y protección frente a abuso.
    - Optimización de PostgreSQL y cachés.
    - Preparación para crecimiento de tenants y clínicas.

16. **Producto y negocio**
    - Refinar planes Free/Professional/Clinic según uso real.
    - Onboarding y activación.
    - Analítica de conversión.
    - Límites y capacidad de clínicas.
    - Facturación SaaS y operaciones de cuenta.
    - Preparación para comercialización a mayor escala.

### Orden de prioridad

1. Seguridad, hardening y corrección de regresiones.
2. Producción, despliegue y operabilidad.
3. Mejoras que reduzcan el trabajo diario del nutricionista.
4. Monetización relacionada con funcionalidades existentes.
5. Cumplimiento legal y fiscal.
6. Evolución del marketplace.
7. IA e integraciones avanzadas.

Los módulos 16 y 17 quedan cerrados en su primera versión. Las mejoras posteriores se incorporarán como evolución y solo se reabrirá su alcance base si una auditoría encuentra un defecto real.

## Lo que demuestra este proyecto

DietoExpress sirve como proyecto práctico de desarrollo de una plataforma SaaS completa y cubre:

- Desarrollo Full Stack con ASP.NET Core y Angular.
- Diseño y consumo de APIs REST.
- Arquitectura multi-tenant.
- Autenticación, autorización y aislamiento de datos.
- PostgreSQL y Entity Framework Core.
- Integración con APIs y servicios externos.
- Integración de pagos y suscripciones.
- Portales web diferenciados para profesionales y pacientes.
- Automatización de procesos y notificaciones.
- Gestión de documentación y privacidad.
- CI/CD y despliegue en infraestructura Linux.
- Diseño responsive y arquitectura frontend modular.
- Pruebas automatizadas de seguridad y regresión.

## Capturas

*(Pendiente de añadir.)*
