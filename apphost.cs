#:sdk Aspire.AppHost.Sdk@13.6.1
#:property AspireUseCliBundle=true

var builder = DistributedApplication.CreateBuilder(args);

// The aspireify skill will wire up your projects here.

builder.Build().Run();