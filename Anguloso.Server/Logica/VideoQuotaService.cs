using System.Data;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Controla el consumo interno de videollamadas por nutricionista y a nivel global.
/// La cuota se reserva al primer acceso a una cita online usando minutos-participante
/// estimados (duración de la cita × 2 participantes). Esto permite proteger el cupo
/// incluso con el plan gratuito de LiveKit, sin depender de funciones Analytics de pago.
/// </summary>
public sealed class VideoQuotaService
{
    private const int DefaultNutritionistMonthlyMinutes = 800;
    private const int DefaultGlobalMonthlyMinutes = 4000;
    private const int DefaultWarningPercent = 80;
    private const int DefaultCriticalPercent = 90;

    private readonly angulosodbContext _context;
    private readonly ConfigServ _config;
    private readonly ILogger<VideoQuotaService> _logger;

    public VideoQuotaService(angulosodbContext context, ConfigServ config, ILogger<VideoQuotaService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    public async Task<VideoQuotaResult> ReserveForAppointmentAsync(
        int appointmentId,
        int nutritionistId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((endsAtUtc - startsAtUtc).TotalMinutes));
        var participantMinutes = checked(minutes * 2);
        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var nutritionistLimit = Math.Max(1, _config.GetConfigInt(
            "VIDEO_LIVEKIT_NUTRITIONIST_MONTHLY_PARTICIPANT_MINUTES",
            DefaultNutritionistMonthlyMinutes) ?? DefaultNutritionistMonthlyMinutes);
        var globalLimit = Math.Max(1, _config.GetConfigInt(
            "VIDEO_LIVEKIT_GLOBAL_MONTHLY_PARTICIPANT_MINUTES",
            DefaultGlobalMonthlyMinutes) ?? DefaultGlobalMonthlyMinutes);
        var warningPercent = Math.Clamp(_config.GetConfigInt(
            "VIDEO_LIVEKIT_QUOTA_WARNING_PERCENT",
            DefaultWarningPercent) ?? DefaultWarningPercent, 50, 99);
        var criticalPercent = Math.Clamp(_config.GetConfigInt(
            "VIDEO_LIVEKIT_QUOTA_CRITICAL_PERCENT",
            DefaultCriticalPercent) ?? DefaultCriticalPercent, warningPercent + 1, 100);

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Serializamos las reservas por nutricionista para que dos pestañas no puedan
        // superar simultáneamente la cuota antes de que PostgreSQL confirme ambas.
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(917234, 0); SELECT pg_advisory_xact_lock(917234, {0});",
            new object[] { nutritionistId });

        var existingReservation = await _context.Database
            .SqlQueryRaw<ExistingVideoReservation>(
                @"SELECT appointment_id AS ""AppointmentId"", participant_minutes AS ""ParticipantMinutes""
                  FROM video_usage_reservations
                  WHERE appointment_id = {0}
                  LIMIT 1;",
                appointmentId)
            .SingleOrDefaultAsync(cancellationToken);

        var nutritionistUsed = await _context.Database
            .SqlQueryRaw<int>(
                @"SELECT COALESCE(SUM(participant_minutes), 0)::int AS ""Value""
                  FROM video_usage_reservations
                  WHERE nutritionist_id = {0} AND period_start = {1};",
                nutritionistId, periodStart.Date)
            .SingleAsync(cancellationToken);

        var globalUsed = await _context.Database
            .SqlQueryRaw<int>(
                @"SELECT COALESCE(SUM(participant_minutes), 0)::int AS ""Value""
                  FROM video_usage_reservations
                  WHERE period_start = {0};",
                periodStart.Date)
            .SingleAsync(cancellationToken);

        if (existingReservation != null)
        {
            await transaction.CommitAsync(cancellationToken);
            return BuildResult(
                true,
                existingReservation.ParticipantMinutes,
                nutritionistUsed,
                nutritionistLimit,
                globalUsed,
                globalLimit,
                warningPercent,
                criticalPercent);
        }

        var nextNutritionist = checked(nutritionistUsed + participantMinutes);
        var nextGlobal = checked(globalUsed + participantMinutes);

        if (nextNutritionist > nutritionistLimit || nextGlobal > globalLimit)
        {
            var scope = nextNutritionist > nutritionistLimit ? "el nutricionista" : "la plataforma";
            var title = nextNutritionist > nutritionistLimit
                ? $"Cuota de videollamadas agotada para nutricionista {nutritionistId}"
                : "Cuota global de videollamadas agotada";

            await transaction.RollbackAsync(cancellationToken);

            // La alerta se registra después del rollback para que no quede anulada por la
            // misma transacción que rechaza la reserva.
            await ApplicationAlertService.RecordAsync(
                _context,
                "error",
                "video-quota",
                title,
                $"Se ha bloqueado una nueva consulta online porque {scope} superaría la cuota mensual configurada.",
                logger: _logger);

            return BuildResult(
                false,
                participantMinutes,
                nutritionistUsed,
                nutritionistLimit,
                globalUsed,
                globalLimit,
                warningPercent,
                criticalPercent);
        }

        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO video_usage_reservations
                (appointment_id, nutritionist_id, period_start, participant_minutes, created_at)
            VALUES
                ({appointmentId}, {nutritionistId}, {periodStart.Date}, {participantMinutes}, NOW())
            ON CONFLICT (appointment_id) DO NOTHING;", cancellationToken);

        nutritionistUsed = nextNutritionist;
        globalUsed = nextGlobal;

        if (ReachedThreshold(nutritionistUsed, nutritionistLimit, warningPercent))
        {
            await ApplicationAlertService.RecordAsync(
                _context,
                nutritionistUsed >= nutritionistLimit * criticalPercent / 100 ? "error" : "warning",
                "video-quota",
                $"Consumo de videollamadas elevado para nutricionista {nutritionistId}",
                $"El nutricionista {nutritionistId} ha alcanzado el {Percent(nutritionistUsed, nutritionistLimit)} % de su cuota mensual de videollamadas.");
        }

        if (ReachedThreshold(globalUsed, globalLimit, warningPercent))
        {
            await ApplicationAlertService.RecordAsync(
                _context,
                globalUsed >= globalLimit * criticalPercent / 100 ? "error" : "warning",
                "video-quota",
                "Consumo global de videollamadas elevado",
                $"La plataforma ha alcanzado el {Percent(globalUsed, globalLimit)} % de la cuota global mensual de videollamadas.");
        }

        await transaction.CommitAsync(cancellationToken);

        return BuildResult(
            true,
            participantMinutes,
            nutritionistUsed,
            nutritionistLimit,
            globalUsed,
            globalLimit,
            warningPercent,
            criticalPercent);
    }

    private static bool ReachedThreshold(int used, int limit, int threshold) =>
        used * 100 >= limit * threshold;

    private static int Percent(int used, int limit) =>
        limit <= 0 ? 100 : Math.Min(100, (int)Math.Floor(used * 100d / limit));

    private static VideoQuotaResult BuildResult(
        bool allowed,
        int reservedParticipantMinutes,
        int nutritionistUsed,
        int nutritionistLimit,
        int globalUsed,
        int globalLimit,
        int warningPercent,
        int criticalPercent)
    {
        var nutritionistPercent = Percent(nutritionistUsed, nutritionistLimit);
        var globalPercent = Percent(globalUsed, globalLimit);
        var percent = Math.Max(nutritionistPercent, globalPercent);

        return new VideoQuotaResult(
            allowed,
            reservedParticipantMinutes,
            nutritionistUsed,
            nutritionistLimit,
            globalUsed,
            globalLimit,
            percent >= criticalPercent,
            percent >= warningPercent);
    }

    private sealed class ExistingVideoReservation
    {
        public int AppointmentId { get; set; }
        public int ParticipantMinutes { get; set; }
    }
}

public sealed record VideoQuotaResult(
    bool Allowed,
    int ReservedParticipantMinutes,
    int NutritionistUsedParticipantMinutes,
    int NutritionistLimitParticipantMinutes,
    int GlobalUsedParticipantMinutes,
    int GlobalLimitParticipantMinutes,
    bool Critical,
    bool Warning);
