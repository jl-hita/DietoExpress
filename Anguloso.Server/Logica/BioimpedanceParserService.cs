using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Anguloso.Server.Model;

namespace Anguloso.Server.Logica;

public class BioimpedanceParserService
{
    public BioimpedancePreviewResponseDto Parse(Stream stream, string? forcedDevice = null)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = reader.ReadToEnd();
        return ParseText(content, forcedDevice);
    }

    public BioimpedancePreviewResponseDto ParseText(string text, string? forcedDevice = null)
    {
        var result = new BioimpedancePreviewResponseDto();

        if (string.IsNullOrWhiteSpace(text))
        {
            result.Warnings.Add("El archivo está vacío.");
            return result;
        }

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

        if (lines.Count == 0)
        {
            result.Warnings.Add("No se encontraron líneas de datos válidas en el archivo.");
            return result;
        }

        // Determinar delimitador (coma, punto y coma o tabulación)
        char delimiter = DetectDelimiter(lines);

        // Detectar si es InBody, Tanita o Genérico
        string detectedDevice = !string.IsNullOrWhiteSpace(forcedDevice) && forcedDevice != "auto"
            ? forcedDevice
            : DetectDevice(lines);

        result.DetectedBrand = detectedDevice;

        // Parsear según dispositivo o genérico
        var rows = ParseLines(lines, delimiter, detectedDevice, result.Warnings);
        result.Rows = rows;
        result.TotalRowsFound = rows.Count;

        if (result.TotalRowsFound == 0 && result.Warnings.Count == 0)
        {
            result.Warnings.Add("No se pudieron extraer filas con mediciones numéricas claras (peso/grasa).");
        }

        return result;
    }

    private char DetectDelimiter(List<string> lines)
    {
        var sample = string.Join("\n", lines.Take(5));
        int semicolons = sample.Count(c => c == ';');
        int commas = sample.Count(c => c == ',');
        int tabs = sample.Count(c => c == '\t');

        if (tabs > semicolons && tabs > commas) return '\t';
        if (semicolons >= commas) return ';';
        return ',';
    }

    private string DetectDevice(List<string> lines)
    {
        var joined = string.Join(" ", lines.Take(15)).ToLowerInvariant();

        if (joined.Contains("inbody") || joined.Contains("smm") || joined.Contains("pbf") || joined.Contains("vfl") || joined.Contains("lookin'body"))
        {
            return "InBody";
        }

        if (joined.Contains("tanita") || joined.Contains("gmon") || joined.Contains("healthware") || joined.Contains("fat(%)") || joined.Contains("tbw"))
        {
            return "Tanita";
        }

        return "Genérico";
    }

    private List<ImportedBiometricRowDto> ParseLines(List<string> lines, char delimiter, string device, List<string> warnings)
    {
        var rows = new List<ImportedBiometricRowDto>();

        // Localizar fila de encabezados
        int headerIndex = -1;
        string[]? headers = null;

        for (int i = 0; i < Math.Min(lines.Count, 15); i++)
        {
            var parts = SplitCsvLine(lines[i], delimiter);
            var lowerParts = parts.Select(p => p.Trim().ToLowerInvariant()).ToArray();

            // Buscar indicios de columnas biométricas
            if (lowerParts.Any(p => p.Contains("peso") || p.Contains("weight") || p.Contains("grasa") || p.Contains("fat") || p.Contains("date") || p.Contains("fecha")))
            {
                headerIndex = i;
                headers = parts.Select(p => p.Trim()).ToArray();
                break;
            }
        }

        if (headerIndex == -1 || headers == null)
        {
            // Intentar parser transaccional llave-valor (por ejemplo reporte vertical InBody / Tanita)
            var kvRow = TryParseKeyValueFormat(lines, device);
            if (kvRow != null)
            {
                rows.Add(kvRow);
                return rows;
            }

            warnings.Add("No se pudo identificar una fila de encabezados estándar. Comprueba el formato del archivo.");
            return rows;
        }

        // Mapear índices de columnas
        var map = BuildColumnIndexMap(headers);

        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            var parts = SplitCsvLine(lines[i], delimiter);
            if (parts.Length <= 1 || parts.All(string.IsNullOrWhiteSpace)) continue;

            var row = new ImportedBiometricRowDto
            {
                SourceDevice = device
            };

            // Fecha
            if (map.DateIdx >= 0 && map.DateIdx < parts.Length)
            {
                row.MeasurementDate = ParseDateTime(parts[map.DateIdx]);
            }
            else
            {
                row.MeasurementDate = DateTime.Today;
            }

            // Peso
            if (map.WeightIdx >= 0 && map.WeightIdx < parts.Length)
                row.Weight = ParseDouble(parts[map.WeightIdx]);

            // Altura
            if (map.HeightIdx >= 0 && map.HeightIdx < parts.Length)
                row.Height = ParseDouble(parts[map.HeightIdx]);

            // Grasa corporal %
            if (map.FatIdx >= 0 && map.FatIdx < parts.Length)
                row.BodyFat = ParseDouble(parts[map.FatIdx]);

            // Masa muscular
            if (map.MuscleIdx >= 0 && map.MuscleIdx < parts.Length)
                row.MuscleMass = ParseDouble(parts[map.MuscleIdx]);

            // Grasa visceral
            if (map.VisceralIdx >= 0 && map.VisceralIdx < parts.Length)
                row.VisceralFat = ParseDouble(parts[map.VisceralIdx]);

            // Cintura
            if (map.WaistIdx >= 0 && map.WaistIdx < parts.Length)
                row.Waist = ParseDouble(parts[map.WaistIdx]);

            // Cadera
            if (map.HipIdx >= 0 && map.HipIdx < parts.Length)
                row.Hip = ParseDouble(parts[map.HipIdx]);

            // Solo añadir si contiene al menos peso o grasa
            if (row.Weight.HasValue || row.BodyFat.HasValue || row.MuscleMass.HasValue)
            {
                row.Notes = $"Importado automáticamente desde {device} ({DateTime.Now:dd/MM/yyyy})";
                rows.Add(row);
            }
        }

        return rows;
    }

    private ImportedBiometricRowDto? TryParseKeyValueFormat(List<string> lines, string device)
    {
        var row = new ImportedBiometricRowDto
        {
            MeasurementDate = DateTime.Today,
            SourceDevice = device
        };

        bool foundAny = false;

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^([^:=,;\t]+)[:=,;\t]+(.*)$");
            if (!match.Success) continue;

            string key = match.Groups[1].Value.Trim().ToLowerInvariant();
            string val = match.Groups[2].Value.Trim();

            if (key.Contains("date") || key.Contains("fecha"))
            {
                row.MeasurementDate = ParseDateTime(val);
            }
            else if (key.Contains("weight") || key.Contains("peso"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.Weight = num; foundAny = true; }
            }
            else if (key.Contains("height") || key.Contains("altura") || key.Contains("estatura"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.Height = num; foundAny = true; }
            }
            else if (key.Contains("fat") || key.Contains("grasa") || key.Contains("pbf"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.BodyFat = num; foundAny = true; }
            }
            else if (key.Contains("muscle") || key.Contains("muscular") || key.Contains("smm"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.MuscleMass = num; foundAny = true; }
            }
            else if (key.Contains("visceral") || key.Contains("vfl"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.VisceralFat = num; foundAny = true; }
            }
            else if (key.Contains("waist") || key.Contains("cintura"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.Waist = num; }
            }
            else if (key.Contains("hip") || key.Contains("cadera"))
            {
                var num = ParseDouble(val);
                if (num.HasValue) { row.Hip = num; }
            }
        }

        if (foundAny)
        {
            row.Notes = $"Importado desde reporte {device} ({DateTime.Now:dd/MM/yyyy})";
            return row;
        }

        return null;
    }

    private class ColumnMap
    {
        public int DateIdx = -1;
        public int WeightIdx = -1;
        public int HeightIdx = -1;
        public int FatIdx = -1;
        public int MuscleIdx = -1;
        public int VisceralIdx = -1;
        public int WaistIdx = -1;
        public int HipIdx = -1;
    }

    private ColumnMap BuildColumnIndexMap(string[] headers)
    {
        var map = new ColumnMap();

        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].ToLowerInvariant();

            // Fecha
            if (map.DateIdx == -1 && (h.Contains("date") || h.Contains("fecha") || h.Contains("test time") || h.Contains("hora")))
                map.DateIdx = i;

            // Peso
            else if (map.WeightIdx == -1 && (h.Contains("weight") || h.Contains("peso") || h == "wt" || h.StartsWith("peso (kg)")))
                map.WeightIdx = i;

            // Altura
            else if (map.HeightIdx == -1 && (h.Contains("height") || h.Contains("altura") || h.Contains("estatura") || h == "ht"))
                map.HeightIdx = i;

            // Grasa corporal %
            else if (map.FatIdx == -1 && (h.Contains("pbf") || h.Contains("% fat") || h.Contains("fat %") || h.Contains("fat(%)") || h.Contains("% grasa") || h.Contains("porcentaje de grasa") || h.Contains("body fat") || h == "fat"))
                map.FatIdx = i;

            // Masa muscular
            else if (map.MuscleIdx == -1 && (h.Contains("smm") || h.Contains("muscle mass") || h.Contains("masa muscular") || h.Contains("muscle(kg)") || h.Contains("muscle") || h.Contains("lbm")))
                map.MuscleIdx = i;

            // Grasa visceral
            else if (map.VisceralIdx == -1 && (h.Contains("vfl") || h.Contains("visceral") || h.Contains("grasa visceral") || h.Contains("visceral fat rating") || h.Contains("visceral fat level")))
                map.VisceralIdx = i;

            // Cintura
            else if (map.WaistIdx == -1 && (h.Contains("waist") || h.Contains("cintura")))
                map.WaistIdx = i;

            // Cadera
            else if (map.HipIdx == -1 && (h.Contains("hip") || h.Contains("cadera")))
                map.HipIdx = i;
        }

        return map;
    }

    private string[] SplitCsvLine(string line, char delimiter)
    {
        // Parser que respeta comillas dobles
        var pattern = string.Format("(?<=^|{0})(\"(?:[^\"]|\"\")*\"|[^{0}]*)", Regex.Escape(delimiter.ToString()));
        var matches = Regex.Matches(line, pattern);
        var list = new List<string>();

        foreach (Match m in matches)
        {
            string val = m.Value.Trim();
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
            {
                val = val.Substring(1, val.Length - 2).Replace("\"\"", "\"");
            }
            list.Add(val.Trim());
        }

        return list.ToArray();
    }

    private double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Extraer únicamente número y separador decimal
        var clean = Regex.Replace(value.Trim(), @"[^\d,\.\-]", "");
        if (string.IsNullOrWhiteSpace(clean)) return null;

        // Si contiene coma y punto (ej. 1.234,56 o 1,234.56)
        if (clean.Contains(',') && clean.Contains('.'))
        {
            if (clean.LastIndexOf(',') > clean.LastIndexOf('.'))
                clean = clean.Replace(".", "").Replace(',', '.');
            else
                clean = clean.Replace(",", "");
        }
        else if (clean.Contains(','))
        {
            clean = clean.Replace(',', '.');
        }

        if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
        {
            return Math.Round(result, 2);
        }

        return null;
    }

    private DateTime ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.Today;

        string clean = value.Trim();

        string[] formats = new[]
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "dd-MM-yyyy",
            "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm",
            "yyyyMMdd", "MM/dd/yyyy", "yyyy.MM.dd", "dd.MM.yyyy"
        };

        if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
        {
            return dtExact;
        }

        if (DateTime.TryParse(clean, out var dtGeneric))
        {
            return dtGeneric;
        }

        return DateTime.Today;
    }
}
