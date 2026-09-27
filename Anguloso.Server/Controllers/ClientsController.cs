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
[Authorize] // opcional: añade si usas auth
public class ClientsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EnergyCalculatorService _calculatorService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILicenseService _licenseService;

    public ClientsController(
        angulosodbContext context, 
        EnergyCalculatorService calculatorService,
        IAuditLogService auditLogService,
        ILicenseService licenseService)
    {
        _context = context;
        _calculatorService = calculatorService;
        _auditLogService = auditLogService;
        _licenseService = licenseService;
    }

    // GET: api/clients
    [HttpGet]
    public async Task<ActionResult<List<ClientListDto>>> GetClients()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        var isClinicAdmin = User.IsInRole("clinic_admin");

        var list = await _context.clients
            .Where(c => c.user_id == userId.Value || (isClinicAdmin && tenantId.HasValue && c.tenant_id == tenantId.Value))
            .OrderByDescending(c => c.created_at)
            .Select(c => new ClientListDto
            {
                Id = c.id,
                FullName = c.full_name,
                Email = c.email,
                Phone = c.phone,
                Gender = c.gender,
                BirthDate = c.birth_date.HasValue ? new DateTime?(c.birth_date.Value.ToDateTime(TimeOnly.MinValue)) : null,
                CreatedAt = c.created_at
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET: api/clients/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClientDetailDto>> GetClient(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        var isClinicAdmin = User.IsInRole("clinic_admin");

        var client = await _context.clients
            .Include(c => c.biometrics)
            .Include(c => c.medical_history)
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.lifestyle_history)
            .FirstOrDefaultAsync(c => c.id == id && (c.user_id == userId.Value || (isClinicAdmin && tenantId.HasValue && c.tenant_id == tenantId.Value)));

        if (client == null) return NotFound();

        // Trazabilidad de acceso a datos clínicos (Art. 32 RGPD y Ley 41/2002)
        await _auditLogService.LogAccessAsync(
            action: "READ_MEDICAL_CHART",
            entityName: "clients",
            entityId: id.ToString(),
            clientId: client.id,
            details: $"Acceso a la historia clínica y anamnesis del paciente {client.full_name}"
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

    // POST: api/clients
    [HttpPost]
    public async Task<ActionResult> CreateClient([FromBody] CreateClientDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var licenseCheck = await _licenseService.CanCreateClientAsync(tenantId, userId.Value);
        if (!licenseCheck.Allowed) return BadRequest(licenseCheck.Reason);

        var client = new clients
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

        await _auditLogService.LogAccessAsync(
            action: "CREATE_PATIENT",
            entityName: "clients",
            entityId: client.id.ToString(),
            clientId: client.id,
            details: $"Alta inicial de expediente clínico para el paciente {client.full_name}"
        );

        return CreatedAtAction(nameof(GetClient), new { id = client.id }, new { id = client.id });
    }

    // PUT: api/clients/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClient(int id, [FromBody] UpdateClientDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients
            .Include(c => c.medical_history)
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.lifestyle_history)
            .FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);

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

        var client = await _context.clients.Include(c => c.biometrics).FirstOrDefaultAsync(c => c.id == id && (c.user_id == userId.Value || (User.IsInRole("clinic_admin") && AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User).Value)));
        if (client == null) return NotFound();

        // Optionally: delete biometrics cascade if not configured
        _context.biometrics.RemoveRange(client.biometrics);
        _context.clients.Remove(client);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET: api/clients/{id}/energy-requirements
    [HttpGet("{id:int}/energy-requirements")]
    public async Task<ActionResult<EnergyRequirementsDto>> GetEnergyRequirements(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients
            .Include(c => c.biometrics)
            .FirstOrDefaultAsync(c => c.id == id && (c.user_id == userId.Value || (User.IsInRole("clinic_admin") && AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User).Value)));

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

        double weight = (double)latestBiometrics.weight.Value;
        double height = (double)latestBiometrics.height.Value;
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

        var client = await _context.clients
            .Include(c => c.biometrics.OrderByDescending(b => b.measurement_date))
            .Include(c => c.client_diets)
            .FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);

        if (client == null) return NotFound();

        var latestBio = client.biometrics
            .OrderByDescending(b => b.measurement_date)
            .FirstOrDefault();

        var activeDietAssignment = await _context.client_diets
            .Where(cd => cd.client_id == id && cd.is_active == true)
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