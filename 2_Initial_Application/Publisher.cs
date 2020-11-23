using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace KedaFunctionsDemo
{
    public static class Publisher
    {
        [FunctionName("Publisher")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            CancellationToken cts,
            ILogger log,
            [Queue("k8queue", Connection = "AzureWebJobsStorage")] IAsyncCollector<string> messages)
        {
            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            if(string.IsNullOrEmpty(name)){
                return new BadRequestObjectResult("Pass a name in the query string or in the request body to proceed.");
            }

            await messages.AddAsync(name, cts);

            return new OkObjectResult($"Hello, {name}. This HTTP triggered function executed successfully.");
        }
    }
}
