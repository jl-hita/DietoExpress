# RGPD/LOPDGDD — Mapa inicial de tratamientos DietoExpress

> Documento de trabajo. Debe convertirse en el Registro de Actividades de Tratamiento (RAT/ROPA) y validarse jurídicamente con el modelo empresarial real.

## A. Tratamientos propios de DietoExpress

| Tratamiento | Finalidad | Datos | Interesados | Base jurídica a validar | Retención a definir |
|---|---|---|---|---|---|
| Cuenta y autenticación | Crear y mantener cuentas | Identificación, contacto, credenciales técnicas, logs | Profesionales, admins | Contrato / obligación legal según caso | Mientras exista cuenta + plazo legal |
| Suscripción y facturación | Contratar y cobrar SaaS | Identificación, contacto, plan, pagos/facturación | Clientes | Contrato / obligación legal | Según obligación fiscal/mercantil |
| Soporte | Resolver incidencias | Contacto, contenido aportado, logs | Clientes | Contrato/interés legítimo a validar | Plazo de soporte + necesidad |
| Seguridad | Detectar abuso, ataques y fraude | IP, eventos, autenticación, logs | Usuarios | Interés legítimo/obligación legal a validar | Política de logs |
| Comunicaciones del servicio | Avisos técnicos/contractuales | Contacto, preferencias | Clientes | Contrato/obligación legal según caso | Mientras sean necesarias |
| Marketing | Promoción de DietoExpress | Contacto, preferencias | Leads/clientes | Consentimiento u otra base aplicable | Hasta baja/oposición + límites legales |
| Analítica | Mejorar producto | Eventos, identificadores | Usuarios | Base a validar; minimizar/pseudonimizar | Política específica |
| Cumplimiento | Evidenciar obligaciones | Aceptaciones, versiones, auditoría | Usuarios | Obligación legal/interés legítimo según tratamiento | Según obligación/evidencia |

## B. Tratamientos por cuenta de profesional/clínica

| Tratamiento | Datos principales | Responsable habitual a validar | DietoExpress |
|---|---|---|---|
| Expediente nutricional | Identificación, salud, evolución | Profesional/clínica | Encargado |
| Citas | Identificación, agenda, modalidad | Profesional/clínica | Encargado |
| Comunicaciones asistenciales | Mensajes, notificaciones | Profesional/clínica | Encargado |
| Documentos del paciente | PDFs, aceptación, hash, auditoría | Profesional/clínica | Encargado |
| Dietas y recomendaciones | Datos nutricionales, contenido asistencial | Profesional/clínica | Encargado |
| Check-ins/evolución | Datos aportados por paciente | Profesional/clínica | Encargado |
| Automatizaciones asistenciales | Datos necesarios para ejecutar reglas | Profesional/clínica | Encargado |
| Portal paciente | Identidad, documentos, citas, mensajes | Profesional/clínica | Encargado |

## C. Datos de especial sensibilidad

Debe identificarse explícitamente:
- datos de salud;
- medidas corporales y biometrías cuando constituyan datos de salud;
- información sobre patologías/alergias/intolerancias;
- documentación clínica;
- información de menores;
- datos de contacto;
- información de pago/facturación;
- logs de seguridad.

## D. Derechos

DietoExpress debe proporcionar mecanismos y/o soporte para que el responsable pueda atender:
- acceso;
- rectificación;
- supresión;
- oposición;
- limitación;
- portabilidad;
- derechos relacionados con decisiones automatizadas cuando resulten aplicables.

Debe distinguirse entre solicitudes dirigidas al profesional/clínica como responsable y solicitudes relativas a tratamientos propios de DietoExpress.

## E. Brechas

Implementar:
1. detección;
2. clasificación;
3. registro;
4. contención;
5. evaluación de riesgo;
6. comunicación al cliente responsable cuando DietoExpress actúe como encargado;
7. documentación de decisiones;
8. soporte para las obligaciones de notificación que correspondan.

La referencia temporal de 72 horas se aplica al responsable cuando proceda notificar una brecha a la autoridad; no debe trasladarse mecánicamente como un SLA contractual sin analizar el flujo real.

## F. EIPD / análisis de riesgos

Antes de cerrar el módulo debe documentarse:
- tratamientos de alto riesgo;
- escala;
- categorías especiales;
- automatizaciones;
- IA;
- perfilado;
- transferencias;
- proveedores;
- medidas existentes;
- riesgo residual;
- necesidad o no de EIPD;
- medidas adicionales.

## G. Conservación

No establecer un único TTL global.

Crear una matriz por tratamiento:
tratamiento → finalidad → base jurídica → plazo → evento de inicio → bloqueo → supresión/anonimización → excepción legal.

Para documentación asistencial deberá contemplarse además la legislación clínica aplicable y la normativa autonómica.

## H. Privacidad desde el diseño

Todo nuevo módulo que trate datos personales debe pasar un checklist:
- ¿qué datos necesita?
- ¿para qué?
- ¿quién decide la finalidad?
- ¿es necesario almacenar el dato?
- ¿es dato de salud?
- ¿quién puede verlo?
- ¿cuánto tiempo?
- ¿qué proveedor interviene?
- ¿hay transferencia internacional?
- ¿hay automatización/IA?
- ¿qué evidencia queda?
- ¿cómo se atienden los derechos?
