var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka").WithKafkaUI();

var simulator = builder.AddProject<Projects.Portal_Simulator>("simulator")
    .WithReference(kafka).WaitFor(kafka);

// Zwei Replikate zeigen Broadcast: jede Instanz hat eine eigene Consumer-Group und sieht alle Ticks.
// WaitFor(simulator) ist nur eine Optimierung: Der Simulator legt ticks.v1 mit 6 Partitionen an,
// und Quote übersteht ein (noch) fehlendes Topic ohnehin (siehe TickConsumer).
var quote = builder.AddProject<Projects.Portal_Quote>("quote")
    .WithReference(kafka).WaitFor(simulator).WithReplicas(2);

var bff = builder.AddProject<Projects.Portal_Bff>("bff")
    .WithReference(simulator).WithReference(quote).WithExternalHttpEndpoints();

builder.AddJavaScriptApp("frontend", "../Frontend", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("NUXT_BFF_URL", bff.GetEndpoint("http"))
    .WithReference(bff).WaitFor(bff).WithExternalHttpEndpoints();

builder.Build().Run();
