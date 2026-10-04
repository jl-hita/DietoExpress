# Catálogo de variables legales

Las plantillas de `docs/legal/templates/` usan variables para evitar introducir datos reales directamente en el código o en los textos jurídicos.

## Ámbitos

- `platform.*`: titular y configuración legal de DietoExpress. Solo SuperAdmin.
- `professional.*`: profesional o clínica que utiliza DietoExpress. Se edita desde Ajustes → Legal.
- `rat.*`, `retention.*`, `risk.*`, `provider.*`: documentación interna de cumplimiento. Se completará con la información real durante el cierre del gate.

## Principio de publicación

Un documento con variables sin resolver no debe publicarse.

La aplicación deberá considerar una plantilla como **pendiente de completar** mientras contenga una variable `{{...}}` o mientras falte alguno de los datos marcados como obligatorios para ese documento.

## Datos del profesional/clínica

Los campos configurables incluyen identidad legal, NIF/CIF, domicilio, contacto, privacidad, DPO cuando proceda, web, información profesional cuando proceda, base jurídica definida por el profesional, destinatarios, conservación, información del servicio y condiciones económicas.

En una clínica, los datos de identidad legal pertenecen al ámbito de la organización/tenant. En una cuenta profesional individual pertenecen al usuario profesional.

## Datos de DietoExpress

SuperAdmin configura identidad del titular, NIF/CIF, domicilio, contacto, privacidad, DPO cuando proceda, web, registro/autorizaciones, proveedores, transferencias, conservación, contratación, soporte, reclamaciones, cookies, subencargados y control de versión documental.

## Importante

Los textos son plantillas de trabajo para adaptar a la actividad real. No constituyen por sí mismos una certificación de cumplimiento ni sustituyen una revisión jurídica.


## Campos añadidos en la revisión documental

- `professional.dpa_duration`: duración del encargo.
- `professional.dpa_end_of_service_summary`: devolución/supresión al finalizar.
- `professional.patient_additional_purposes`: finalidades adicionales para pacientes.
- `professional.patient_special_categories_summary`: categorías especiales u otros datos adicionales.
- `professional.consultation_risks`: riesgos relevantes, cuando proceda.
- `platform.breach_notification_summary`: procedimiento/canal de notificación de brechas al responsable.
- `rat.dpo_contact`: contacto del DPD/DPD, si procede.
- `rat.*_data_categories`, `rat.*_recipients`, `rat.*_transfers`: detalle del RAT por actividad.
- `rat.security_measures_reference`: referencia a medidas técnicas y organizativas.

Estos campos no se deben rellenar con valores genéricos para superar el gate: deben representar la situación real de DietoExpress o del profesional/centro. La plantilla sigue siendo un documento de trabajo y requiere revisión jurídica antes de publicación.
