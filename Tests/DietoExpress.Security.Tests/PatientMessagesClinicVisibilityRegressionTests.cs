using System.Security.Claims;
using Anguloso.Server.Controllers;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Ejecuta el endpoint contra SQLite real en memoria, incluyendo su consulta SQL,
/// para comprobar visibilidad de conversaciones y aislamiento entre tenants.
/// </summary>
public sealed class PatientMessagesClinicVisibilityRegressionTests
{
    [Fact]
    public async Task ClinicAdmin_SeesTenantConversationsButNeverCrossTenantOrInconsistentClientRows()
    {
        await using var fixture = await ConversationFixture.CreateAsync();
        await fixture.InsertAsync("""
            INSERT INTO users (id, tenant_id) VALUES (11, 10), (22, 20);
            INSERT INTO clients (id, tenant_id, full_name, archived_at)
                VALUES (101, 10, 'Paciente de la clínica', NULL),
                       (102, 20, 'Paciente de otro tenant', NULL);
            INSERT INTO patient_conversations (id, tenant_id, client_id, updated_at)
                VALUES (1001, 10, 101, '2026-10-10 10:00:00'),
                       (1002, 20, 102, '2026-10-10 11:00:00'),
                       (1003, 10, 102, '2026-10-10 12:00:00'),
                       (1004, 20, 101, '2026-10-10 13:00:00');
            """);

        var result = await fixture.CreateController(userId: 11, tenantId: 10, role: "clinic_admin").GetConversations();

        var response = Assert.IsType<OkObjectResult>(result);
        var conversations = Assert.IsAssignableFrom<IReadOnlyList<ConversationSummaryDto>>(response.Value);
        var onlyConversation = Assert.Single(conversations);
        Assert.Equal(1001, onlyConversation.ConversationId);
        Assert.Equal(101, onlyConversation.ClientId);
        Assert.Equal("Paciente de la clínica", onlyConversation.ClientName);
    }

    [Fact]
    public async Task Nutritionist_SeesOnlyActivelyAssignedClientsWithinOwnTenant()
    {
        await using var fixture = await ConversationFixture.CreateAsync();
        await fixture.InsertAsync("""
            INSERT INTO users (id, tenant_id) VALUES (11, 10), (22, 20);
            INSERT INTO clients (id, tenant_id, full_name, archived_at)
                VALUES (101, 10, 'Asignado activo', NULL),
                       (102, 10, 'Asignación inactiva', NULL),
                       (103, 10, 'Sin asignación', NULL),
                       (104, 20, 'Cliente de otro tenant', NULL);
            INSERT INTO patient_conversations (id, tenant_id, client_id, updated_at)
                VALUES (1001, 10, 101, '2026-10-10 10:00:00'),
                       (1002, 10, 102, '2026-10-10 11:00:00'),
                       (1003, 10, 103, '2026-10-10 12:00:00'),
                       (1004, 10, 104, '2026-10-10 13:00:00');
            INSERT INTO client_nutritionist_assignments (client_id, nutritionist_id, is_active)
                VALUES (101, 11, 1), (102, 11, 0), (104, 11, 1);
            """);

        var result = await fixture.CreateController(userId: 11, tenantId: 10, role: "nutritionist").GetConversations();

        var response = Assert.IsType<OkObjectResult>(result);
        var conversations = Assert.IsAssignableFrom<IReadOnlyList<ConversationSummaryDto>>(response.Value);
        var onlyConversation = Assert.Single(conversations);
        Assert.Equal(1001, onlyConversation.ConversationId);
        Assert.Equal(101, onlyConversation.ClientId);
    }

    private sealed class ConversationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly angulosodbContext _db;

        private ConversationFixture(SqliteConnection connection, angulosodbContext db)
        {
            _connection = connection;
            _db = db;
        }

        public static async Task<ConversationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<angulosodbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new angulosodbContext(options);
            var fixture = new ConversationFixture(connection, db);
            await fixture.InsertAsync("""
                CREATE TABLE users (id INTEGER PRIMARY KEY, tenant_id INTEGER NOT NULL);
                CREATE TABLE clients (
                    id INTEGER PRIMARY KEY, tenant_id INTEGER NOT NULL,
                    full_name TEXT NOT NULL, archived_at TEXT NULL);
                CREATE TABLE patient_conversations (
                    id INTEGER PRIMARY KEY, tenant_id INTEGER NOT NULL,
                    client_id INTEGER NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE patient_messages (
                    id INTEGER PRIMARY KEY, conversation_id INTEGER NOT NULL,
                    sender_client_id INTEGER NULL, read_at TEXT NULL,
                    body TEXT NOT NULL, created_at TEXT NOT NULL);
                CREATE TABLE client_nutritionist_assignments (
                    client_id INTEGER NOT NULL, nutritionist_id INTEGER NOT NULL,
                    is_active INTEGER NOT NULL);
                """);
            return fixture;
        }

        public Task<int> InsertAsync(string sql) =>
            _db.Database.ExecuteSqlRawAsync(sql);

        public PatientMessagesController CreateController(int userId, int tenantId, string role)
        {
            var controller = new PatientMessagesController(_db, null!, null!);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("tenantId", tenantId.ToString()),
                new Claim(ClaimTypes.Role, role)
            };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "IntegrationTest")),
                    RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
                }
            };
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
