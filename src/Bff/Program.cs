var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Runde 1: reiner Proxy unter /api. Login (Cookie-Session) und PDP-Aggregation kommen in Runde 4.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();
