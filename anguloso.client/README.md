# DietoExpress

DietoExpress es una aplicación web Full Stack para la gestión nutricional y el seguimiento de la alimentación, desarrollada como proyecto personal con ASP.NET Core y Angular.

El proyecto integra múltiples bases de datos públicas de alimentos para ofrecer información nutricional completa y mantener una base de datos local optimizada.

## Características

- Autenticación mediante JWT.
- Gestión de alimentos y dietas.
- Registro y seguimiento nutricional.
- API REST desarrollada con ASP.NET Core.
- Interfaz SPA desarrollada con Angular.
- Base de datos local optimizada mediante caché de alimentos.

## Obtención de datos nutricionales

La aplicación combina varias fuentes de información:

- **BEDCA (Base de Datos Española de Composición de Alimentos)**: durante el primer inicio se importa automáticamente la base de datos para disponer de un catálogo inicial de alimentos.
- **Open Food Facts (OFF)**: cuando un alimento no existe en la base local, se consulta automáticamente mediante su API y queda almacenado para futuras búsquedas.
- **USDA FoodData Central**: utilizada como fuente adicional cuando la información no está disponible localmente o en Open Food Facts.

Gracias a este enfoque, la aplicación reduce el número de consultas externas y mejora el rendimiento mediante una base de datos propia que actúa como caché.

## Tecnologías

### Backend

- ASP.NET Core
- Entity Framework Core
- SQL Server
- JWT Authentication
- AutoMapper

### Frontend

- Angular
- TypeScript
- Angular Material
- RxJS

## Arquitectura

Angular SPA
⬇
ASP.NET Core Web API
⬇
Entity Framework Core
⬇
SQL Server

Fuentes externas:

- BEDCA
- Open Food Facts API
- USDA FoodData Central API

## Instalación

### Requisitos

- .NET 8 SDK
- Node.js
- SQL Server

### Configuración

1. Crear un `appsettings.json` a partir de `appsettings.Example.json`.
2. Configurar la cadena de conexión y las claves necesarias.
3. Ejecutar la aplicación.
4. En el primer inicio se importarán automáticamente los alimentos de BEDCA.

## Lo que demuestra este proyecto

Este proyecto ha servido para profundizar en:

- Desarrollo Full Stack con ASP.NET Core y Angular.
- Diseño de APIs REST.
- Entity Framework Core.
- Autenticación mediante JWT.
- Integración con APIs externas.
- Procesos de importación y sincronización de datos.
- Arquitectura cliente-servidor.
- Persistencia y caché de información nutricional.

## Capturas

*(Pendiente de añadir.)*
