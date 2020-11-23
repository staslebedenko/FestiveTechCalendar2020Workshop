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
        public static async System.Threading.Tasks.Task RunAsync([QueueTrigger("k8queue", Connection = "AzureWebJobsStorage")]string myQueueItem,
        ILogger log,
        CancellationToken cts,
        [Queue("k8queueresults", Connection = "AzureWebJobsStorage")] IAsyncCollector<string> messages)
        {
            log.LogInformation($"C# Queue trigger function processed: {myQueueItem}");

            await messages.AddAsync($"Processed: {myQueueItem}", cts);
        }
    }
}
