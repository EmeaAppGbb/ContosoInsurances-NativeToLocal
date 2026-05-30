var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);

var sqlDb = sqlServer.AddDatabase("insurancedb");

var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

var publicApi = builder.AddProject<Projects.ContosoInsurance_Api>("api")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq);

var backendApi = builder.AddProject<Projects.ContosoInsurance_BackendApi>("backendapi")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq);

var claimsWorker = builder.AddProject<Projects.ContosoInsurance_Worker_Claims>("worker-claims")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq)
    .WaitFor(publicApi);

var quotesWorker = builder.AddProject<Projects.ContosoInsurance_Worker_Quotes>("worker-quotes")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq)
    .WaitFor(publicApi);

var projectionsWorker = builder.AddProject<Projects.ContosoInsurance_Worker_Projections>("worker-projections")
    .WithReference(sqlDb)
    .WithReference(rabbitmq)
    .WaitFor(sqlDb)
    .WaitFor(rabbitmq)
    .WaitFor(claimsWorker)
    .WaitFor(quotesWorker)
    .WaitFor(backendApi);

builder.AddProject<Projects.ContosoInsurance_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(publicApi)
    .WaitFor(publicApi)
    .WaitFor(projectionsWorker);

builder.AddProject<Projects.ContosoInsurance_BackendPortal>("backendportal")
    .WithExternalHttpEndpoints()
    .WithReference(backendApi)
    .WaitFor(backendApi)
    .WaitFor(claimsWorker)
    .WaitFor(quotesWorker);

builder.Build().Run();
