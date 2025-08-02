using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace NinjaHorizon.Function
{
    // Base request class
    public class BaseClanRequest
    {
        public string Action { get; set; }
    }

    // Specific request classes for each action
    public class CreateClanRequest : BaseClanRequest
    {
        public string ClanName { get; set; }
        public string Description { get; set; }
    }

    public class GetClanInfoRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class JoinClanRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class LeaveClanRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class ApplyToClanRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class InvitePlayerRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
    }

    public class GetClanApplicationsRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class AcceptApplicationRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
    }

    public class RejectApplicationRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
    }

    public class GetClanMembersRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class UpdateReputationRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
        public int ReputationChange { get; set; }
    }

    public class UpdateStaminaRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
        public int StaminaChange { get; set; }
    }

    public class IsPlayerInClanRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class RestoreStaminaRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
        public int TokenCost { get; set; } = 20;
        public int StaminaRestore { get; set; } = 50;
    }

    public class AttackClanRequest : BaseClanRequest
    {
        public string AttackerClanId { get; set; }
        public string DefenderClanId { get; set; }
    }

    public class UpgradeBuildingRequest : BaseClanRequest
    {
        public string ClanId { get; set; }
        public string BuildingType { get; set; } // "teahouse", "bathhouse", "trainingcentre"
    }

    public class GetClanStatusRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class GetClanAttackHistoryRequest : BaseClanRequest
    {
        public string GroupId { get; set; }
    }

    public class PlayerClanResponse
    {
        public ClanMember Membership { get; set; }
        public ClanInfo Clan { get; set; }
    }

    public static class Clan
    {
        [FunctionName("Clan")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Clan function processed a request.");

            try
            {
                // Parse the function context
                var context = await PlayFabUtil.ParseFunctionContext(req);
                var playFabUtil = PlayFabUtil.InitializeFromContext(context);

                // Restore stamina for the current player if needed (based on time passed)
                await ClanHelper.RestorePlayerStaminaIfNeededAsync(playFabUtil);

                // First, deserialize to get the action
                var requestData = context.FunctionArgument;
                var baseRequest = JsonConvert.DeserializeObject<BaseClanRequest>(requestData.ToString());

                if (baseRequest == null || string.IsNullOrEmpty(baseRequest.Action))
                {
                    return new BadRequestObjectResult("Invalid request. Action is required.");
                }

                // Route to appropriate handler based on action
                switch (baseRequest.Action.ToLower())
                {
                    case "createclan":
                        var createRequest = JsonConvert.DeserializeObject<CreateClanRequest>(requestData.ToString());
                        return await CreateClan(playFabUtil, createRequest, log);

                    case "getallclans":
                        return await GetAllClans(playFabUtil, log);

                    case "getclaninfo":
                        var getClanInfoRequest = JsonConvert.DeserializeObject<GetClanInfoRequest>(requestData.ToString());
                        return await GetClanInfo(playFabUtil, getClanInfoRequest, log);

                    case "joinclan":
                        var joinRequest = JsonConvert.DeserializeObject<JoinClanRequest>(requestData.ToString());
                        return await JoinClan(playFabUtil, joinRequest, log);

                    case "leaveclan":
                        var leaveRequest = JsonConvert.DeserializeObject<LeaveClanRequest>(requestData.ToString());
                        return await LeaveClan(playFabUtil, leaveRequest, log);

                    case "applytoclan":
                        var applyRequest = JsonConvert.DeserializeObject<ApplyToClanRequest>(requestData.ToString());
                        return await ApplyToClan(playFabUtil, applyRequest, log);

                    case "inviteplayer":
                        var inviteRequest = JsonConvert.DeserializeObject<InvitePlayerRequest>(requestData.ToString());
                        return await InvitePlayer(playFabUtil, inviteRequest, log);

                    case "getclanapplications":
                        var getApplicationsRequest = JsonConvert.DeserializeObject<GetClanApplicationsRequest>(requestData.ToString());
                        return await GetClanApplications(playFabUtil, getApplicationsRequest, log);

                    case "acceptapplication":
                        var acceptRequest = JsonConvert.DeserializeObject<AcceptApplicationRequest>(requestData.ToString());
                        return await AcceptApplication(playFabUtil, acceptRequest, log);

                    case "rejectapplication":
                        var rejectRequest = JsonConvert.DeserializeObject<RejectApplicationRequest>(requestData.ToString());
                        return await RejectApplication(playFabUtil, rejectRequest, log);

                    case "getclanmembers":
                        var getMembersRequest = JsonConvert.DeserializeObject<GetClanMembersRequest>(requestData.ToString());
                        return await GetClanMembers(playFabUtil, getMembersRequest, log);

                    case "updatereputation":
                        var updateReputationRequest = JsonConvert.DeserializeObject<UpdateReputationRequest>(requestData.ToString());
                        return await UpdateReputation(playFabUtil, updateReputationRequest, log);

                    case "updatestamina":
                        var updateStaminaRequest = JsonConvert.DeserializeObject<UpdateStaminaRequest>(requestData.ToString());
                        return await UpdateStamina(playFabUtil, updateStaminaRequest, log);

                    case "isplayerinclan":
                        var isPlayerInClanRequest = JsonConvert.DeserializeObject<IsPlayerInClanRequest>(requestData.ToString());
                        return await IsPlayerInClan(playFabUtil, isPlayerInClanRequest, log);

                    case "restorestamina":
                        var restoreStaminaRequest = JsonConvert.DeserializeObject<RestoreStaminaRequest>(requestData.ToString());
                        return await RestoreStamina(playFabUtil, restoreStaminaRequest, log);

                    case "getplayerclan":
                        return await GetPlayerClan(playFabUtil, log);

                    case "attackclan":
                        var attackRequest = JsonConvert.DeserializeObject<AttackClanRequest>(requestData.ToString());
                        return await AttackClan(playFabUtil, attackRequest, log);

                    case "upgradebuilding":
                        var upgradeBuildingRequest = JsonConvert.DeserializeObject<UpgradeBuildingRequest>(requestData.ToString());
                        return await UpgradeBuilding(playFabUtil, upgradeBuildingRequest, log);

                    case "getclanstatus":
                        var getStatusRequest = JsonConvert.DeserializeObject<GetClanStatusRequest>(requestData.ToString());
                        return await GetClanStatus(playFabUtil, getStatusRequest, log);

                    case "getclanattackhistory":
                        var getAttackHistoryRequest = JsonConvert.DeserializeObject<GetClanAttackHistoryRequest>(requestData.ToString());
                        return await GetClanAttackHistory(playFabUtil, getAttackHistoryRequest, log);

                    default:
                        return new BadRequestObjectResult($"Unknown action: {baseRequest.Action}");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing clan request");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<dynamic> CreateClan(PlayFabUtil playFabUtil, CreateClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.ClanName))
            {
                throw new Exception("ClanName is required for creating a clan.");
            }

            try
            {
                var clanInfo = await ClanHelper.CreateClanAsync(playFabUtil, request.ClanName, request.Description ?? "");
                log.LogInformation($"Created clan: {clanInfo.GroupName} with ID: {clanInfo.GroupId}");
                return JsonConvert.SerializeObject(clanInfo);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error creating clan");
                throw new Exception($"Error creating clan: {ex.Message}");
            }
        }

        private static async Task<dynamic> GetAllClans(PlayFabUtil playFabUtil, ILogger log)
        {
            try
            {
                var clans = await ClanHelper.GetAllClansAsync(playFabUtil);
                log.LogInformation($"Retrieved {clans.Count} clans");
                return JsonConvert.SerializeObject(clans);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting all clans");
                throw new Exception($"Error getting all clans: {ex.Message}");
            }
        }

        private static async Task<dynamic> GetClanInfo(PlayFabUtil playFabUtil, GetClanInfoRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for getting clan info.");
            }

            try
            {
                var clanInfo = await ClanHelper.GetClanInfoAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Retrieved clan info for: {clanInfo.GroupName}");
                return JsonConvert.SerializeObject(clanInfo);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting clan info");
                throw new Exception($"Error getting clan info: {ex.Message}");
            }
        }

        private static async Task<dynamic> JoinClan(PlayFabUtil playFabUtil, JoinClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for joining a clan.");
            }

            try
            {
                await ClanHelper.JoinClanAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Player {playFabUtil.Entity.Id} joined clan {request.GroupId}");
                return new { message = "Successfully joined clan" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error joining clan");
                throw new Exception($"Error joining clan: {ex.Message}");
            }
        }

        private static async Task<dynamic> LeaveClan(PlayFabUtil playFabUtil, LeaveClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for leaving a clan.");
            }

            try
            {
                await ClanHelper.LeaveClanAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Player {playFabUtil.Entity.Id} left clan {request.GroupId}");
                return new { message = "Successfully left clan" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error leaving clan");
                throw new Exception($"Error leaving clan: {ex.Message}");
            }
        }

        private static async Task<dynamic> ApplyToClan(PlayFabUtil playFabUtil, ApplyToClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for applying to a clan.");
            }

            try
            {
                await ClanHelper.ApplyToClanAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Player {playFabUtil.Entity.Id} applied to clan {request.GroupId}");
                return new { message = "Successfully applied to clan" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error applying to clan");
                throw new Exception($"Error applying to clan: {ex.Message}");
            }
        }

        private static async Task<dynamic> InvitePlayer(PlayFabUtil playFabUtil, InvitePlayerRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                throw new Exception("GroupId and PlayerEntityKeyId are required for inviting a player.");
            }

            try
            {
                await ClanHelper.InvitePlayerAsync(playFabUtil, request.GroupId, request.PlayerEntityKeyId);
                log.LogInformation($"Invited player {request.PlayerEntityKeyId} to clan {request.GroupId}");
                return new { message = "Successfully invited player" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error inviting player");
                throw new Exception($"Error inviting player: {ex.Message}");
            }
        }

        private static async Task<dynamic> GetClanApplications(PlayFabUtil playFabUtil, GetClanApplicationsRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for getting clan applications.");
            }

            try
            {
                var applications = await ClanHelper.GetClanApplicationsAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Retrieved {applications.Count} applications for clan {request.GroupId}");
                return JsonConvert.SerializeObject(applications);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting clan applications");
                throw new Exception($"Error getting clan applications: {ex.Message}");
            }
        }

        private static async Task<dynamic> AcceptApplication(PlayFabUtil playFabUtil, AcceptApplicationRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                throw new Exception("GroupId and PlayerEntityKeyId are required for accepting an application.");
            }

            try
            {
                await ClanHelper.AcceptApplicationAsync(playFabUtil, request.GroupId, request.PlayerEntityKeyId);
                log.LogInformation($"Accepted application from player {request.PlayerEntityKeyId} to clan {request.GroupId}");
                return new { message = "Successfully accepted application" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error accepting application");
                throw new Exception($"Error accepting application: {ex.Message}");
            }
        }

        private static async Task<dynamic> RejectApplication(PlayFabUtil playFabUtil, RejectApplicationRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                throw new Exception("GroupId and PlayerEntityKeyId are required for rejecting an application.");
            }

            try
            {
                await ClanHelper.RejectApplicationAsync(playFabUtil, request.GroupId, request.PlayerEntityKeyId);
                log.LogInformation($"Rejected application from player {request.PlayerEntityKeyId} to clan {request.GroupId}");
                return new { message = "Successfully rejected application" };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error rejecting application");
                throw new Exception($"Error rejecting application: {ex.Message}");
            }
        }

        private static async Task<dynamic> GetClanMembers(PlayFabUtil playFabUtil, GetClanMembersRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                throw new Exception("GroupId is required for getting clan members.");
            }

            try
            {
                var members = await ClanHelper.GetClanMembersAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Retrieved {members.Count} members for clan {request.GroupId}");
                return JsonConvert.SerializeObject(members);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting clan members");
                throw new Exception($"Error getting clan members: {ex.Message}");
            }
        }

        private static async Task<IActionResult> UpdateReputation(PlayFabUtil playFabUtil, UpdateReputationRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                return new BadRequestObjectResult("GroupId and PlayerEntityKeyId are required for updating reputation.");
            }

            try
            {
                await ClanHelper.UpdateClanMemberReputationAsync(playFabUtil, request.GroupId, request.PlayerEntityKeyId, request.ReputationChange);
                log.LogInformation($"Updated reputation for player {request.PlayerEntityKeyId} in clan {request.GroupId} by {request.ReputationChange}");
                return new OkObjectResult(new { message = "Successfully updated reputation" });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error updating reputation");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<IActionResult> UpdateStamina(PlayFabUtil playFabUtil, UpdateStaminaRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                return new BadRequestObjectResult("GroupId and PlayerEntityKeyId are required for updating stamina.");
            }

            try
            {
                await ClanHelper.UpdateClanMemberStaminaAsync(playFabUtil, request.GroupId, request.PlayerEntityKeyId, request.StaminaChange);
                log.LogInformation($"Updated stamina for player {request.PlayerEntityKeyId} in clan {request.GroupId} by {request.StaminaChange}");
                return new OkObjectResult(new { message = "Successfully updated stamina" });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error updating stamina");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<IActionResult> IsPlayerInClan(PlayFabUtil playFabUtil, IsPlayerInClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                return new BadRequestObjectResult("GroupId is required for checking clan membership.");
            }

            try
            {
                var isMember = await ClanHelper.IsPlayerInClanAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Checked membership for player {playFabUtil.Entity.Id} in clan {request.GroupId}: {isMember}");
                return new OkObjectResult(new { isMember = isMember });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error checking clan membership");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<IActionResult> RestoreStamina(PlayFabUtil playFabUtil, RestoreStaminaRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrEmpty(request.PlayerEntityKeyId))
            {
                return new BadRequestObjectResult("GroupId and PlayerEntityKeyId are required for restoring stamina.");
            }

            try
            {
                await ClanHelper.RestoreStaminaManuallyAsync(
                    playFabUtil,
                    request.GroupId,
                    request.PlayerEntityKeyId,
                    request.TokenCost,
                    request.StaminaRestore
                );

                log.LogInformation($"Restored {request.StaminaRestore} stamina for player {request.PlayerEntityKeyId} in clan {request.GroupId} for {request.TokenCost} tokens");
                return new OkObjectResult(new
                {
                    message = "Successfully restored stamina",
                    staminaRestored = request.StaminaRestore,
                    tokensCost = request.TokenCost
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error restoring stamina");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<dynamic> GetPlayerClan(PlayFabUtil playFabUtil, ILogger log)
        {
            try
            {
                var membership = await ClanHelper.GetPlayerClanAsync(playFabUtil);

                if (membership == null)
                {
                    log.LogInformation($"Player {playFabUtil.Entity.Id} is not in any clan");
                    var emptyResponse = new PlayerClanResponse
                    {
                        Membership = null,
                        Clan = null
                    };
                    return JsonConvert.SerializeObject(emptyResponse);
                }

                // Get clan details if membership exists
                ClanInfo clanDetails = null;
                try
                {
                    clanDetails = await ClanHelper.GetClanInfoAsync(playFabUtil, membership.ClanId);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Could not get clan details for clan {membership.ClanId}");
                    // Continue without clan details
                }

                var response = new PlayerClanResponse
                {
                    Membership = membership,
                    Clan = clanDetails
                };

                log.LogInformation($"Retrieved clan membership for player {playFabUtil.Entity.Id} in clan {membership.ClanId}");
                return JsonConvert.SerializeObject(response);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting player clan memberships");
                throw new Exception($"Error getting player clan: {ex.Message}");
            }
        }

        private static async Task<dynamic> AttackClan(PlayFabUtil playFabUtil, AttackClanRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.AttackerClanId) || string.IsNullOrEmpty(request.DefenderClanId))
            {
                throw new Exception("AttackerClanId and DefenderClanId are required for attacking a clan.");
            }

            if (request.AttackerClanId == request.DefenderClanId)
            {
                throw new Exception("Cannot attack your own clan.");
            }

            try
            {
                var attackResult = await ClanHelper.AttackClanAsync(playFabUtil, request.AttackerClanId, request.DefenderClanId);
                log.LogInformation($"Clan attack: {request.AttackerClanId} -> {request.DefenderClanId}, Success: {attackResult.IsSuccessful}, Reputation: {attackResult.ReputationGained}");
                return JsonConvert.SerializeObject(attackResult);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error attacking clan");
                throw new Exception($"Error attacking clan: {ex.Message}");
            }
        }

        private static async Task<IActionResult> UpgradeBuilding(PlayFabUtil playFabUtil, UpgradeBuildingRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.ClanId) || string.IsNullOrEmpty(request.BuildingType))
            {
                return new BadRequestObjectResult("ClanId and BuildingType are required for upgrading a building.");
            }

            var validBuildingTypes = new[] { "teahouse", "bathhouse", "trainingcentre" };
            if (!validBuildingTypes.Any(bt => bt == request.BuildingType.ToLower()))
            {
                return new BadRequestObjectResult("BuildingType must be one of: teahouse, bathhouse, trainingcentre");
            }

            try
            {
                var upgraded = await ClanHelper.UpgradeBuildingAsync(playFabUtil, request.ClanId, request.BuildingType);
                if (upgraded)
                {
                    log.LogInformation($"Successfully upgraded {request.BuildingType} for clan {request.ClanId}");
                    return new OkObjectResult(new { message = $"Successfully upgraded {request.BuildingType}", success = true });
                }
                else
                {
                    log.LogWarning($"Failed to upgrade {request.BuildingType} for clan {request.ClanId} - already at max level or insufficient resources");
                    return new BadRequestObjectResult("Building is already at maximum level or insufficient resources");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error upgrading building");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<IActionResult> GetClanStatus(PlayFabUtil playFabUtil, GetClanStatusRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                return new BadRequestObjectResult("GroupId is required for getting clan status.");
            }

            try
            {
                var status = await ClanHelper.GetClanCurrentStatusAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Retrieved clan status for: {request.GroupId}");
                return new OkObjectResult(status);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting clan status");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<IActionResult> GetClanAttackHistory(PlayFabUtil playFabUtil, GetClanAttackHistoryRequest request, ILogger log)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                return new BadRequestObjectResult("GroupId is required for getting clan attack history.");
            }

            try
            {
                var attacks = await ClanHelper.GetClanRecentAttacksAsync(playFabUtil, request.GroupId);
                log.LogInformation($"Retrieved {attacks.Count} recent attacks for clan: {request.GroupId}");
                return new OkObjectResult(attacks);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting clan attack history");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }
    }
}
