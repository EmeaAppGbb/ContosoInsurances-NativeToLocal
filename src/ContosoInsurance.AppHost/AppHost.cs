var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure resources
var sqlServer = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);

var sqlDb = sqlServer.AddDatabase("insurancedb");

var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

// Backend API
var api = builder.AddProject<Projects.ContosoInsurance_Api>("api")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq);

// Background Worker
builder.AddProject<Projects.ContosoInsurance_Worker>("worker")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq);

// Blazor Web Frontend
builder.AddProject<Projects.ContosoInsurance_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
