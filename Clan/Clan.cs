using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NinjaHorizon.Function.Clan;

namespace NinjaHorizon.Function
{
    /// <summary>
    /// Azure Function for Clan system with action-based routing
    /// </summary>
    public static class ClanFunction
    {
        [FunctionName("Clan")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                var context = await PlayFabUtil.ParseFunctionContext(req);
                var playFabUtil = PlayFabUtil.InitializeFromContext(context);
                var clanService = new ClanService(playFabUtil);

                // Parse the action from the function argument
                var functionArg = context.FunctionArgument;
                string action = functionArg.action?.ToString() ?? functionArg.Action?.ToString();

                if (string.IsNullOrEmpty(action))
                {
                    return new BadRequestObjectResult(new { error = "Action is required" });
                }

                log.LogInformation($"Clan function processing action: {action}");

                // Route based on action
                return action.ToLower() switch
                {
                    "createclan" => await HandleCreateClan(clanService, functionArg, log),
                    "applytoclan" => await HandleApplyToClan(clanService, functionArg, log),
                    "inviteplayer" => await HandleInvitePlayer(clanService, functionArg, log),
                    "acceptapplication" => await HandleAcceptApplication(clanService, functionArg, log),
                    "rejectapplication" => await HandleRejectApplication(clanService, functionArg, log),
                    "getpendingapplications" => await HandleGetPendingApplications(clanService, functionArg, log),
                    "getclandetails" => await HandleGetClanDetails(clanService, functionArg, log),
                    "getclanleaderboard" => await HandleGetClanLeaderboard(clanService, functionArg, log),
                    "attackclan" => await HandleAttackClan(clanService, functionArg, log),
                    "upgradebuilding" => await HandleUpgradeBuilding(clanService, functionArg, log),
                    "contributereputation" => await HandleContributeReputation(clanService, functionArg, log),
                    "getmystamina" => await HandleGetMyStamina(clanService, playFabUtil, log),
                    "leaveclan" => await HandleLeaveClan(clanService, playFabUtil, log),
                    "searchclan" => await HandleSearchClan(clanService, functionArg, log),
                    _ => new BadRequestObjectResult(new { error = $"Unknown action: {action}" })
                };
            }
            catch (Exception ex)
            {
                log.LogError($"Error in Clan function: {ex.Message}");
                return new BadRequestObjectResult(new { error = ex.Message });
            }
        }

        private static async Task<dynamic> HandleCreateClan(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<CreateClanRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanName))
            {
                return new BadRequestObjectResult(new { error = "ClanName is required" });
            }

            var result = await service.CreateClan(request.ClanName);
            return result;
        }

        private static async Task<dynamic> HandleApplyToClan(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<ApplyToClanRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId))
            {
                return new BadRequestObjectResult(new { error = "ClanId is required" });
            }

            await service.ApplyToClan(request.ClanId);
            return new { success = true, message = "Application sent successfully" };
        }

        private static async Task<dynamic> HandleInvitePlayer(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<InviteToClanRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId) || string.IsNullOrWhiteSpace(request?.TargetPlayerId))
            {
                return new BadRequestObjectResult(new { error = "ClanId and TargetPlayerId are required" });
            }

            await service.InvitePlayerToClan(request.ClanId, request.TargetPlayerId);
            return new { success = true, message = "Invitation sent successfully" };
        }

        private static async Task<dynamic> HandleAcceptApplication(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<AcceptApplicationRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId) || string.IsNullOrWhiteSpace(request?.ApplicantEntityId))
            {
                return new BadRequestObjectResult(new { error = "ClanId and ApplicantEntityId are required" });
            }

            await service.AcceptApplication(request.ClanId, request.ApplicantEntityId);
            return new { success = true, message = "Application accepted" };
        }

        private static async Task<dynamic> HandleRejectApplication(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<RejectApplicationRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId) || string.IsNullOrWhiteSpace(request?.ApplicantEntityId))
            {
                return new BadRequestObjectResult(new { error = "ClanId and ApplicantEntityId are required" });
            }

            await service.RejectApplication(request.ClanId, request.ApplicantEntityId);
            return new { success = true, message = "Application rejected" };
        }

        private static async Task<dynamic> HandleGetPendingApplications(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<GetClanDetailsRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId))
            {
                return new BadRequestObjectResult(new { error = "ClanId is required" });
            }

            var applications = await service.GetPendingApplications(request.ClanId);
            return new { applications = applications };
        }

        private static async Task<dynamic> HandleGetClanDetails(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<GetClanDetailsRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId))
            {
                return new BadRequestObjectResult(new { error = "ClanId is required" });
            }

            var details = await service.GetClanDetails(request.ClanId);
            return details;
        }

        private static async Task<dynamic> HandleGetClanLeaderboard(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<GetClanLeaderboardRequest>(args.ToString());
            int count = request?.Count ?? 100;
            
            var leaderboard = await service.GetClanLeaderboard(count);
            return leaderboard;
        }

        private static async Task<dynamic> HandleAttackClan(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<AttackClanRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.TargetClanId))
            {
                return new BadRequestObjectResult(new { error = "TargetClanId is required" });
            }

            var result = await service.AttackClan(request.TargetClanId);
            return result;
        }

        private static async Task<dynamic> HandleUpgradeBuilding(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<UpgradeBuildingRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId))
            {
                return new BadRequestObjectResult(new { error = "ClanId is required" });
            }

            await service.UpgradeBuilding(request.ClanId, request.BuildingType);
            return new { success = true, message = $"{request.BuildingType} upgraded successfully" };
        }

        private static async Task<dynamic> HandleContributeReputation(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<ContributeReputationRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.ClanId))
            {
                return new BadRequestObjectResult(new { error = "ClanId is required" });
            }

            if (request.Amount <= 0)
            {
                return new BadRequestObjectResult(new { error = "Amount must be positive" });
            }

            await service.ContributeReputation(request.ClanId, request.Amount);
            return new { success = true, message = $"Contributed {request.Amount} reputation to clan" };
        }

        private static async Task<dynamic> HandleGetMyStamina(ClanService service, PlayFabUtil playFabUtil, ILogger log)
        {
            // Get player's current clan
            var memberships = await playFabUtil.ListEntityGroupMembership(playFabUtil.Entity);
            string clanId = memberships.Groups?.FirstOrDefault()?.Group.Id;

            // Get clan details if in a clan (for Bath House level)
            int bathHouseLevel = 1;
            if (!string.IsNullOrEmpty(clanId))
            {
                var clanDetails = await service.GetClanDetails(clanId);
                bathHouseLevel = clanDetails.Buildings.BathHouseLevel;
            }

            var stats = await service.GetPlayerStatisticsPublic(playFabUtil.Entity.Id);
            var staminaData = ClanStatistics.ParsePlayerStamina(stats);
            int currentStamina = ClanStatistics.CalculateCurrentStamina(
                staminaData.Stamina,
                staminaData.StaminaLastUpdate,
                staminaData.MaxStamina,
                bathHouseLevel);
            
            // Calculate effective max stamina to return
            int effectiveMaxStamina = ClanStatistics.CalculateEffectiveMaxStamina(
                staminaData.MaxStamina, bathHouseLevel);

            return new
            {
                currentStamina = currentStamina,
                maxStamina = effectiveMaxStamina, // Show effective max (includes building bonus)
                storedMaxStamina = staminaData.MaxStamina, // Show base for reference
                buildingBonus = effectiveMaxStamina - staminaData.MaxStamina,
                reputation = staminaData.Reputation,
                clanId = clanId
            };
        }

        private static async Task<dynamic> HandleLeaveClan(ClanService service, PlayFabUtil playFabUtil, ILogger log)
        {
            var memberships = await playFabUtil.ListEntityGroupMembership(playFabUtil.Entity);
            string clanId = memberships.Groups?.FirstOrDefault()?.Group.Id;

            if (string.IsNullOrEmpty(clanId))
            {
                return new BadRequestObjectResult(new { error = "You are not in a clan" });
            }

            await playFabUtil.RemoveEntityGroupMembers(clanId, new List<PlayFab.EconomyModels.EntityKey> { playFabUtil.Entity });
            return new { success = true, message = "Left clan successfully" };
            private static async Task<dynamic> HandleSearchClan(ClanService service, dynamic args, ILogger log)
        {
            var request = JsonConvert.DeserializeObject<SearchClanRequest>(args.ToString());
            if (string.IsNullOrWhiteSpace(request?.SearchQuery))
            {
                return new BadRequestObjectResult(new { error = "SearchQuery is required" });
            }

            // If query is 5 digits, try short code search
            if (request.SearchQuery.Length == 5 && int.TryParse(request.SearchQuery, out _))
            {
                try 
                {
                    return await service.SearchClanByCode(request.SearchQuery);
                }
                catch
                {
                    // Fallback or error if not found
                    return new BadRequestObjectResult(new { error = "Clan not found with that ID" });
                }
            }
            
            // Otherwise, could implement name search here or return error
            return new BadRequestObjectResult(new { error = "Invalid search format. Please enter a 5-digit Clan ID." });
        }
    }
    }
}
