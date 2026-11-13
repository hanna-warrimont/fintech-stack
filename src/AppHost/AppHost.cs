var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka").WithKafkaUI();

var simulator = builder.AddProject<Projects.Portal_Simulator>("simulator")
    .WithReference(kafka).WaitFor(kafka);

builder.Build().Run();
