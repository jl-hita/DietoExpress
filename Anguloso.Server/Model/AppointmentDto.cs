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
