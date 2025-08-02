using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;
using PlayFab.ServerModels;
using EntityKey = PlayFab.EconomyModels.EntityKey;

namespace NinjaHorizon.Function
{
    public class TitleAuthenticationContext
    {
        public string Id { get; set; }
        public string EntityToken { get; set; }
    }

    public class FunctionExecutionContext<T>
    {
        public PlayFab.ProfilesModels.EntityProfileBody CallerEntityProfile { get; set; }
        public TitleAuthenticationContext TitleAuthenticationContext { get; set; }
        public bool? GeneratePlayStreamEvent { get; set; }
        public T FunctionArgument { get; set; }
    }

    public class PlayFabUtil
    {
        public PlayFabServerInstanceAPI ServerApi { get; private set; }
        public PlayFabEconomyInstanceAPI EconomyApi { get; private set; }
        public PlayFabMultiplayerInstanceAPI MultiplayerApi { get; private set; }
        public PlayFabProgressionInstanceAPI ProgressionApi { get; private set; }
        public PlayFabGroupsInstanceAPI GroupsApi { get; private set; }
        public PlayFabDataInstanceAPI DataApi { get; private set; }
        public EntityKey Entity { get; private set; }
        public string PlayFabId { get; private set; }

        private PlayFabUtil(
            PlayFabServerInstanceAPI serverApi,
            PlayFabEconomyInstanceAPI economyApi,
            PlayFabMultiplayerInstanceAPI multiplayerApi,
            PlayFabProgressionInstanceAPI progressionApi,
            PlayFabGroupsInstanceAPI groupsApi,
            PlayFabDataInstanceAPI dataApi,
            EntityKey entity,
            string playFabId
        )
        {
            ServerApi = serverApi;
            EconomyApi = economyApi;
            MultiplayerApi = multiplayerApi;
            ProgressionApi = progressionApi;
            GroupsApi = groupsApi;
            DataApi = dataApi;
            Entity = entity;
            PlayFabId = playFabId;
        }

        public static PlayFabUtil InitializeFromContext(FunctionExecutionContext<dynamic> context)
        {
            var apiSettings = new PlayFabApiSettings
            {
                TitleId = context.TitleAuthenticationContext.Id,
                DeveloperSecretKey = Environment.GetEnvironmentVariable("DeveloperSecretKey"),
            };

            var titleContext = new PlayFabAuthenticationContext
            {
                EntityToken = context.TitleAuthenticationContext.EntityToken
            };

            var entity = new EntityKey
            {
                Id = context.CallerEntityProfile.Entity.Id,
                Type = context.CallerEntityProfile.Entity.Type
            };

            return new PlayFabUtil(
                new PlayFabServerInstanceAPI(apiSettings, titleContext),
                new PlayFabEconomyInstanceAPI(apiSettings, titleContext),
                new PlayFabMultiplayerInstanceAPI(apiSettings, titleContext),
                new PlayFabProgressionInstanceAPI(apiSettings, titleContext),
                new PlayFabGroupsInstanceAPI(apiSettings, titleContext),
                new PlayFabDataInstanceAPI(apiSettings, titleContext),
                entity,
                context.CallerEntityProfile.Lineage.MasterPlayerAccountId
            );
        }

        public static async Task<FunctionExecutionContext<dynamic>> ParseFunctionContext(
            HttpRequest req
        )
        {
            string requestBody = await req.ReadAsStringAsync();
            var context = JsonConvert.DeserializeObject<FunctionExecutionContext<dynamic>>(
                requestBody
            );
            return context;
        }

        public async Task<GetInventoryItemsResponse> GetInventoryItems(string filter)
        {
            var request = new GetInventoryItemsRequest
            {
                Entity = Entity,
                CollectionId = "default",
                Filter = filter
            };

            var result = await EconomyApi.GetInventoryItemsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get inventory items: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<List<InventoryItem>> GetCurrencyItems(List<string> currencyIds)
        {
            var filter = string.Join(" or ", currencyIds.Select(id => $"id eq '{id}'"));
            var inventoryItems = await GetInventoryItems(filter);
            return inventoryItems.Items;
        }

        public async Task<InventoryItem> GetCurrencyItem(
            string currencyId,
            int? expectedAmount = null
        )
        {
            var inventoryItems = await GetCurrencyItems(new List<string> { currencyId });
            if (inventoryItems.Count != 1)
            {
                throw new Exception("User does not have the required item");
            }
            var inventoryItem = inventoryItems[0];
            if (expectedAmount != null && inventoryItem.Amount < expectedAmount)
            {
                throw new Exception("User does not have the required amount");
            }
            return inventoryItem;
        }

        public async Task ExecuteInventoryOperations(List<InventoryOperation> operations)
        {
            var request = new ExecuteInventoryOperationsRequest
            {
                Entity = Entity,
                Operations = operations,
                CollectionId = "default"
            };

            var result = await EconomyApi.ExecuteInventoryOperationsAsync(request);
            if (result.Error != null)
            {
                throw new Exception(
                    $"Failed to execute inventory operations: {result.Error.ErrorMessage}"
                );
            }
        }

        public async Task<SearchItemsResponse> SearchItems(string search = "", string filter = "", int count = 50, string continuationToken = null)
        {
            var request = new SearchItemsRequest
            {
                Entity = Entity,
                Search = search,
                Filter = filter,
                Count = count
            };

            if (!string.IsNullOrEmpty(continuationToken))
            {
                request.ContinuationToken = continuationToken;
            }

            var result = await EconomyApi.SearchItemsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to search items: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetTitleDataResult> GetTitleData(List<string> keys)
        {
            var request = new GetTitleDataRequest { Keys = keys };
            var result = await ServerApi.GetTitleDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get title data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetUserDataResult> GetUserData(List<string> keys)
        {
            var request = new GetUserDataRequest { PlayFabId = PlayFabId, Keys = keys };
            var result = await ServerApi.GetUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get user data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetPlayerCombinedInfoResult> GetPlayerCombinedInfo(
            List<string> titleDataKeys,
            List<string> userDataKeys,
            List<string> playerStatisticNames
        )
        {
            var infoRequestParameters = new GetPlayerCombinedInfoRequestParams();

            if (playerStatisticNames != null && playerStatisticNames.Count > 0)
            {
                infoRequestParameters.GetPlayerStatistics = true;
                infoRequestParameters.PlayerStatisticNames = playerStatisticNames;
            }

            if (titleDataKeys != null && titleDataKeys.Count > 0)
            {
                infoRequestParameters.GetTitleData = true;
                infoRequestParameters.TitleDataKeys = titleDataKeys;
            }

            if (userDataKeys != null && userDataKeys.Count > 0)
            {
                infoRequestParameters.GetUserData = true;
                infoRequestParameters.UserDataKeys = userDataKeys;
            }

            var request = new GetPlayerCombinedInfoRequest
            {
                PlayFabId = PlayFabId,
                InfoRequestParameters = infoRequestParameters
            };

            var result = await ServerApi.GetPlayerCombinedInfoAsync(request);
            return result.Result;
        }

        public async Task UpdateUserData(Dictionary<string, string> data)
        {
            var request = new UpdateUserDataRequest { PlayFabId = PlayFabId, Data = data };
            var result = await ServerApi.UpdateUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update user data: {result.Error.ErrorMessage}");
            }
        }

        public async Task UpdatePlayerStatistics(Dictionary<string, int> statistics)
        {
            var statisticUpdates = statistics.Select(stat => new StatisticUpdate
            {
                StatisticName = stat.Key,
                Value = stat.Value
            }).ToList();

            var request = new UpdatePlayerStatisticsRequest
            {
                PlayFabId = PlayFabId,
                Statistics = statisticUpdates
            };
            var result = await ServerApi.UpdatePlayerStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update player statistics: {result.Error.ErrorMessage}");
            }
        }

        public async Task CreateSharedGroup(string sharedGroupId)
        {
            var request = new CreateSharedGroupRequest
            {
                SharedGroupId = sharedGroupId
            };

            await ServerApi.CreateSharedGroupAsync(request);
        }

        public async Task<GetSharedGroupDataResult> GetSharedGroupData(string sharedGroupId)
        {
            var request = new GetSharedGroupDataRequest
            {
                SharedGroupId = sharedGroupId
            };

            var result = await ServerApi.GetSharedGroupDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get shared group data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<UpdateSharedGroupDataResult> UpdateSharedGroupData(string sharedGroupId, Dictionary<string, string> data)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = sharedGroupId,
                Data = data
            };

            var result = await ServerApi.UpdateSharedGroupDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update shared group data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.GroupsModels.CreateGroupResponse> CreateEntityGroup(string groupName, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.CreateGroupRequest
            {
                GroupName = groupName,
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.CreateGroupAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to create entity group: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.GroupsModels.GetGroupResponse> GetEntityGroup(string groupId)
        {
            var request = new PlayFab.GroupsModels.GetGroupRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId }
            };

            var result = await GroupsApi.GetGroupAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get entity group: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.DataModels.SetObjectsResponse> SetEntityGroupObjects(string groupId, Dictionary<string, object> objects)
        {
            var request = new PlayFab.DataModels.SetObjectsRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = groupId },
                Objects = objects.Select(kvp => new PlayFab.DataModels.SetObject
                {
                    ObjectName = kvp.Key,
                    DataObject = kvp.Value
                }).ToList()
            };

            var result = await DataApi.SetObjectsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to set entity group objects: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.DataModels.GetObjectsResponse> GetEntityGroupObjects(string groupId, List<string> objectNames = null)
        {
            var request = new PlayFab.DataModels.GetObjectsRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = groupId }
            };

            if (objectNames != null && objectNames.Count > 0)
            {
                request.EscapeObject = false;
            }

            var result = await DataApi.GetObjectsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get entity group objects: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.GroupsModels.InviteToGroupResponse> InviteToEntityGroup(string groupId, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.InviteToGroupRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.InviteToGroupAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to invite to entity group: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.GroupsModels.ApplyToGroupResponse> ApplyToEntityGroup(string groupId, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.ApplyToGroupRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.ApplyToGroupAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to apply to entity group: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task AcceptEntityGroupInvitation(string groupId, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.AcceptGroupInvitationRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.AcceptGroupInvitationAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to accept entity group invitation: {result.Error.ErrorMessage}");
            }
        }

        public async Task AcceptEntityGroupApplication(string groupId, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.AcceptGroupApplicationRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.AcceptGroupApplicationAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to accept entity group application: {result.Error.ErrorMessage}");
            }
        }

        public async Task<PlayFab.GroupsModels.ListMembershipResponse> ListEntityGroupMembership(EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.ListMembershipRequest
            {
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.ListMembershipAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to list entity group membership: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<PlayFab.GroupsModels.ListGroupMembersResponse> ListEntityGroupMembers(string groupId)
        {
            var request = new PlayFab.GroupsModels.ListGroupMembersRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId }
            };

            var result = await GroupsApi.ListGroupMembersAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to list entity group members: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task RemoveEntityGroupMembers(string groupId, List<EntityKey> members)
        {
            var groupsMembers = members.Select(m => new PlayFab.GroupsModels.EntityKey { Id = m.Id, Type = m.Type }).ToList();
            var request = new PlayFab.GroupsModels.RemoveMembersRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Members = groupsMembers
            };

            var result = await GroupsApi.RemoveMembersAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to remove entity group members: {result.Error.ErrorMessage}");
            }
        }

        public async Task<PlayFab.GroupsModels.ListGroupApplicationsResponse> ListEntityGroupApplications(string groupId)
        {
            var request = new PlayFab.GroupsModels.ListGroupApplicationsRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId }
            };

            var result = await GroupsApi.ListGroupApplicationsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to list entity group applications: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task RejectEntityGroupApplication(string groupId, EntityKey entity)
        {
            var request = new PlayFab.GroupsModels.RemoveGroupApplicationRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId },
                Entity = new PlayFab.GroupsModels.EntityKey { Id = entity.Id, Type = entity.Type }
            };

            var result = await GroupsApi.RemoveGroupApplicationAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to reject entity group application: {result.Error.ErrorMessage}");
            }
        }

        public async Task DeleteEntityGroup(string groupId)
        {
            var request = new PlayFab.GroupsModels.DeleteGroupRequest
            {
                Group = new PlayFab.GroupsModels.EntityKey { Id = groupId }
            };

            var result = await GroupsApi.DeleteGroupAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to delete entity group: {result.Error.ErrorMessage}");
            }
        }

        public static string GetStackIdFromType(string type)
        {
            if (type == "Entity" || type == "Weapon" || type == "BackItem" || type == "Clothing")
                return Guid.NewGuid().ToString();
            return null;
        }
    }
}
