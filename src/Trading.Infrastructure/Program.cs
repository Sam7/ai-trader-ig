using Pulumi;

using Gcp = Pulumi.Gcp;

return await Deployment.RunAsync(() =>
{
    var config = new Config();
    var gcpConfig = new Config("gcp");

    var project = gcpConfig.Require("project");
    var region = config.Get("region") ?? "us-central1";
    var zone = config.Get("zone") ?? "us-central1-a";
    var namePrefix = config.Get("namePrefix") ?? "ai-trader";

    var labels = new InputMap<string>
    {
        ["application"] = "ai-trader-ig",
        ["managed-by"] = "pulumi",
    };

    var workerServiceAccount = new Gcp.ServiceAccount.Account("worker-service-account", new()
    {
        AccountId = $"{namePrefix}-worker",
        DisplayName = "AI Trader worker VM",
        Project = project,
    });

    var backupBucket = new Gcp.Storage.Bucket("backup-bucket", new()
    {
        ForceDestroy = false,
        Labels = labels,
        Location = region.ToUpperInvariant(),
        Project = project,
        PublicAccessPrevention = "enforced",
        StorageClass = "STANDARD",
        UniformBucketLevelAccess = true,
    });

    _ = new Gcp.Storage.BucketIAMMember("worker-backup-bucket-object-admin", new()
    {
        Bucket = backupBucket.Name,
        Member = Output.Format($"serviceAccount:{workerServiceAccount.Email}"),
        Role = "roles/storage.objectAdmin",
    });

    var workerInstance = new Gcp.Compute.Instance("worker-instance", new()
    {
        BootDisk = new Gcp.Compute.Inputs.InstanceBootDiskArgs
        {
            AutoDelete = true,
            InitializeParams = new Gcp.Compute.Inputs.InstanceBootDiskInitializeParamsArgs
            {
                Image = "ubuntu-os-cloud/ubuntu-2404-lts-amd64",
                Labels = labels,
                Size = 30,
                Type = "pd-standard",
            },
        },
        Labels = labels,
        MachineType = "e2-micro",
        Name = $"{namePrefix}-worker",
        NetworkInterfaces =
        {
            new Gcp.Compute.Inputs.InstanceNetworkInterfaceArgs
            {
                AccessConfigs =
                {
                    new Gcp.Compute.Inputs.InstanceNetworkInterfaceAccessConfigArgs(),
                },
                Network = "default",
            },
        },
        Project = project,
        ServiceAccount = new Gcp.Compute.Inputs.InstanceServiceAccountArgs
        {
            Email = workerServiceAccount.Email,
            Scopes =
            {
                "https://www.googleapis.com/auth/devstorage.read_write",
            },
        },
        Zone = zone,
    });

    return new Dictionary<string, object?>
    {
        ["backupBucketName"] = backupBucket.Name,
        ["workerExternalIp"] = workerInstance.NetworkInterfaces.Apply(
            interfaces => interfaces[0].AccessConfigs[0].NatIp),
        ["workerInstanceName"] = workerInstance.Name,
        ["workerInstanceZone"] = workerInstance.Zone,
        ["workerServiceAccountEmail"] = workerServiceAccount.Email,
    };
});
