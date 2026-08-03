using System;
using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

public class ClientDietListDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int DietId { get; set; }
    public string DietName { get; set; } = string.Empty;
    public DateTime? AssignedAt { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

public class AssignDietDto
{
    [Required]
    public int DietId { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public string? Notes { get; set; }
}

public class UpdateClientDietDto
{
    [Required]
    public DateTime StartDate { get; set; }
    
    public DateTime? EndDate { get; set; }
    
    public bool IsActive { get; set; }
    
    public string? Notes { get; set; }
}
