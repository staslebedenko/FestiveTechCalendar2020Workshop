# Festive Tech Calendar 2020 Workshop - Function/KEDA/RabbitMQ/SQL (level 200)  
https://festivetechcalendar.com/ 

Welcome to the self-paced workshop on how to lift an Azure Functions application to on-premises via Kubernetes, KEDA, and RabbitMQ.

## Steps
The workshop is build around six steps.

1. Install local components - azure cli, , chokubectl, helm, etc.
2. Create sample Azure Functions application via Functions CLI CLI or Visual studio.
3. Deploy infrastructure in Azure via included infrastructure script.
4. Update Functions triggers, Kubernetes manifest and deploy application to the cloud.
5. Deployment and configuration of RabbitMQ.
6. Final steps, testing and problems.

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

Azure Functions Core Tools ahd Azure CLI installation are straightforward, but there is a need to provide details on the last three.

Docker - if you have a latest Window 10 version, then you can might need the latest Windows Subsystem for Linux version from here.
https://docs.microsoft.com/en-us/windows/wsl/install-win10
Also kubectl will be added to the system, if you choose kubernetes option during the installation.

Kubectl - if it is not available after docker installaation, then there is a need to install it manually or via Powershell.
https://kubernetes.io/docs/tasks/tools/install-kubectl/#install-kubectl-on-windows

HELM - the simple way is to get archive from https://github.com/kubernetes/helm/releases . 
Extract helm.exe to a directory and it to the environment variable PATH.

<img src="img/envvariable.png" width="400">

But since I use choco package manager https://chocolatey.org/install, my choice was to install it as package via - choco install kubernetes-helm

In order to check installations, run following commands in CMD:

```bash
func
az
docker
kubectl
helm
```

## Step 2. Create sample Azure Functions application via Functions CLI CLI or Visual studio.

Lets begin with project setup and create two functions. The first one will have and HTTP trigger with name "Publisher" and second one Azure Storage Queue trigger with name Subscriber. We will add output trigger to another storage queue later, to avoid initial setup of Azure SQL server.

Run the following command via command prompt CMD

```bash
    func init KedaFunctions --worker-runtime dotnet --docker
    cd KedaFunctions 
    func new --name Publisher --template “HTTP trigger” 
    func new --name Subscriber --template “Queue Trigger”
```

Alternatively there is an option to create a new project in Visual Studio and select Azure Functions.

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
registryName=k82registry$postfix
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

In Publisher we adding output to Azure Storage Queue.

```csharp

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

namespace KedaFunctions
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
```

And in Subscriber we changing entire signature to code below, including switch from static void.

```csharp
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Host;
    using Microsoft.Extensions.Logging;

    namespace KedaFunctions
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

Now we can run this solution with command func start and run test curl command

```bash
func start --build --verbose
curl --get http://localhost:7071/api/Publisher?name=FestiveCalendarParticipant
```


## Step 4. Update Functions triggers, Kubernetes manifest and deploy application to the cloud.

During this step we will:
* Deploy container to the private container registry (ACR).
* Lift container to Azure Kubernetes cluster (AKS).


Let`s start a CMD and call az login command

```bash
az login
az account set --subscription {your-subscription-guid}
az account show

az acr login --name k82registry

az aks get-credentials --resource-group k82-calendar --name k82-calendar --overwrite-existing
```

Now lets build docker container locally, for that we need to use Azure Container Registry name from CLI script below - k82registry (get correct name from Cloud Shell output). 
And push it to Azure Container registry

```bash
docker build -t k82registry.azurecr.io/kedafunctions:v1 .
docker images
docker push k82registry.azurecr.io/kedafunctions:v1
az acr repository list --name k82Registry --output table
```

One thing is missing now - it`s KEDA installation.
There are several options to install KEDA, with function tools, HELM or kubectl, here is the link. To speed up things we will do this from project directory with func command.

```bash
func kubernetes install --namespace keda
```
Then we generating cluster manifest with --dry-run option, otherwise application will be deployed to cluster.

```bash
func kubernetes deploy --name k82-calendar --image-name "k82registry.azurecr.io/kedafunctions:v1" --dry-run > k8_keda_demo.yml
```

Its a good idea to double check generated k8_keda_demo.yml file and compare container image name with those are published in container registry. In my case version were different :v1 referenced in YAML file and different one in registry.

After changing YAML file with a correct container name, we need to deploy it.

```bash
kubectl apply -f k8_keda_demo.yml
kubectl get deployments -w
kubectl get pods
```

And finally, use cluster public IP to send a new message down the pipeline and observe results via Azure Storage account Queue k8queueresults
http://40.127.237.207/api/Publisher?name=Test

## Step 5. Deployment and configuration of RabbitMQ.

And now we will proceed with RabbitMQ configuration and deployment to Kubernetes.

Login to our cluster from CMD

```bash
az acr login --name k82registry
az aks get-credentials --resource-group k82-calendar --name k82-calendar --overwrite-existing
kubectl config use-context k82-calendar
kubectl get pods
```

Then proceed with RabbitMQ deployment and last string will return instance password in Base64 format.
You can decode it with online/local tool like https://emn178.github.io/online-tools/base64_decode.html

```bash
kubectl create namespace k8rabbit
helm repo add bitnami https://charts.bitnami.com/bitnami
helm search repo bitnami
helm repo update
helm install k8-rabbitmq bitnami/rabbitmq --set rabbitmq.username=user,rabbitmq.password=PAS3SWORD3 --namespace k8rabbit

kubectl get deployments,pods,services --namespace k8rabbit

kubectl get secret --namespace k8rabbit k8-rabbitmq -o jsonpath="{.data.rabbitmq-password}"

```

We can also open cluster configuration via Azure portal and observe results, now we have a RabbitMQ along with KEDA, but we should create a queue there, so we can put messages.

<img src="img/clusterazurerabbit.png" width="600">

For the local Kubernetes cluster, you should install and configure NGINX.
Proceed to the cluster configuration blade and open “my-rabbitmq” section.
And make the following changes to the YAML file, by changing the type ClusterIP to LoadBalancer - this will create a new public IP address along with new rules in the existing load balancer.

<img src="img/clusterazurerabbitbalancer.png" width="600">

This YAML update will generate new load balancing rules for the selected public IP address and RabbitMQ ports. If you want to disable or restrict access to your queues - just delete balancing rules or limit access to your IP address only via Network Security Group.

<img src="img/clusterazurerabbitbalanceren.png" width="600">

Queue configuration
http://20.67.129.70:15672

Log in to the management console via a new IP address, port 15762 and user and password created earlier and create transient queue via user interface.

<img src="img/queuecreate.png" width="600">

Now, let's form a connection string for RabbitMQ queue, we can obtain the internal and publish IP address from K8s Portal Services and Ingresses page. The public IP address can be used for development purposes from the local machine.

```bash
default format - amqp://user:password@url:port
internal format - amqp://user:password@10.240.0.71:5672
public format - amqp://user:password@20.67.129.70:5672
```
We will add the last connection string to our local.settings.json

And We will start with adding RabbitMQ extension to the KedaFunctionsDemo.csproj, VS Code will ask you to make package restore.

```csharp
<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.RabbitMQ" Version="0.2.2029-beta" />
```

Then proceed with an output trigger change for the Publisher function.
We need to change static async Task<IActionResult> to static IActionResult
    
```csharp
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;

namespace KedaFunctions
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
And adding the new input trigger for the Subscriber function, we will keep output to the storage queue as is, to observe that messages are running across our pipeline.

```csharp
[RabbitMQ(QueueName = "k8queue",  ConnectionStringSetting = "RabbitMQConnection")] string message
```
And full resulting code

```csharp
using System.Threading;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace KedaFunctions
{
    public static class Subscriber
    {
        [FunctionName("Subscriber")]
        public static async System.Threading.Tasks.Task RunAsync([RabbitMQTrigger("k8queue", ConnectionStringSetting = "RabbitMQConnection")] string myQueueItem,
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
Now we can run this solution with command func start and run test curl command to see how message will be processed by RabbitMQ.

```bash
func start --build --verbose
curl --get http://localhost:7071/api/Publisher?name=FestiveCalendarParticipant
```

Build a new container, publishing it and deploy a new manifest. Just in case you closed window where you made az login command, I will provide full script.

```bash
az login
az account set --subscription {your-subscription-guid}
az account show

az acr login --name k82registry
az acr login --name k82registry --expose-token

az acr repository list --name k82registry --output table

az aks get-credentials --resource-group k82-calendar --name k82-calendar --overwrite-existing

docker build -t k82registry.azurecr.io/kedafunctions:v2 .
docker images
docker push k82registry.azurecr.io/kedafunctions:v2
az acr repository list --name k82Registry --output table
```

Generate a new kubernetes manifest and deploy it
```bash
func kubernetes deploy --name k82-calendar --image-name "k82registry.azurecr.io/kedafunctions:v2" --dry-run > k8_keda_rabbit_demo.yml

kubectl apply -f k8_keda_rabbit_demo.yml
kubectl get deployments -w
kubectl get pods
```

## Step 6. Final steps, testing and problems.

Finally, your Publisher function available via the same url
http://40.127.237.207/api/Publisher?name=SomeoneFromRabbit 

And we can do a little load test via Artillery

```bash
npm install -g artillery
artillery quick --count 20 --num 100 http://20.54.101.197/api/Publisher?name=LoadTest
```

Summary
* Are functions can be run on Kubernetes? Yes.
* Are functions better in the Azure? Yes.
* Are functions app is cloud agnostic? Yes.
* Do you need two solutions for on-prem and cloud? No.



In order to run container locally you need to specify all secrets like azure storage token or RabbitMQ credentials.

```bash
docker build -t k82registry.azurecr.io/kedafunctions:v1 .
docker tag k82registry.azurecr.io/kedafunctions:v1 k82registry.azurecr.io/kedafunctions:v1

docker run -p 9090:80 -e AzureWebJobsStorage="DefaultEndpointsProtocol=https;AccountName=k82storage24853;AccountKey=sMHZw3245fdnUo/xrpylcpGRAKIKItYwbuaSm9apnO3crcnKdNP3sVM3PjaAm51XPjkj3i3aj7OMRt+Qg==;EndpointSuffix=core.windows.net" k82registry.azurecr.io/kedafunctions:v1

curl --get http://localhost:9090/api/Publisher?name=FestiveCalendarParticipant

FOR /f "tokens=*" %i IN ('docker ps -q') DO docker stop %i
```
