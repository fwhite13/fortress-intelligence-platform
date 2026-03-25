using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class IntakeResponseService : IIntakeResponseService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public IntakeResponseService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertAsync(string opportunityId, string fieldCode, string value)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO intake_responses (opportunity_id, field_code, value, page_name, created_at, updated_at) " +
            "VALUES (@opp, @fc, @val, NULL, NOW(), NOW()) " +
            "ON DUPLICATE KEY UPDATE value=VALUES(value), updated_at=NOW()",
            new MySqlParameter("@opp", opportunityId),
            new MySqlParameter("@fc", fieldCode),
            new MySqlParameter("@val", value));
    }

    public async Task<Dictionary<string, string>> LoadAllAsync(string opportunityId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.IntakeResponses
            .Where(r => r.OpportunityId == opportunityId)
            .ToDictionaryAsync(r => r.FieldCode, r => r.Value ?? "");
    }
}
