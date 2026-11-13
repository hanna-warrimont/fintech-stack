var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka").WithKafkaUI();

var simulator = builder.AddProject<Projects.Portal_Simulator>("simulator")
    .WithReference(kafka).WaitFor(kafka);

// Zwei Replikate zeigen Broadcast: jede Instanz hat eine eigene Consumer-Group und sieht alle Ticks.
// WaitFor(simulator): Der Simulator legt ticks.v1 mit 6 Partitionen an, bevor Quote abonniert.
var quote = builder.AddProject<Projects.Portal_Quote>("quote")
    .WithReference(kafka).WaitFor(simulator).WithReplicas(2);

var bff = builder.AddProject<Projects.Portal_Bff>("bff")
    .WithReference(simulator).WithReference(quote).WithExternalHttpEndpoints();

builder.Build().Run();
