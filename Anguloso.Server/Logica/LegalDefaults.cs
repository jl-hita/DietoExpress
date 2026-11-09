namespace Anguloso.Server.Logica;

/// <summary>
/// Valores recomendados de la configuración legal que pueden deducirse del funcionamiento real de la
/// aplicación. No incluyen datos que solo conoce el titular (identidad, proveedores concretos de alojamiento,
/// correo o copias de seguridad, precios, soporte, transferencias): esos campos siguen siendo obligatorios.
/// La interfaz solo los vuelca en campos vacíos y el usuario debe revisarlos y guardarlos.
/// </summary>
public static class LegalDefaults
{
    public static IReadOnlyDictionary<string, string> Platform()
    {
        var today = DateTime.UtcNow.ToString("dd/MM/yyyy");
        return new Dictionary<string, string>
        {
            ["document_version"] = "1.0",
            ["last_update_date"] = today,
            ["governing_law_summary"] =
                "Estas condiciones y el uso de la plataforma se rigen por la legislación española. Si eres consumidor, serán competentes los juzgados y tribunales de tu domicilio; en los demás casos, los del domicilio del titular, salvo que una norma imperativa disponga otra cosa.",
            ["non_essential_cookies_summary"] =
                "No se utilizan cookies de analítica, publicidad ni otras tecnologías no esenciales. Si en el futuro se incorporasen, solo se activarían tras el consentimiento del usuario.",
            ["cookie_third_parties"] =
                "Google Identity Services (inicio de sesión con Google), Stripe (pago alojado), LiveKit (videoconsulta, bajo demanda), Google Calendar (sincronización de agenda, bajo demanda) y Web Push (avisos, solo si el usuario los activa).",
            ["retention_policy_reference"] =
                "la matriz de conservación y supresión vigente de DietoExpress, que fija un plazo por cada tratamiento",
            ["retention_matrix_summary"] = string.Join("\n", new[]
            {
                "- **Cuentas y autenticación:** mientras dure la relación contractual; tras la baja se suprimen o anonimizan, salvo lo necesario para facturación y defensa de reclamaciones.",
                "- **Contratación y facturación:** el plazo que exigen las normas fiscales y mercantiles (con carácter general, seis años), bloqueados tras el fin de su finalidad.",
                "- **Datos de pacientes (por cuenta del profesional o clínica):** los que indique el responsable. Al terminar el servicio se ponen a su disposición para su exportación y después se suprimen, salvo obligación legal de conservación de la historia clínica, que corresponde al responsable.",
                "- **Aceptaciones y evidencias legales:** mientras puedan ser necesarias para acreditar la relación y defender reclamaciones (plazo general de prescripción).",
                "- **Registros de seguridad y auditoría:** doce meses con rotación automática, ampliables mientras dure un incidente o una investigación.",
                "- **Solicitudes de derechos y registro de brechas:** el tiempo necesario para acreditar su atención y, como mínimo, el plazo de prescripción de posibles responsabilidades.",
                "- **Copias de seguridad:** caducan automáticamente conforme a su ciclo de vida documentado; los datos suprimidos desaparecen de ellas al expirar la copia."
            }),
            ["breach_notification_summary"] =
                "Ante una violación de la seguridad, DietoExpress la contiene, la registra y evalúa su riesgo. Cuando actúa como encargado, avisa al responsable (profesional o clínica) sin dilación indebida y como máximo en 48 horas. Cuando es responsable y hay riesgo para las personas, notifica a la Agencia Española de Protección de Datos en 72 horas y, si el riesgo es alto, informa a los afectados. Puedes comunicar un incidente al correo de privacidad indicado en el apartado 1.",
            ["rat_controller_role"] =
                "Responsable del tratamiento (cuentas, contratación, soporte y seguridad) y encargado del tratamiento (datos de pacientes por cuenta de profesionales y clínicas).",
            ["rat_review_date"] = today,
            ["rat_accounts_summary"] =
                "Finalidad: crear y gestionar cuentas y accesos. Interesados: profesionales, personal de clínicas y administradores. Datos: identificativos, contacto y credenciales. Base: ejecución del contrato (art. 6.1.b RGPD). Destinatarios: proveedores tecnológicos como encargados. Conservación: durante la relación contractual.",
            ["rat_billing_summary"] =
                "Finalidad: contratar, facturar y cumplir obligaciones fiscales y mercantiles. Interesados: clientes profesionales y clínicas. Datos: identificativos, facturación y referencias de pago (el proveedor de pagos gestiona los datos de la tarjeta). Base: contrato y obligación legal (art. 6.1.b y 6.1.c). Destinatarios: proveedor de pagos y Administración tributaria. Conservación: plazos fiscales y mercantiles.",
            ["rat_patient_summary"] =
                "Tratamiento como encargado por cuenta de cada profesional o clínica (responsable): gestión de pacientes, dietas, documentación, agenda, comunicaciones y videoconsulta. Interesados: pacientes y personas de contacto. Datos: identificativos, de contacto y de salud (categorías especiales). Base: determinada por el responsable, normalmente ejecución del contrato y art. 9.2.h RGPD. Conservación: instrucciones del responsable.",
            ["rat_security_summary"] =
                "Finalidad: proteger la plataforma y los datos, auditar accesos y gestionar incidencias. Datos: direcciones IP, registros técnicos y de auditoría. Base: interés legítimo y obligación de seguridad (art. 6.1.f y 6.1.c). Conservación: doce meses con rotación, salvo incidente.",
            ["rat_rights_summary"] =
                "Finalidad: atender solicitudes de derechos y reclamaciones. Interesados: quien ejerce el derecho. Datos: los necesarios para identificar y resolver la solicitud. Base: obligación legal (art. 6.1.c). Plazo de respuesta: un mes. Si la solicitud se refiere a datos de pacientes, se traslada al responsable.",
            ["risk_version"] = "1.0",
            ["risk_date"] = today,
            ["risk_scope"] =
                "Plataforma DietoExpress en su conjunto: cuentas de profesionales, gestión de pacientes con datos de salud, documentación, comunicaciones, videoconsulta, pagos y registros de seguridad.",
            ["risk_summary"] =
                "- Acceso no autorizado a historiales con datos de salud.\n- Acceso de un cliente a datos de otro cliente (aislamiento entre centros).\n- Pérdida o alteración de datos.\n- Divulgación por error de documentos o planes a un tercero.\n- Dependencia de proveedores y transferencias internacionales.\n- Uso de datos para fines distintos de los informados.",
            ["risk_controls_summary"] =
                "- Contraseñas almacenadas con hash; cookies de sesión HttpOnly, Secure y SameSite=Strict.\n- Control de acceso por rol y por centro, con comprobación en cada operación.\n- Registro de auditoría de los accesos a información clínica.\n- Documentos de pacientes en almacenamiento privado, nunca como recursos públicos, con huella SHA-256 y evidencia de aceptación.\n- Enlaces de baja y tokens opacos almacenados como hash.\n- Videoconsulta sin grabación ni transcripción por defecto.\n- Procedimientos de derechos y de brechas.",
            ["risk_residual_risk_summary"] =
                "Medio, aceptable con las medidas descritas, a revisar cada vez que cambien los tratamientos, los proveedores o las transferencias.",
            ["risk_dpia_decision"] =
                "Se considera necesaria una evaluación de impacto (EIPD) para los tratamientos de datos de salud a gran escala.",
            ["risk_dpia_justification"] =
                "El tratamiento de categorías especiales de datos a gran escala figura entre los supuestos del art. 35.3.b RGPD y en la lista de tratamientos que requieren EIPD de la AEPD. Como encargado, DietoExpress ayuda a cada responsable a realizar la suya.",
            ["risk_next_review"] = "A los doce meses de la fecha de este análisis o antes si cambia el tratamiento."
        };
    }

    public static IReadOnlyDictionary<string, string> Professional() => new Dictionary<string, string>
    {
        ["patient_privacy_legal_basis"] =
            "Ejecución del contrato o de medidas precontractuales de prestación del servicio de nutrición (art. 6.1.b RGPD), obligaciones legales (art. 6.1.c) y, para atender reclamaciones y la seguridad, interés legítimo (art. 6.1.f).",
        ["patient_special_categories_summary"] =
            "Los datos de salud se tratan porque son necesarios para la prestación de asistencia sanitaria o nutricional por un profesional sujeto a secreto profesional (art. 9.2.h y 9.3 RGPD y art. 9 LOPDGDD). Si el servicio no tuviera carácter sanitario, se pediría tu consentimiento explícito (art. 9.2.a RGPD).",
        ["patient_recipients_summary"] =
            "DietoExpress, como plataforma tecnológica que actúa de encargado del tratamiento; el proveedor del servicio de videoconsulta cuando uses la teleconsulta; y las administraciones y autoridades cuando exista una obligación legal.",
        ["patient_retention_summary"] =
            "Tu historia y documentación clínica se conservan como mínimo cinco años desde la última asistencia (art. 17 Ley 41/2002) o el plazo mayor que establezca la normativa autonómica aplicable. Los datos de facturación, el tiempo que exijan las normas fiscales. Después se bloquean y se suprimen.",
        ["patient_additional_purposes"] =
            "Ninguna otra. Fotografías, comunicaciones comerciales y cesiones a terceros solo se tratarán si las autorizas expresamente."
    };
}
