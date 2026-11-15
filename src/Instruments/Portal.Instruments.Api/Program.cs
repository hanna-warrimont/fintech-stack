using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Api;
using Portal.Instruments.Application;
using Portal.Instruments.Infrastructure;
using Portal.Instruments.Infrastructure.CorporateActions;
using Portal.Instruments.Infrastructure.Outbox;
using Portal.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kafka = new KafkaSettings(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddSingleton(TimeProvider.System);
// Aspire-Integration: Datenquelle aus ConnectionStrings:instrumentdb mit Health Check, Traces und Metriken.
builder.AddNpgsqlDataSource("instrumentdb");
builder.Services
    .AddInstrumentApplication(builder.Configuration.GetValue<TimeSpan?>("CorporateActions:LeadTime"))
    .AddInstrumentInfrastructure();
// Der Instrument-Service prüft selbst: Signatur und Aussteller (Keycloak-Realm), Audience portal-api, Rollen.
// Das BFF reicht das Access Token nur durch – es entscheidet nicht über Rechte.
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = $"{builder.Configuration["services:keycloak:http:0"]}/realms/portal";
    o.Audience = "portal-api";
    o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    o.MapInboundClaims = false;
    o.TokenValidationParameters.NameClaimType = "preferred_username";
    o.TokenValidationParameters.RoleClaimType = "roles";
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Pfleger, p => p.RequireRole("pfleger"))
    .AddPolicy(Policies.Freigeber, p => p.RequireRole("freigeber"))
    .AddPolicy(Policies.PflegerOderFreigeber, p => p.RequireRole("pfleger", "freigeber"));
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(kafka.Producer()).Build());
builder.Services.AddHostedService<OutboxDispatcher>();
// Gesendete Outbox-Zeilen 7 Tage behalten (zum Nachsehen, was rausging), dann löschen.
builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<RetentionJob>(sp, RetentionJob.Outbox, TimeSpan.FromDays(7)));
// Experiment 17: CorporateActions__JobInstances=2 startet den Job doppelt – SKIP LOCKED verhindert doppelte Wirksamkeit.
for (var i = 0; i < builder.Configuration.GetValue("CorporateActions:JobInstances", 1); i++)
    builder.Services.AddSingleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<EffectivenessJob>(sp));

var app = builder.Build();

// Vor dem Start: Topic (kompaktiert) und Schema bereitstellen, dann Seed – alles bevor der Dispatcher läuft.
await kafka.EnsureTopicAsync(Topics.Instruments, Topics.Compacted);
await kafka.EnsureTopicAsync(Topics.CorporateActions, Topics.LongRetention);
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
await SeedData.EnsureAsync(app.Services, CancellationToken.None);

app.UseAuthentication();
app.UseAuthorization();
app.MapInstrumentEndpoints();
app.MapCorporateActionEndpoints();
app.MapDefaultEndpoints();

app.Run();
