using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Api;
using Portal.Instruments.Application;
using Portal.Instruments.Infrastructure;
using Portal.Instruments.Infrastructure.Outbox;
using Portal.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kafka = builder.Configuration.GetConnectionString("kafka")!;
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddInstrumentApplication(builder.Configuration.GetValue<TimeSpan?>("CorporateActions:LeadTime"))
    .AddInstrumentInfrastructure(builder.Configuration.GetConnectionString("instrumentdb")!);
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
    new ProducerBuilder<string, string>(OutboxDispatcher.ProducerConfig(kafka)).Build());
builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();

// Vor dem Start: Topic (kompaktiert) und Schema bereitstellen, dann Seed – alles bevor der Dispatcher läuft.
await Topics.EnsureTopicAsync(kafka, Topics.Instruments, config: Topics.Compacted);
await Topics.EnsureTopicAsync(kafka, Topics.CorporateActions, config: Topics.LongRetention);
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
await SeedData.EnsureAsync(app.Services, CancellationToken.None);

app.UseAuthentication();
app.UseAuthorization();
app.MapInstrumentEndpoints();
app.MapCorporateActionEndpoints();
app.MapDefaultEndpoints();

app.Run();
