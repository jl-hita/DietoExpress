# Menores, comunicaciones y modelo de clínica — DietoExpress

> Documento de diseño funcional y de cumplimiento. Requiere validación jurídica antes de publicación.

## Menores

El producto debe permitir distinguir:

- paciente menor;
- representante legal;
- usuario profesional autorizado.

El flujo debe poder registrar la representación y la documentación necesaria sin mezclar la identidad del menor con la del representante.

Antes de habilitar un flujo específico para menores deberán validarse los requisitos aplicables a la edad, consentimiento, representación y comunicaciones electrónicas.

## Comunicaciones

Separar al menos:

1. comunicaciones asistenciales necesarias para prestar el servicio;
2. comunicaciones contractuales/técnicas;
3. comunicaciones de marketing.

Las preferencias de marketing no deben utilizarse para bloquear comunicaciones asistenciales necesarias.

Cada canal debe poder quedar identificado cuando sea relevante: email, SMS, WhatsApp u otros proveedores que se incorporen.

## Modelo de clínica

El sistema debe preservar la responsabilidad de la clínica/profesional sobre el expediente de sus pacientes y permitir:

- reasignación de pacientes;
- baja de un profesional;
- continuidad del acceso autorizado;
- exportación;
- cierre de tenant;
- conservación o eliminación conforme a instrucciones y obligaciones aplicables.

## Estado

- Asignaciones multi-nutricionista: implementadas.
- Baja/archivo de profesionales: parcialmente implementado.
- Reasignación de pacientes: existente en el roadmap funcional.
- Flujo específico de menores: pendiente.
- Preferencias completas por finalidad/canal: pendiente.
- Procedimiento de cierre de clínica: pendiente.
