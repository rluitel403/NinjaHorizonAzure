using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;

namespace Inventory.Function
{
    public static class Gacha
    {
        [FunctionName("Gacha")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            FunctionExecutionContext<dynamic> context = JsonConvert.DeserializeObject<
                FunctionExecutionContext<dynamic>
            >(await req.ReadAsStringAsync());

            var apiSettings = new PlayFabApiSettings()
            {
                TitleId = context.TitleAuthenticationContext.Id,
                DeveloperSecretKey = Environment.GetEnvironmentVariable("DeveloperSecretKey"),
            };

            PlayFabAuthenticationContext titleContext = new PlayFabAuthenticationContext
            {
                EntityToken = context.TitleAuthenticationContext.EntityToken
            };
            var serverApi = new PlayFabServerInstanceAPI(apiSettings, titleContext);
            var economyApi = new PlayFabEconomyInstanceAPI(apiSettings, titleContext);
            var args = context.FunctionArgument;

            Dictionary<int, double> items = new Dictionary<int, double>()
                {
                    { 0, 30 },
                    { 1, 30 },
                    { 2, 20 },
                    { 3, 10 },
                    { 4, 30 },
                    { 5, 5 },
                    { 6, 10 },
                    { 7, 5 }
                };
            Random rand = new Random();
            double totalWeight = 0;
            foreach (double weight in items.Values)
            {
                totalWeight += weight;
            }
            double randomNumber = rand.NextDouble() * totalWeight;
            double cumulativeWeight = 0;
            int itemIndex = 0;
            foreach (KeyValuePair<int, double> kvp in items)
            {
                cumulativeWeight += kvp.Value;
                if (randomNumber <= cumulativeWeight)
                {
                    itemIndex = kvp.Key;
                    break;
                }
            }

            return itemIndex;
        }
    }
}
