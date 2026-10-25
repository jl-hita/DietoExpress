namespace Anguloso.Server.Middleware;

/// <summary>
/// Opts a write endpoint into the transactional Idempotency-Key contract.
/// Apply only after reviewing its database and external side effects.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute { }
