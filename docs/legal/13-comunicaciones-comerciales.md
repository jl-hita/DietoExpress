# Comunicaciones asistenciales y comerciales

DietoExpress separa técnicamente las comunicaciones necesarias para la prestación del servicio de las comunicaciones comerciales.

## Comunicaciones asistenciales

Incluyen avisos necesarios para citas, documentación, seguimiento, acceso al portal, incidencias de servicio y otras acciones directamente relacionadas con la atención o el servicio contratado.

La preferencia de baja comercial **no bloquea** estas comunicaciones.

## Comunicaciones comerciales

Las promociones, novedades comerciales, campañas y contenidos de marketing deben enviarse únicamente a destinatarios con una preferencia comercial explícita y vigente, conforme a la base jurídica validada para el caso concreto.

La preferencia comercial de email se almacena separadamente de las preferencias asistenciales in_app, email y push.

## Baja

Los emails comerciales deben incluir un enlace sencillo de baja.

El enlace debe utilizar un token opaco. DietoExpress almacena únicamente su hash y no necesita exponer el identificador del paciente en la URL.

Al utilizarlo:
1. la preferencia de email comercial pasa a false;
2. se registra la fecha de baja;
3. los siguientes envíos comerciales deben comprobar la preferencia antes de enviar;
4. la baja no altera las comunicaciones asistenciales.

Una nueva alta comercial genera un token nuevo.

## Regla para desarrollos futuros

No reutilizar patient_communication_preferences.email_enabled para interpretar consentimiento comercial. Esa preferencia controla el canal de comunicaciones asistenciales automatizadas.

Cualquier nuevo flujo de marketing debe pasar por la capa de comunicaciones comerciales y comprobar el consentimiento/preferencia justo antes del envío.