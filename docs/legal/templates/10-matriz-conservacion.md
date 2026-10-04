# Matriz de conservación y supresión

| Tratamiento | Evento inicial | Plazo | Bloqueo | Supresión/anonimización | Excepción |
|---|---|---|---|---|---|
| Cuenta profesional | {{retention.account_start}} | {{retention.account_period}} | {{retention.account_lock}} | {{retention.account_delete}} | {{retention.account_exception}} |
| Facturación | {{retention.billing_start}} | {{retention.billing_period}} | {{retention.billing_lock}} | {{retention.billing_delete}} | {{retention.billing_exception}} |
| Expediente/paciente | {{retention.patient_start}} | {{retention.patient_period}} | {{retention.patient_lock}} | {{retention.patient_delete}} | {{retention.patient_exception}} |
| Aceptaciones legales | {{retention.acceptance_start}} | {{retention.acceptance_period}} | {{retention.acceptance_lock}} | {{retention.acceptance_delete}} | {{retention.acceptance_exception}} |
| Logs/auditoría | {{retention.logs_start}} | {{retention.logs_period}} | {{retention.logs_lock}} | {{retention.logs_delete}} | {{retention.logs_exception}} |
| Backups | {{retention.backup_start}} | {{retention.backup_period}} | {{retention.backup_lock}} | {{retention.backup_delete}} | {{retention.backup_exception}} |

**Nota:** los plazos deben determinarse según finalidad, obligación legal y actividad real; no utilizar valores inventados.
