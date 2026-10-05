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

Los bloques principales ya implementados incluyen gestión multi-tenant de pacientes, dietas y recetas, portal del paciente, comunicación profesional-paciente, documentación y privacidad, automatizaciones, agenda, suscripciones/Stripe, directorio público y **Módulo 17 — Soporte y comunicación con SuperAdmin**.

El Módulo 17 queda completado en su primera versión: tickets/conversaciones, aislamiento por tenant, notas internas, asignación a SuperAdmin, estados y prioridades, historial/auditoría, notificaciones, reapertura, filtros y búsqueda.

### Próximos bloques

- **Hardening y auditoría rápida de flujos**: revisión de regresiones funcionales y de seguridad tras los últimos bloques de desarrollo.
- **Despliegue Linux desde cero**: completar y mantener la guía exhaustiva de instalación, configuración, secretos, directorios/logs, PostgreSQL, systemd, Nginx y HTTPS.
- **Google Calendar**: integración de agenda y documentación operativa, con endurecimiento de la configuración OAuth.
- **Consulta guiada y herramientas avanzadas de gestión de consulta.**
- **Estadísticas y gestión profesional.**
- **Agenda y reservas públicas.**
- **Pagos puntuales y pago online de consultas.**
- **Documentación legal y cumplimiento avanzado.**
- **Gestión profesional y VERI*FACTU**: ingresos, gastos, registros de IVA e informes orientados a Renta, sin sustituir asesoramiento fiscal.
- **Nuevos módulos profesionales** inspirados en herramientas de gestión nutricional existentes.
- **Especializaciones y soporte multiidioma.**
- **Evolución del directorio hacia marketplace.**
- **Funcionalidades avanzadas de automatización e IA**, sujetas a revisión de privacidad, seguridad y marco legal.

### Mejoras pendientes de soporte

Como evolución posterior del Módulo 17 quedan, si aportan valor real:

- contador de no leídos por ticket basado en última lectura;
- filtros avanzados de tenant en la interfaz de SuperAdmin;
- mejoras de UX del centro de notificaciones;
- adjuntos en tickets, cuando exista un modelo seguro de almacenamiento y antivirus/validación;
- cualquier realtime/polling solo si la necesidad real lo justifica; no forma parte de la primera versión.

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
