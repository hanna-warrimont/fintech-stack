var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka").WithKafkaUI();
var postgres = builder.AddPostgres("postgres");
var instrumentDb = postgres.AddDatabase("instrumentdb");
var searchDb = postgres.AddDatabase("searchdb");
var historyDb = postgres.AddDatabase("historydb");

// Fester Port 8080: Der Token-Aussteller (iss) enthält die URL – sie muss über Neustarts gleich bleiben.
// Lokal HTTP ohne Zertifikat: Sonst müssten Browser und Dienste dem Dev-Zertifikat vertrauen (Systemeinstellung).
// ponytail: Keycloak im Dev-Modus mit eingebauter Datenbank; eigene Postgres-DB und TLS erst in Azure (Runde 5)
#pragma warning disable ASPIRECERTIFICATES001 // experimentelle API; fällt mit TLS in Runde 5 ohnehin weg
var keycloak = builder.AddKeycloak("keycloak", 8080).WithoutHttpsCertificate().WithRealmImport("./keycloak");
#pragma warning restore ASPIRECERTIFICATES001

// WithHttpHealthCheck("/health"): Das Dashboard zeigt den Zustand, und WaitFor wartet auf "healthy" statt nur auf
// "gestartet" – der Simulator also, bis Instrument migriert, geseedet und mit seiner Datenbank verbunden ist.
var instrument = builder.AddProject<Projects.Portal_Instruments_Api>("instrument").WithHttpHealthCheck("/health")
    .WithReference(instrumentDb).WithReference(kafka).WithReference(keycloak)
    .WaitFor(instrumentDb).WaitFor(kafka).WaitFor(keycloak)
    // Experiment 17: InstrumentJobInstances=2 lässt den Wirksamkeits-Job zweimal im Instrument-Service laufen.
    .WithEnvironment("CorporateActions__JobInstances", builder.Configuration["InstrumentJobInstances"] ?? "1");

var search = builder.AddProject<Projects.Portal_Search>("search").WithHttpHealthCheck("/health")
    .WithReference(searchDb).WithReference(kafka).WaitFor(searchDb).WaitFor(kafka);

// Replikate per Umgebungsvariable (HistoryReplicas=2) für das Experiment Arbeitsteilung. Neuaufbau nur mit 1 Replikat sicher.
var history = builder.AddProject<Projects.Portal_History>("history").WithHttpHealthCheck("/health")
    .WithReference(historyDb).WithReference(kafka).WaitFor(historyDb).WaitFor(kafka)
    .WithReplicas(int.Parse(builder.Configuration["HistoryReplicas"] ?? "1", System.Globalization.CultureInfo.InvariantCulture));
// WaitFor(instrument) ist nur eine Optimierung: Der Instrument-Service legt instruments.v1 an.
var simulator = builder.AddProject<Projects.Portal_Simulator>("simulator").WithHttpHealthCheck("/health")
    .WithReference(kafka).WaitFor(kafka).WaitFor(instrument);

// Zwei Replikate zeigen Broadcast: jede Instanz hat eine eigene Consumer-Group und sieht alle Ticks.
// WaitFor(simulator) ist nur eine Optimierung: Der Simulator legt ticks.v1 mit 6 Partitionen an,
// und Quote übersteht ein (noch) fehlendes Topic ohnehin (siehe TickConsumer).
var quote = builder.AddProject<Projects.Portal_Quote>("quote").WithHttpHealthCheck("/health")
    .WithReference(kafka).WaitFor(simulator).WithReplicas(2);

var bff = builder.AddProject<Projects.Portal_Bff>("bff").WithHttpHealthCheck("/health")
    .WithReference(instrument).WithReference(search).WithReference(quote).WithReference(history)
    .WithReference(keycloak).WaitFor(keycloak).WithExternalHttpEndpoints();

builder.AddJavaScriptApp("frontend", "../Frontend", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("NUXT_BFF_URL", bff.GetEndpoint("http"))
    .WithReference(bff).WaitFor(bff).WithExternalHttpEndpoints();

builder.Build().Run();
