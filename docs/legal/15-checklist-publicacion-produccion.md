# Checklist de publicación legal en producción

Los textos y el flujo técnico están preparados. Lo que sigue **solo puede aportarlo el titular** o validarlo un profesional del derecho. La aplicación no permite publicar documentos con datos sin resolver, y el panel de preparación (`/api/legal-documents/readiness`) indica qué falta.

## 1. Datos del titular (Configuración legal de DietoExpress)

Rellenar a mano, no hay valor recomendado:

- Denominación, NIF/CIF, domicilio, teléfono, sitio web.
- Correos: general, privacidad, soporte y reclamaciones. DPO solo si se ha designado.
- Información registral (Registro Mercantil u otra) si procede.
- Precios y planes, facturación y renovación, cancelación, reembolsos y desistimiento.
- Condiciones de soporte.
- Responsable del análisis de riesgos (`risk_owner`).

## 2. Proveedores reales (no deducibles del código)

- **Proveedores y destinatarios** y **subencargados**: alojamiento/servidor, base de datos, copias de seguridad, proveedor de correo SMTP, Stripe, Google (OAuth y Calendar) y LiveKit. Para cada uno: entidad, servicio, ubicación y subcontratación.
- **Transferencias internacionales**: indicar si existen y el mecanismo (decisión de adecuación, cláusulas contractuales tipo). Confirmar para LiveKit la región de datos de la UE y aceptar su DPA (ver `05-livekit-dpa-y-consulta-online.md`).
- Contrato/DPA firmado con cada proveedor.

## 3. Valores recomendados

En las pantallas de configuración legal, el botón **Cargar valores recomendados** rellena solo los campos vacíos que se deducen del funcionamiento de la aplicación (RAT, conservación, brechas, cookies, análisis de riesgos, ley aplicable y bases jurídicas del paciente). **Revísalos antes de guardar**, en especial:

- los plazos de conservación (sobre todo la normativa autonómica de historia clínica);
- la decisión sobre EIPD y el riesgo residual;
- que la lista de medidas de seguridad coincide con la realidad de la infraestructura (cifrado en reposo, copias, accesos).

## 4. Cada profesional o clínica

Antes de recibir pacientes debe completar su configuración legal (identificación, titulación y colegiación, descripción del servicio y límites, riesgos, transferencias). Sin ella la aplicación no emite documentación al paciente ni permite iniciar consultas.

## 5. Publicación

1. Superadmin: completar y guardar la configuración legal de la plataforma.
2. Generar y publicar aviso legal, política de privacidad, términos, política de cookies y acuerdo de encargo (los términos se publican con la clave `saas_terms`, imprescindible para el alta y la facturación).
3. Comprobar que `readiness` devuelve `ready: true` y que `/legal` muestra los documentos.
4. Generar para uso interno (sin publicar): RAT, matriz de conservación y análisis de riesgos.

## 6. Revisión jurídica externa

Antes de abrir la plataforma a clientes reales con datos de salud, un abogado debería revisar los textos publicados, el acuerdo de encargo, las bases jurídicas, los plazos de conservación y la decisión de EIPD. Este checklist y los textos son un punto de partida técnico, no una certificación de cumplimiento.

## 7. Otras comprobaciones técnicas antes de producción

- Decidir y documentar la clasificación jurídica de la cookie `g_state` (Google) y si hace falta banner.
- Verificar copias de seguridad y restauración, y el ciclo de vida de las copias.
- Confirmar que la videoconsulta no graba ni transcribe.
