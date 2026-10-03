using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = "Professional")]
public class ClientsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EnergyCalculatorService _calculatorService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILicenseService _licenseService;
    private readonly AutomationService _automationService;
    private readonly PatientDocumentService _patientDocumentService;

    public ClientsController(
        angulosodbContext context, 
        EnergyCalculatorService calculatorService,
        IAuditLogService auditLogService,
        ILicenseService licenseService,
        AutomationService automationService,
        PatientDocumentService patientDocumentService)
    {
        _context = context;
        _calculatorService = calculatorService;
        _auditLogService = auditLogService;
        _licenseService = licenseService;
        _automationService = automationService;
        _patientDocumentService = patientDocumentService;
    }

    // GET: api/clients
    [HttpGet]
    public async Task<ActionResult<object>> GetClients([FromQuery] bool includeAll = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        var isClinicAdmin = User.IsInRole("clinic_admin");
        var isSuperAdmin = User.IsInRole("superadmin");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var searchTerm = search?.Trim();
        if (searchTerm?.Length > 100) return BadRequest("El texto de búsqueda no puede superar los 100 caracteres.");

        // La consulta aplica primero aislamiento y asignación; la paginación y búsqueda se ejecutan sobre ese conjunto ya autorizado para no convertir filtros del frontend en un mecanismo de acceso.
        var query = _context.clients
            .Where(c => c.archived_at == null)
            .Where(c => includeAll && isSuperAdmin
                ? true
                : (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                   (c.user_id == userId.Value ||
                    _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                    isClinicAdmin)));

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.full_name, pattern) ||
                EF.Functions.ILike(c.email, pattern) ||
                EF.Functions.ILike(c.phone, pattern));
        }

        var totalCount = await query.CountAsync();
        var list = await query
            .OrderByDescending(c => c.created_at)
            .ThenByDescending(c => c.id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClientListDto
            {
                Id = c.id,
                FullName = c.full_name,
                Email = c.email,
                Phone = c.phone,
                Gender = c.gender,
                BirthDate = c.birth_date.HasValue ? new DateTime?(c.birth_date.Value.ToDateTime(TimeOnly.MinValue)) : null,
                CreatedAt = c.created_at,
                LifecycleStatus = c.lifecycle_status,
                LastActivityAt = c.last_activity_at
            })
            .ToListAsync();

        return Ok(new { items = list, totalCount, page, pageSize });
    }

    // GET: api/clients/can-create
    [HttpGet("can-create")]
    public async Task<IActionResult> CanCreateClient()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        var licenseCheck = await _licenseService.CanCreateClientAsync(tenantId, userId.Value);
        return Ok(new { allowed = licenseCheck.Allowed, reason = licenseCheck.Reason });
    }

    // GET: api/clients/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClientDetailDto>> GetClient(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        var isClinicAdmin = User.IsInRole("clinic_admin");
        var isSuperAdmin = User.IsInRole("superadmin");

        // Se carga en una única consulta la ficha clínica y sus relaciones necesarias. El límite del histórico evita que una ficha con muchos años de mediciones genere una respuesta desproporcionada.
        var client = await _context.clients
            .Include(c => c.biometrics.OrderByDescending(b => b.measurement_date).Take(500))
            .Include(c => c.medical_history)
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.lifestyle_history)
            .FirstOrDefaultAsync(c => c.id == id && c.archived_at == null &&
                (isSuperAdmin || (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                (c.user_id == userId.Value ||
                 _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                 isClinicAdmin))));

        if (client == null) return NotFound();

        // Trazabilidad de acceso a datos clínicos (Art. 32 RGPD y Ley 41/2002)
        // El acceso a la historia clínica se registra después de comprobar autorización y antes de devolver los datos, dejando trazabilidad del acceso sin registrar intentos rechazados como lecturas válidas.
        await _auditLogService.LogAccessAsync(
            action: "READ_MEDICAL_CHART",
            entityName: "clients",
            entityId: id.ToString(),
            clientId: client.id,
            details: "Acceso a la historia clínica y anamnesis del paciente"
        );

        var dto = new ClientDetailDto
        {
            Id = client.id,
            FullName = client.full_name,
            Email = client.email,
            Phone = client.phone,
            Gender = client.gender,
            BirthDate = client.birth_date.HasValue ? new DateTime?(client.birth_date.Value.ToDateTime(TimeOnly.MinValue)) : null,
            CreatedAt = client.created_at,
            LifecycleStatus = client.lifecycle_status,
            LastActivityAt = client.last_activity_at,
            Notes = client.notes
        };

        if (client.medical_history != null)
        {
            dto.MedicalHistory = new MedicalHistoryDto
            {
                Id = client.medical_history.id,
                Diabetes = client.medical_history.diabetes,
                Hypertension = client.medical_history.hypertension,
                Hypothyroidism = client.medical_history.hypothyroidism,
                Surgeries = client.medical_history.surgeries,
                RoutineMedication = client.medical_history.routine_medication,
                OtherPathologies = client.medical_history.other_pathologies
            };
        }

        if (client.digestive_health != null)
        {
            dto.DigestiveHealth = new DigestiveHealthDto
            {
                Id = client.digestive_health.id,
                IntestinalHabits = client.digestive_health.intestinal_habits,
                Bloating = client.digestive_health.bloating,
                Heartburn = client.digestive_health.heartburn,
                GlutenIntolerance = client.digestive_health.gluten_intolerance,
                LactoseIntolerance = client.digestive_health.lactose_intolerance,
                FodmapsIntolerance = client.digestive_health.fodmaps_intolerance,
                OtherIntolerances = client.digestive_health.other_intolerances,
                Notes = client.digestive_health.notes
            };
        }

        if (client.food_preferences != null)
        {
            dto.FoodPreferences = new FoodPreferencesDto
            {
                Id = client.food_preferences.id,
                PreferredFoods = client.food_preferences.preferred_foods,
                DislikedFoods = client.food_preferences.disliked_foods,
                Allergies = client.food_preferences.allergies
            };
        }

        if (client.lifestyle_history != null)
        {
            dto.LifestyleHistory = new LifestyleHistoryDto
            {
                Id = client.lifestyle_history.id,
                WorkSchedule = client.lifestyle_history.work_schedule,
                SleepHabits = client.lifestyle_history.sleep_habits,
                WaterConsumption = client.lifestyle_history.water_consumption,
                AlcoholConsumption = client.lifestyle_history.alcohol_consumption,
                TobaccoConsumption = client.lifestyle_history.tobacco_consumption
            };
        }

        dto.Biometrics = client.biometrics
            .OrderByDescending(b => b.measurement_date)
            .Select(b => new BiometricsDto
            {
                Id = b.id,
                MeasurementDate = b.measurement_date.ToDateTime(TimeOnly.MinValue),
                Weight = b.weight,
                Height = b.height,
                BodyFat = b.body_fat,
                MuscleMass = b.muscle_mass,
                VisceralFat = b.visceral_fat,
                Waist = b.waist,
                Hip = b.hip,
                Neck = b.neck,
                Triceps = b.triceps,
                Abdomen = b.abdomen,
                Thigh = b.thigh,
                Subscapular = b.subscapular,
                Suprailiac = b.suprailiac,
                Notes = b.notes
            }).ToList();

        return Ok(dto);
    }

    private static string? ValidateClientPayload(CreateClientDto? dto) => dto == null ? "Datos del paciente no válidos." : ValidateClientFields(dto.FullName, dto.Email, dto.Phone, dto.Gender, dto.Notes, dto.MedicalHistory, dto.DigestiveHealth, dto.FoodPreferences, dto.LifestyleHistory);

    private static string? ValidateClientPayload(UpdateClientDto? dto) => dto == null ? "Datos del paciente no válidos." : ValidateClientFields(dto.FullName, dto.Email, dto.Phone, dto.Gender, dto.Notes, dto.MedicalHistory, dto.DigestiveHealth, dto.FoodPreferences, dto.LifestyleHistory);

    private static string? ValidateClientFields(string? fullName, string? email, string? phone, string? gender, string? notes,
        MedicalHistoryDto? medical, DigestiveHealthDto? digestive, FoodPreferencesDto? preferences, LifestyleHistoryDto? lifestyle)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200) return "El nombre del paciente es obligatorio y no puede superar los 200 caracteres.";
        if (email?.Length > 254) return "El email no puede superar los 254 caracteres.";
        if (phone?.Length > 50) return "El teléfono no puede superar los 50 caracteres.";
        if (gender?.Length > 50) return "El género no puede superar los 50 caracteres.";
        if (notes?.Length > 10000) return "Las notas no pueden superar los 10000 caracteres.";
        if (medical != null && (medical.Surgeries?.Length > 5000 || medical.RoutineMedication?.Length > 5000 || medical.OtherPathologies?.Length > 5000)) return "Los datos de antecedentes superan el tamaño permitido.";
        if (digestive != null && (digestive.IntestinalHabits?.Length > 5000 || digestive.OtherIntolerances?.Length > 5000 || digestive.Notes?.Length > 5000)) return "Los datos digestivos superan el tamaño permitido.";
        if (preferences != null && (preferences.PreferredFoods?.Length > 5000 || preferences.DislikedFoods?.Length > 5000 || preferences.Allergies?.Length > 5000)) return "Las preferencias alimentarias superan el tamaño permitido.";
        if (lifestyle != null && (lifestyle.WorkSchedule?.Length > 5000 || lifestyle.SleepHabits?.Length > 5000 || lifestyle.WaterConsumption?.Length > 5000 || lifestyle.AlcoholConsumption?.Length > 5000 || lifestyle.TobaccoConsumption?.Length > 5000)) return "Los datos de estilo de vida superan el tamaño permitido.";
        return null;
    }

    // POST: api/clients
    [HttpPost]
    public async Task<ActionResult> CreateClient([FromBody] CreateClientDto dto)
    {
        var validationError = ValidateClientPayload(dto);
        if (validationError != null) return BadRequest(validationError);

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");

        clients client;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Serializamos las altas por tenant para que el límite de pacientes de la licencia
            // no pueda superarse mediante peticiones concurrentes. El bloqueo vive solo durante
            // esta transacción y hace que la comprobación y el alta formen una operación lógica.
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            var licenseCheck = await _licenseService.CanCreateClientAsync(tenantId, userId.Value);
            if (!licenseCheck.Allowed) return BadRequest(licenseCheck.Reason);

            client = new clients
            {
            user_id = userId.Value,
            tenant_id = AuthHelpers.GetTenantId(User),
            full_name = dto.FullName,
            email = dto.Email ?? "",
            phone = dto.Phone ?? "",
            gender = dto.Gender ?? "",
            notes = dto.Notes ?? "",
            created_at = DateTime.UtcNow
        };

        if (dto.BirthDate.HasValue)
            client.birth_date = DateOnly.FromDateTime(dto.BirthDate.Value);

        // Initialize anamnesis tables
        client.medical_history = new medical_history
        {
            diabetes = dto.MedicalHistory?.Diabetes ?? false,
            hypertension = dto.MedicalHistory?.Hypertension ?? false,
            hypothyroidism = dto.MedicalHistory?.Hypothyroidism ?? false,
            surgeries = dto.MedicalHistory?.Surgeries ?? "",
            routine_medication = dto.MedicalHistory?.RoutineMedication ?? "",
            other_pathologies = dto.MedicalHistory?.OtherPathologies ?? ""
        };

        client.digestive_health = new digestive_health
        {
            intestinal_habits = dto.DigestiveHealth?.IntestinalHabits ?? "",
            bloating = dto.DigestiveHealth?.Bloating ?? false,
            heartburn = dto.DigestiveHealth?.Heartburn ?? false,
            gluten_intolerance = dto.DigestiveHealth?.GlutenIntolerance ?? false,
            lactose_intolerance = dto.DigestiveHealth?.LactoseIntolerance ?? false,
            fodmaps_intolerance = dto.DigestiveHealth?.FodmapsIntolerance ?? false,
            other_intolerances = dto.DigestiveHealth?.OtherIntolerances ?? "",
            notes = dto.DigestiveHealth?.Notes ?? ""
        };

        client.food_preferences = new food_preferences
        {
            preferred_foods = dto.FoodPreferences?.PreferredFoods ?? "",
            disliked_foods = dto.FoodPreferences?.DislikedFoods ?? "",
            allergies = dto.FoodPreferences?.Allergies ?? ""
        };

        client.lifestyle_history = new lifestyle_history
        {
            work_schedule = dto.LifestyleHistory?.WorkSchedule ?? "",
            sleep_habits = dto.LifestyleHistory?.SleepHabits ?? "",
            water_consumption = dto.LifestyleHistory?.WaterConsumption ?? "",
            alcohol_consumption = dto.LifestyleHistory?.AlcoholConsumption ?? "",
            tobacco_consumption = dto.LifestyleHistory?.TobaccoConsumption ?? ""
        };

            _context.clients.Add(client);
            await _context.SaveChangesAsync();

            _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments
        {
            client_id = client.id,
            nutritionist_id = userId.Value,
            assigned_by_user_id = userId.Value,
            assigned_at = DateTime.UtcNow,
            is_active = true
            });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        // El evento se emite después de persistir el cliente; la automatización es un efecto derivado y no debe convertir un alta válida en un error de la operación principal.
        try
        {
            await _automationService.PublishEventAsync(
                tenantId.Value,
                "client.created",
                "client",
                client.id.ToString(),
                new AutomationService.ClientCreatedPayload(client.id, userId.Value),
                $"client:{client.id}:created");
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ClientsController>>()
                .LogError(ex, "No se pudo registrar la automatización de alta del paciente {ClientId}.", client.id);
        }

        // Generamos la documentación obligatoria una vez confirmado el alta.
        // Un fallo documental nunca invalida un paciente ya creado.
        try
        {
            var createdDocuments = await _patientDocumentService.CreateRequiredDocumentsAsync(
                tenantId.Value,
                client.id,
                userId.Value,
                HttpContext.RequestAborted);

            if (createdDocuments > 0)
            {
                await _automationService.ScheduleActionAsync(
                    tenantId.Value,
                    "notify_patient",
                    new NotifyPatientAction(
                        client.id,
                        "documents_pending",
                        "Tienes documentación pendiente",
                        "Tu nutricionista ha preparado documentación que debes revisar desde tu portal.",
                        "/patient?tab=documents"),
                    DateTime.UtcNow,
                    idempotencyKey: $"documents:created:{client.id}");
            }
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ClientsController>>()
                .LogError(ex, "No se pudo generar la documentación inicial del paciente {ClientId}.", client.id);
        }

        await _auditLogService.LogAccessAsync(
            action: "CREATE_PATIENT",
            entityName: "clients",
            entityId: client.id.ToString(),
            clientId: client.id,
            details: "Alta inicial de expediente clínico"
        );

        return CreatedAtAction(nameof(GetClient), new { id = client.id }, new { id = client.id });
    }

    // PUT: api/clients/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClient(int id, [FromBody] UpdateClientDto dto)
    {
        var validationError = ValidateClientPayload(dto);
        if (validationError != null) return BadRequest(validationError);

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);

        // Se cargan las tablas de anamnesis junto al paciente porque la actualización mantiene
        // el expediente clínico compuesto en una sola operación de persistencia.
        var client = await _context.clients
            .Include(c => c.medical_history)
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.lifestyle_history)
            .FirstOrDefaultAsync(c => c.id == id && c.archived_at == null &&
                tenantId.HasValue && c.tenant_id == tenantId.Value &&
                (c.user_id == userId.Value ||
                 _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                 User.IsInRole("clinic_admin")));

        if (client == null) return NotFound();

        client.full_name = dto.FullName;
        client.email = dto.Email ?? client.email;
        client.phone = dto.Phone ?? client.phone;
        client.gender = dto.Gender ?? client.gender;
        client.notes = dto.Notes ?? client.notes;
        if (dto.BirthDate.HasValue) client.birth_date = DateOnly.FromDateTime(dto.BirthDate.Value);

        // Update medical history
        if (dto.MedicalHistory != null)
        {
            if (client.medical_history == null) client.medical_history = new medical_history();
            client.medical_history.diabetes = dto.MedicalHistory.Diabetes;
            client.medical_history.hypertension = dto.MedicalHistory.Hypertension;
            client.medical_history.hypothyroidism = dto.MedicalHistory.Hypothyroidism;
            client.medical_history.surgeries = dto.MedicalHistory.Surgeries ?? "";
            client.medical_history.routine_medication = dto.MedicalHistory.RoutineMedication ?? "";
            client.medical_history.other_pathologies = dto.MedicalHistory.OtherPathologies ?? "";
        }

        // Update digestive health
        if (dto.DigestiveHealth != null)
        {
            if (client.digestive_health == null) client.digestive_health = new digestive_health();
            client.digestive_health.intestinal_habits = dto.DigestiveHealth.IntestinalHabits ?? "";
            client.digestive_health.bloating = dto.DigestiveHealth.Bloating;
            client.digestive_health.heartburn = dto.DigestiveHealth.Heartburn;
            client.digestive_health.gluten_intolerance = dto.DigestiveHealth.GlutenIntolerance;
            client.digestive_health.lactose_intolerance = dto.DigestiveHealth.LactoseIntolerance;
            client.digestive_health.fodmaps_intolerance = dto.DigestiveHealth.FodmapsIntolerance;
            client.digestive_health.other_intolerances = dto.DigestiveHealth.OtherIntolerances ?? "";
            client.digestive_health.notes = dto.DigestiveHealth.Notes ?? "";
        }

        // Update food preferences
        if (dto.FoodPreferences != null)
        {
            if (client.food_preferences == null) client.food_preferences = new food_preferences();
            client.food_preferences.preferred_foods = dto.FoodPreferences.PreferredFoods ?? "";
            client.food_preferences.disliked_foods = dto.FoodPreferences.DislikedFoods ?? "";
            client.food_preferences.allergies = dto.FoodPreferences.Allergies ?? "";
        }

        // Update lifestyle history
        if (dto.LifestyleHistory != null)
        {
            if (client.lifestyle_history == null) client.lifestyle_history = new lifestyle_history();
            client.lifestyle_history.work_schedule = dto.LifestyleHistory.WorkSchedule ?? "";
            client.lifestyle_history.sleep_habits = dto.LifestyleHistory.SleepHabits ?? "";
            client.lifestyle_history.water_consumption = dto.LifestyleHistory.WaterConsumption ?? "";
            client.lifestyle_history.alcohol_consumption = dto.LifestyleHistory.AlcoholConsumption ?? "";
            client.lifestyle_history.tobacco_consumption = dto.LifestyleHistory.TobaccoConsumption ?? "";
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/clients/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteClient(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);

        var client = await _context.clients.Include(c => c.biometrics).FirstOrDefaultAsync(c =>
            c.id == id && c.archived_at == null &&
            tenantId.HasValue && c.tenant_id == tenantId.Value &&
            (c.user_id == userId.Value || User.IsInRole("clinic_admin")));
        if (client == null) return NotFound();

        // Optionally: delete biometrics cascade if not configured
        client.archived_at = DateTime.UtcNow;
        client.lifecycle_status = "archived";
        client.lifecycle_status_changed_at = DateTime.UtcNow;
        client.access_token = null;
        client.access_token_expires_at = null;
        await _context.SaveChangesAsync();
        await _auditLogService.LogAccessAsync("ARCHIVE_PATIENT", "clients", client.id.ToString(), client.id, "Archivado del expediente clínico");

        return NoContent();
    }

    // GET: api/clients/{id}/energy-requirements
    [HttpGet("{id:int}/energy-requirements")]
    public async Task<ActionResult<EnergyRequirementsDto>> GetEnergyRequirements(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);

        var client = await _context.clients
            .Include(c => c.biometrics)
            .FirstOrDefaultAsync(c => c.id == id && c.archived_at == null &&
                (User.IsInRole("superadmin") || (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                (c.user_id == userId.Value ||
                 _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                 User.IsInRole("clinic_admin")))));

        if (client == null) return NotFound("Client not found.");

        if (!client.birth_date.HasValue)
        {
            return BadRequest("El paciente debe tener una fecha de nacimiento registrada para calcular sus necesidades calóricas.");
        }

        var latestBiometrics = client.biometrics
            .Where(b => b.weight.HasValue && b.height.HasValue)
            .OrderByDescending(b => b.measurement_date)
            .FirstOrDefault();

        if (latestBiometrics == null)
        {
            return BadRequest("El paciente debe tener al menos un registro biométrico con peso y altura para calcular sus necesidades calóricas.");
        }

        int age = DateTime.Today.Year - client.birth_date.Value.Year;
        if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age))) age--;

        var weightValue = latestBiometrics.weight;
        var heightValue = latestBiometrics.height;
        if (!weightValue.HasValue || !heightValue.HasValue || heightValue.Value <= 0)
            return BadRequest("El registro biométrico no contiene peso y altura válidos.");

        double weight = weightValue.Value;
        double height = heightValue.Value;
        double? bodyFat = latestBiometrics.body_fat.HasValue ? (double?)latestBiometrics.body_fat.Value : null;

        var result = _calculatorService.CalculateEnergyRequirements(weight, height, age, client.gender, bodyFat);
        return Ok(result);
    }

    // GET: api/clients/{id}/patient-profile
    // Devuelve el perfil público del paciente para el portal
    [HttpGet("{id:int}/patient-profile")]
    public async Task<ActionResult<PatientProfileDto>> GetPatientProfile(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);

        var client = await _context.clients
            .Include(c => c.biometrics.OrderByDescending(b => b.measurement_date))
            .Include(c => c.client_diets)
            .FirstOrDefaultAsync(c => c.id == id && c.archived_at == null &&
                tenantId.HasValue && c.tenant_id == tenantId.Value &&
                (c.user_id == userId.Value ||
                 _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active)));

        if (client == null) return NotFound();

        var latestBio = client.biometrics
            .OrderByDescending(b => b.measurement_date)
            .FirstOrDefault();

        var activeDietAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == id && cd.is_active == true &&
                         cd.diet != null && cd.diet.tenant_id == tenantId!.Value)
            .FirstOrDefaultAsync();

        int? age = null;
        if (client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - client.birth_date.Value.Year;
            if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }

        // Obtener últimos 8 pesos para mini gráfico de progreso
        var weightHistory = client.biometrics
            .Where(b => b.weight.HasValue)
            .OrderByDescending(b => b.measurement_date)
            .Take(8)
            .OrderBy(b => b.measurement_date)
            .Select(b => new WeightEntryDto
            {
                Date = b.measurement_date.ToDateTime(TimeOnly.MinValue),
                Weight = (double)(b.weight ?? 0)
            })
            .ToList();

        return Ok(new PatientProfileDto
        {
            ClientId = client.id,
            FullName = client.full_name ?? string.Empty,
            Age = age,
            Gender = client.gender,
            CurrentWeight = latestBio?.weight.HasValue == true ? (double?)latestBio.weight.Value : null,
            CurrentHeight = latestBio?.height.HasValue == true ? (double?)latestBio.height.Value : null,
            HasActiveDiet = activeDietAssignment != null,
            ActiveDietAssignmentId = activeDietAssignment?.id,
            ActiveDietStartDate = activeDietAssignment?.start_date.ToDateTime(TimeOnly.MinValue),
            WeightHistory = weightHistory
        });
    }
}