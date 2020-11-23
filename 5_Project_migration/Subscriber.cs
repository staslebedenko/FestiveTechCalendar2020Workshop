using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace KedaFunctionsDemo
{
    public static class Subscriber
    {
        [FunctionName("Subscriber")]
        public static async Task RunAsync(
        [RabbitMQTrigger("k8queue", ConnectionStringSetting = "RabbitMQConnection")] string queueName,
        ILogger log,
        CancellationToken cts,
        [Queue("k8queueresults", Connection = "AzureWebJobsStorage")] IAsyncCollector<string> messages)
        {
            log.LogInformation($"C# Queue trigger function processed: {queueName}");

            var prediction = $"Zoltar speaks! {queueName}, your rate will be '{RatePrediction()}'.";

            await messages.AddAsync($"Processed: {queueName}", cts);
        }

        private static int RatePrediction()
        {
            var random = new Random();
            return random.Next(20, 50);
        }
    }
}
