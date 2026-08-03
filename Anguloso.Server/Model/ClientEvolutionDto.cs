using System;
using System.Collections.Generic;

namespace Anguloso.Server.Model;

public class ClientEvolutionDto
{
    public List<string> Dates { get; set; } = new();
    public List<double?> Weight { get; set; } = new();
    public List<double?> BodyFat { get; set; } = new();
    public List<double?> MuscleMass { get; set; } = new();
    public List<double?> Bmi { get; set; } = new();
    public List<double?> Waist { get; set; } = new();
    public List<double?> Hip { get; set; } = new();
}
