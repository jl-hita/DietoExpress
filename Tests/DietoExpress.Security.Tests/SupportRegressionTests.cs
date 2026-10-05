using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Regresiones arquitectónicas del soporte interno. El objetivo es evitar que una futura
/// refactorización elimine el aislamiento tenant o vuelva a exponer notas internas.
/// </summary>
public sealed class SupportRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void SupportSchema_DefinesTicketConversationAndIndexes()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "SupportSchemaBootstrap.cs"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS support_tickets", bootstrap);
        Assert.Contains("CREATE TABLE IF NOT EXISTS support_messages", bootstrap);
        Assert.Contains("tenant_id INTEGER NOT NULL REFERENCES tenants", bootstrap);
        Assert.Contains("created_by_user_id INTEGER NOT NULL REFERENCES users", bootstrap);
        Assert.Contains("assigned_to_user_id INTEGER REFERENCES users", bootstrap);
        Assert.Contains("is_internal BOOLEAN NOT NULL DEFAULT FALSE", bootstrap);
        Assert.Contains("idx_support_tickets_tenant_updated", bootstrap);
        Assert.Contains("idx_support_messages_ticket_created", bootstrap);
    }

    [Fact]
    public void SupportBootstrap_IsRegisteredDuringStartup()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("builder.Services.AddScoped<SupportService>();", program);
        Assert.Contains("SupportSchemaBootstrap.Initialize(context, logger);", program);
    }

    [Fact]
    public void SupportApi_IsRestrictedToProfessionalRoles()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "SupportController.cs"));

        Assert.Contains("[Authorize(Roles = \"nutritionist,clinic_admin,superadmin\")]", controller);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", controller);
        Assert.Contains("if (User.IsInRole(\"superadmin\")) return Forbid();", controller);
    }

    [Fact]
    public void SupportService_EnforcesOwnerTenantScopeAndHidesInternalNotes()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "SupportService.cs"));

        Assert.Contains("t.tenant_id=@tenant AND t.created_by_user_id=@user", service);
        Assert.Contains("@superadmin OR (t.tenant_id=@tenant AND t.created_by_user_id=@user)", service);
        Assert.Contains("@superadmin OR m.is_internal=FALSE", service);
        Assert.Contains("if (internalNote && !isSuperAdmin) return false;", service);
        Assert.Contains("u.role='superadmin' AND u.archived_at IS NULL", service);
    }

    [Fact]
    public void SupportService_UsesServerSideAuthorAndTransactionalTicketCreation()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "SupportService.cs"));

        Assert.Contains("created_by_user_id", service);
        Assert.Contains("support_messages(ticket_id, author_user_id, body, is_internal)", service);
        Assert.Contains("BeginTransactionAsync()", service);
        Assert.Contains("await transaction.CommitAsync()", service);
    }
}
