# Revisión legal y DPA de LiveKit para DietoExpress

**Estado:** revisión técnica/documental previa a producción  
**Fecha:** 2026-10-06

## Conclusión

La integración técnica puede continuar, pero no debe considerarse cerrada para producción hasta completar la revisión contractual y de privacidad.

El DPA de LiveKit, actualizado el 4 de septiembre de 2026, contempla a LiveKit como Processor para Customer Content y mecanismos de transferencia internacional como las SCC de la UE. Para DietoExpress, las videollamadas pueden contener información de salud o nutrición, por lo que debe verificarse la adecuación del servicio y aceptar el DPA aplicable.

## Pendiente antes de producción

1. Confirmar que el proyecto usa región de datos European Union (Frankfurt).
2. Revisar la lista vigente de subprocesadores y conservar evidencia de la revisión.
3. Confirmar el mecanismo contractual de transferencias internacionales.
4. No habilitar grabaciones, transcripciones, egress o agentes sin revisión específica.
5. Revisar si el plan contratado requiere region pinning para el nivel de cumplimiento buscado.
6. Revisar residencia y transferencias de cualquier servicio adicional conectado a LiveKit.
7. Mantener tokens y credenciales bajo control del servidor y limitar su duración.

## Minimización

El flujo actual no habilita grabación ni agentes/observabilidad de LiveKit. Para producción se debe mantener la minimización: no almacenar grabaciones, no activar transcripciones, no usar LiveKit Inference/Agents para la consulta clínica, no enviar datos clínicos innecesarios y revocar la sala al finalizar.

Esta revisión es técnica y no sustituye asesoramiento jurídico.

## Fuentes oficiales

- https://livekit.com/legal/data-processing-addendum
- https://livekit.com/legal/sub-processors
- https://docs.livekit.io/deploy/admin/regions/data-residency/
- https://docs.livekit.io/deploy/admin/regions/region-pinning/
- https://livekit.com/legal/terms-of-service
- https://livekit.com/legal/privacy-policy
