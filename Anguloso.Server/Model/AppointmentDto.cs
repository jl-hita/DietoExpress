namespace Anguloso.Server.Model;

public class AppointmentSlotDto
{
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int NutritionistId { get; set; }
    public string? NutritionistName { get; set; }
}

public class CreateAppointmentRequestDto
{
    public DateTime StartsAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string? PatientNotes { get; set; }
    public string Modality { get; set; } = "in_person";
    public string Modality { get; set; } = "in_person";
    public string? VideoProvider { get; set; }
    public string? VideoRoomUrl { get; set; }
    public DateTime? VideoExpiresAt { get; set; }
}

public class PublicAppointmentConfirmationDto
{
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string NutritionistName { get; set; } = string.Empty;
}

/// <summary>
/// Datos mínimos necesarios para solicitar una cita desde el directorio público.
/// El tenant y el profesional se resuelven siempre a partir del slug publicado.
/// </summary>
public class PublicAppointmentRequestDto
{
    public DateTime StartsAt { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? PatientNotes { get; set; }
    public int DurationMinutes { get; set; } = 30;
}

public class AppointmentDto
{
    public int Id { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PatientNotes { get; set; }
    public string? ProfessionalNotes { get; set; }
    public int ClientId { get; set; }
    public string? ClientName { get; set; }
    public int NutritionistId { get; set; }
    public string? NutritionistName { get; set; }
}

public class AvailabilityDto
{
    public int Id { get; set; }
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; }
    public bool IsActive { get; set; }
}

public class UpdateAvailabilityRequestDto : SaveAvailabilityRequestDto
{
}

public class SaveAvailabilityRequestDto
{
    public int DayOfWeek { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int SlotMinutes { get; set; } = 30;
    public bool IsActive { get; set; } = true;
}

public class UpdateAppointmentStatusRequestDto
{
    public string? Status { get; set; }
    public string? ProfessionalNotes { get; set; }
}
