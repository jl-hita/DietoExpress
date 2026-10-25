# DietoExpress Android

Primera base de la app Android nativa con Kotlin y Jetpack Compose. La API ASP.NET Core existente seguirá siendo la autoridad para autenticación, permisos y reglas de negocio.

## Estado

Esta rama añade una pantalla de arranque y la estructura Gradle inicial. **No es todavía una app funcional para pacientes ni una versión publicable.** El login, la gestión de cookies, las pantallas del portal, FCM y las videollamadas aún no están implementados.

## Abrir y compilar

1. Abrir la carpeta `android/` en una versión estable de Android Studio compatible con AGP 9.4.
2. Usar JDK 17 o el JDK requerido por la versión de Android Studio instalada.
3. Sincronizar Gradle y compilar la variante Debug.
4. Si Android Studio solicita generar/actualizar el Gradle Wrapper, generar los scripts y el JAR desde la instalación local de Gradle y añadirlos en un commit posterior.

## Identificador

Se usa provisionalmente `com.dietoexpress.mobile`. Antes de publicar, confirmar que no está registrado por otra app y mantenerlo estable después de crear la ficha en Google Play.

## API

La URL por defecto apunta al entorno HTTPS de pruebas actual. La URL se debe parametrizar por variantes de build antes de añadir entornos de desarrollo adicionales. No añadir secretos ni credenciales Firebase al repositorio.

## Siguiente bloque

Implementar cliente HTTP, manejo de sesión basada en la cookie `dietoexpress_patient_session`, errores HTTP y pruebas unitarias antes de añadir pantallas que expongan datos del paciente.
