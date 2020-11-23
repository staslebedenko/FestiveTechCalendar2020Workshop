using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;

namespace KedaFunctionsDemo
{
    public static class Publisher
    {
        [FunctionName("Publisher")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Publisher")] HttpRequest req,
            CancellationToken cts,
            ILogger log,
            [RabbitMQ(ConnectionStringSetting = "RabbitMQConnection", QueueName = "k8queue")] out string message
            )
        {
            try
            {
                string name = req.Query["name"];

                if (string.IsNullOrEmpty(name))
                {
                    message = null;
                    return new BadRequestObjectResult("Pass a name in the query string or in the request body to proceed.");
                }

                message = name;

                return new OkObjectResult($"Hello, {name}. This HTTP triggered function executed successfully.");
            }
            catch (Exception ex)
            {
                message = null;
                return new BadRequestObjectResult($"Exception {ex}");
            }

        }
    }
}
