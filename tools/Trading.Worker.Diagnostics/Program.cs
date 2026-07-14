using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trading.Automation.Diagnostics;
using Trading.Worker.Diagnostics;

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["WorkerDiagnostics:Enabled"] = "true",
    ["WorkerDiagnostics:SentryInterval"] = "00:00:01",
    ["WorkerDiagnostics:SampleInterval"] = "00:00:05",
    ["WorkerDiagnostics:FlushInterval"] = "00:00:30",
    ["WorkerDiagnostics:LocalDirectory"] = "artifacts/diagnostics-lab",
    ["WorkerDiagnostics:SegmentMaximumBytes"] = "8388608",
    ["WorkerDiagnostics:RetentionMaximumBytes"] = "25165824",
    ["WorkerDiagnostics:UploadClosedSegments"] = "false",
    ["WorkerDiagnostics:Containment:Enabled"] = "false",
    ["SyntheticWorkerLoad:Enabled"] = "true",
    ["SyntheticWorkerLoad:Duration"] = "00:02:00",
    ["SyntheticWorkerLoad:AllocationInterval"] = "00:00:00.100",
    ["SyntheticWorkerLoad:RetainedMegabytes"] = "64",
    ["SyntheticWorkerLoad:ChurnMegabytesPerInterval"] = "4",
    ["SyntheticWorkerLoad:BurstMegabytes"] = "32",
    ["SyntheticWorkerLoad:BurstInterval"] = "00:00:15",
    ["SyntheticWorkerLoad:BurstHold"] = "00:00:00.500",
    ["SyntheticWorkerLoad:ResultPath"] = "artifacts/diagnostics-lab/synthetic-memory-lab.json",
});
builder.Configuration.AddCommandLine(args);

builder.Services.AddOptions<SyntheticWorkerLoadOptions>()
    .Bind(builder.Configuration.GetSection(SyntheticWorkerLoadOptions.SectionName));
builder.Services.AddWorkerDiagnostics(builder.Configuration);
builder.Services.AddHostedService<SyntheticWorkerLoadHostedService>();

await builder.Build().RunAsync();
