using Elastic.Apm;
using Elastic.Apm.AspNetCore;
using Elastic.Apm.NetCoreAll;
using Elastic.Apm.StackExchange.Redis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Configuration;
using TestProj.Data;
using TestProj.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

// Add Redis configuration
//var redisConfiguration = builder.Configuration.GetSection("Redis")["ConnectionStrings"];
var redisConfiguration = builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Connection string 'Redis' not found.");
var redis = ConnectionMultiplexer.Connect(redisConfiguration);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddHttpClient();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddRuntimeInstrumentation()
            .AddPrometheusHttpListener()
            .Build();

//builder.Services.AddHttpClient();
//builder.Services.AddAllElasticApm();
//builder.Services.AddHostedService<Worker>();

//var app = builder.Build();

//Configure OpenTelemetry with tracing and auto-start.
//builder.Services.AddOpenTelemetry()
//    .ConfigureResource(resource =>
//        resource.AddService(serviceName: "dotnetcore-mehedy-project"))
//    .WithTracing(tracing => tracing
//        .AddAspNetCoreInstrumentation()
//        .AddOtlpExporter(otlpOptions =>
//        {
//            //SigNoz Cloud Endpoint 
//            otlpOptions.Endpoint = new Uri("https://signoz.osl.team:4317");

//            otlpOptions.Protocol = OtlpExportProtocol.Grpc;

//            ////SigNoz Cloud account Ingestion key
//            //string headerKey = "signoz-ingestion-key";
//            //string headerValue = "c1f772d6-9389-4675-9f6b-4abeb8bdcb06";

//            //string formattedHeader = $"{headerKey}={headerValue}";
//            //otlpOptions.Headers = formattedHeader;
//        }));


var serviceName = "MyAspNetCoreAppMehedy";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName)) // Your service name for APM
    .WithTracing(tracerProviderBuilder => tracerProviderBuilder
        .AddAspNetCoreInstrumentation() // Auto-instrument ASP.NET Core
        .AddHttpClientInstrumentation() // Auto-instrument outgoing HTTP calls
        .AddSqlClientInstrumentation()  // Auto-instrument SQL Database queries
        .AddRedisInstrumentation()      // Auto-instrument Redis calls
        .AddSource(serviceName)
        .AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri("https://otelcollector.udvash-unmesh.com/"); // OpenTelemetry Collector
            opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        })
    )
    .WithMetrics(metricsProviderBuilder => metricsProviderBuilder
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(serviceName)
        .AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri("http://192.168.2.93:4317");
        })
    );

var app = builder.Build();

var logger = app.Logger;
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Add Elastic APM
//app.UseAllElasticApm(builder.Configuration);
//redis.UseElasticApm();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseMiddleware<TestProj.Middleware.RequestTrackingMiddleware>();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapGet("/live-requests", () =>
{
    return Results.Json(RequestTracker.GetActiveRequests());
});

app.MapGet("/live-requests-ui", () =>
{
    var html = """
<!DOCTYPE html>
<html>
<head>
  <title>Live Requests</title>
  <meta charset="UTF-8">
  <style>
    body { font-family: Arial, sans-serif; background: #f9f9f9; margin: 20px; }
    h1 { color: #FF6440; }
    table { width: 100%; border-collapse: collapse; margin-top: 15px; }
    th, td { padding: 8px; border-bottom: 1px solid #ddd; text-align: left; }
    th { background-color: #FF6440; color: white; }
    tr:hover { background-color: #f1f1f1; }
    .status-InProgress { color: green; font-weight: bold; }
    .status-Completed { color: blue; font-weight: bold; }
    .status-Error { color: red; font-weight: bold; }
    .status-ClientDisconnected { color: orange; font-weight: bold; }
  </style>
</head>
<body>

<h1>Live Requests</h1>
<p>Auto-updating every second</p>

<div id="table-container">Loading...</div>

<script>
async function loadData() {
    const res = await fetch('/live-requests');
    const data = await res.json();

    let html = `<table>
        <tr>
            <th>Method</th>
            <th>Path</th>
            <th>Client</th>
            <th>Status</th>
            <th>State</th>
            <th>Duration (ms)</th>
        </tr>`;

    data.forEach(req => {
        const durationMs = Math.round(req.durationMs);
        html += `
            <tr>
                <td>${req.method}</td>
                <td>${req.path}</td>
                <td>${req.clientIp}</td>
                <td class="status-${req.status}">${req.status}</td>
                <td>${req.state}</td>
                <td>${durationMs}</td>
            </tr>`;
    });

    html += "</table>";
    document.getElementById("table-container").innerHTML = html;
}

setInterval(loadData, 1000);
loadData();
</script>

</body>
</html>
""";

    return Results.Content(html, "text/html");
});
app.Run();
