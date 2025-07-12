using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;

namespace NinjaHorizon.Function
{
    public class ClanMember
    {
        public string PlayerEntityKeyId { get; set; }
        public string ClanId { get; set; }
        public int Reputation { get; set; }
        public int Stamina { get; set; }
        public DateTime JoinedDate { get; set; }
        public DateTime LastStaminaRestore { get; set; }
    }

    public static class DatabaseUtil
    {
        private static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? throw new InvalidOperationException("SqlConnectionString environment variable is not set");
        }

        public static async Task<SqlConnection> GetConnectionAsync()
        {
            var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();
            return connection;
        }

        public static async Task<ClanMember> GetClanMemberAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<ClanMember>(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
        }

        public static async Task<List<ClanMember>> GetClanMembersAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore 
                FROM ClanMembers 
                WHERE ClanId = @ClanId 
                ORDER BY Reputation DESC";

            var result = await connection.QueryAsync<ClanMember>(sql, new { ClanId = clanId });
            return result.AsList();
        }

        public static async Task InsertClanMemberAsync(ClanMember clanMember)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                INSERT INTO ClanMembers (PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore)
                VALUES (@PlayerEntityKeyId, @ClanId, @Reputation, @Stamina, @JoinedDate, @LastStaminaRestore)";

            await connection.ExecuteAsync(sql, clanMember);
        }

        public static async Task UpdateClanMemberAsync(ClanMember clanMember)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                UPDATE ClanMembers 
                SET Reputation = @Reputation, Stamina = @Stamina, LastStaminaRestore = @LastStaminaRestore
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, clanMember);
        }

        public static async Task DeleteClanMemberAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                DELETE FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
        }

        public static async Task<int> GetClanTotalReputationAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT ISNULL(SUM(Reputation), 0) 
                FROM ClanMembers 
                WHERE ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<int>(sql, new { ClanId = clanId });
        }

        public static async Task<bool> IsPlayerInClanAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT COUNT(1) 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
            return count > 0;
        }



        public static async Task RestoreStaminaManuallyAsync(string playerEntityKeyId, string clanId, int staminaAmount)
        {
            using var connection = await GetConnectionAsync();

            var sql = $@"
                UPDATE ClanMembers 
                SET Stamina = CASE 
                    WHEN Stamina + @StaminaAmount > {ClanConfig.MAX_STAMINA_PER_PLAYER} THEN {ClanConfig.MAX_STAMINA_PER_PLAYER} 
                    ELSE Stamina + @StaminaAmount 
                END,
                LastStaminaRestore = GETUTCDATE()
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, new
            {
                PlayerEntityKeyId = playerEntityKeyId,
                ClanId = clanId,
                StaminaAmount = staminaAmount
            });
        }

        public static async Task<bool> RestoreStaminaIfNeededAsync(string playerEntityKeyId)
        {
            using var connection = await GetConnectionAsync();

            var sql = $@"
                UPDATE ClanMembers 
                SET Stamina = CASE 
                    WHEN DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= {ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES} AND Stamina < {ClanConfig.MAX_STAMINA_PER_PLAYER} THEN
                        CASE 
                            WHEN Stamina + (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) / {ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES}) * {ClanConfig.AUTO_RESTORE_STAMINA_AMOUNT} > {ClanConfig.MAX_STAMINA_PER_PLAYER} THEN {ClanConfig.MAX_STAMINA_PER_PLAYER}
                            ELSE Stamina + (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) / {ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES}) * {ClanConfig.AUTO_RESTORE_STAMINA_AMOUNT}
                        END
                    ELSE Stamina
                END,
                LastStaminaRestore = CASE 
                    WHEN DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= {ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES} AND Stamina < {ClanConfig.MAX_STAMINA_PER_PLAYER} THEN GETUTCDATE()
                    ELSE LastStaminaRestore
                END
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId
                AND (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= {ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES} AND Stamina < {ClanConfig.MAX_STAMINA_PER_PLAYER})";

            var rowsAffected = await connection.ExecuteAsync(sql, new { PlayerEntityKeyId = playerEntityKeyId });
            return rowsAffected > 0;
        }

        public static async Task<List<ClanMember>> GetPlayerClanMembershipsAsync(string playerEntityKeyId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId";

            var result = await connection.QueryAsync<ClanMember>(sql, new { PlayerEntityKeyId = playerEntityKeyId });
            return result.AsList();
        }

        public static async Task ReduceClanStaminaAsync(string clanId, int totalReduction)
        {
            using var connection = await GetConnectionAsync();

            // Get member count to calculate per-member reduction
            var memberCountSql = @"
                SELECT COUNT(*) 
                FROM ClanMembers 
                WHERE ClanId = @ClanId";

            var memberCount = await connection.QueryFirstOrDefaultAsync<int>(memberCountSql, new { ClanId = clanId });

            if (memberCount == 0) return;

            var reductionPerMember = Math.Max(1, totalReduction / memberCount);

            var sql = @"
                UPDATE ClanMembers 
                SET Stamina = CASE 
                    WHEN Stamina - @ReductionPerMember < 0 THEN 0 
                    ELSE Stamina - @ReductionPerMember 
                END
                WHERE ClanId = @ClanId";

            await connection.ExecuteAsync(sql, new
            {
                ClanId = clanId,
                ReductionPerMember = reductionPerMember
            });
        }
    }
}