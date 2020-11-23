# FestiveTechCalendar2020Workshop (level 200) 
Welcome to the self-paced workshop on how to lift an Azure Functions application to on-premises via Kubernetes, KEDA, and RabbitMQ.

## Steps
The workshop is build around the 5 steps.

1. Install local components - azure cli, kubectl, helm, etc.
2. Create sample Azure Functions application via Functions CLI CLI or Visual studio.
3. Deploy infrastructure in Azure via included infrastructure script.
4. Generate Kubernetes manifest and deploy application to the cloud.
5. Migrate current project to RabbitMQ and deploy container with SQL Server.
6. Final steps, database, testing and problems.

## Prerequisites

Good mood :).
Visual Studio Code or VS 2019.
[NET Core SDK 3.1 (https://dotnet.microsoft.com/download).
[Postman](https://www.getpostman.com/).
Azure subscription .

## Step 1. Install local components - azure cli, kubectl, helm, etc.

Lets install:
1. Azure Functions Core Tools https://docs.docker.com/docker-for-windows/install/
2. Azure CLI https://docs.docker.com/docker-for-windows/install/
3. Docker https://docs.docker.com/docker-for-windows/install/
4. Kubectl https://kubernetes.io/docs/tasks/tools/install-kubectl/#install-kubectl-on-windows
5. Helm https://github.com/helm/helm/releases 
The fastest/easiest way is to install choco package manager https://chocolatey.org/install
and run command choco install kubernetes-helm

## Step 2. Create sample Azure Functions application via Functions CLI CLI or Visual studio.

Lets begin with project setup and create two functions. The first one will have and HTTP trigger with name "Publisher" and second one Azure Storage Queue trigger with name Subscriber. We will add output trigger to another storage queue later, to avoid initial setup of Azure SQL server.

Run the following command via command prompt CMD

```bash
    func init KedaFunctionsDemo — worker-runtime dotnet — docker
    cd KedaFunctionsDemo 
    func new — name Publisher — template “HTTP trigger” 
    func new — name Subscriber — template “Queue Trigger”
```

Alternatively there is an option to create a new project in Visual Studio and select Azure Functions.

The next activity connected with Docker, we need to:
* Create a docker container and test the application.
* Deploy container to the private container registry (ACR).
* Deploy container to Azure Kubernetes cluster (AKS).

```bash
    func init KedaFunctionsDemo — worker-runtime dotnet — docker
    cd KedaFunctionsDemo 
    func new — name Publisher — template “HTTP trigger” 
    func new — name Subscriber — template “Queue Trigger”
```

Now we can run this solution with command func start and run test curl command

```bash
    func start --build --verbose
    curl --get http://localhost:7071/api/Publisher?name=FestiveCalendarParticipant
```

## Step 3. Deploy infrastructure in Azure via included infrastructure script.

For this demo solution we need to scaffold infrastructure in Azure to have initial working version.
I`m a huge fan of Azure CLI for brevity and lightweight

```bash
      postfix=$RANDOM
      location=northeurope
      groupName=k82-calendar$postfix
      clusterName=k82-calendar$postfix
      registryName=k82Registry$postfix
      accountSku=Standard_LRS
      accountName=k82storage$postfix
      queueName=k8queue
      queueResultsName=k8queueresults

      az group create --name $groupName --location $location

      az storage account create --name $accountName --location $location --kind StorageV2 \
      --resource-group $groupName --sku $accountSku --access-tier Hot  --https-only true

      accountKey=$(az storage account keys list --resource-group $groupName --account-name $accountName --query "[0].value" | tr -d '"')

      accountConnString="DefaultEndpointsProtocol=https;AccountName=$accountName;AccountKey=$accountKey;EndpointSuffix=core.windows.net"

      az storage queue create --name $queueName --account-key $accountKey \
      --account-name $accountName --connection-string $accountConnString

      az storage queue create --name $queueResultsName --account-key $accountKey \
      --account-name $accountName --connection-string $accountConnString

      az acr create --resource-group $groupName --name $registryName --sku Standard
      az acr identity assign --identities [system] --name $registryName

      az aks create --resource-group $groupName --name $clusterName --node-count 3 --generate-ssh-keys --network-plugin azure
      az aks update --resource-group $groupName --name $clusterName --attach-acr $registryName

      echo "Update local.settings.json Values=>AzureWebJobsStorage value with:  " $accountConnString

```

We need to bavigate to KedaFunctionsDemo folder and update AzureWebJobsStorage value with storage account connection string. Add output triggers and most importantly change authorization level on Producer function from AuthorizationLevel.Function to AuthorizationLevel.Anonymous.

```csharp
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
```

Now lets build and run docker container locally, but first we need to set a container name like ACR one from CLI script - k82Registry. Be aware that account connection string is needed for container start.

```bash
    docker build -t k82Registry.azurecr.io/kedafunctionsdemo .
    docker run -p -e docker run -p 9090:80 -e AzureWebJobsStorage={storage string without quotes}  k82egistry.azurecr.io/kedafunctionsdemo:v1
```


## Step 4. Generate Kubernetes manifest and deploy application to the cloud.

Now let`s start a CMD and call az login command

```bash
      az login
      az account set --subscription {your-subscription-guid}
      az account show

      az acr login --name k82registry
      az acr login --name k82registry --expose-token

      az acr repository list --name k82registry --output table

      az aks get-credentials --resource-group k82-cluster --name k82-cluster --overwrite-existing

      docker images
      docker push k82registry.azurecr.io/kedafunctionsdemo:latest
      az acr repository list --name k82Registry --output table
```


## Step 5. Migrate current project to RabbitMQ and deploy container with SQL Server.

```csharp
      using System.Threading;
      using Microsoft.AspNetCore.Mvc;
      using Microsoft.Azure.WebJobs;
      using Microsoft.Azure.WebJobs.Extensions.Http;
      using Microsoft.AspNetCore.Http;
      using Microsoft.Extensions.Logging;
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
                  string name = req.Query["name"];

                  if (string.IsNullOrEmpty(name))
                  {
                      message = null;
                      return new BadRequestObjectResult("Pass a name in the query string or in the request body to proceed.");
                  }

                  message = name;

                  return new OkObjectResult($"Hello, {name}. This HTTP triggered function executed successfully.");
              }
          }
      }
```


## Step 6. Final steps, database, testing and problems.

Now we need to choose a storage for our data.
