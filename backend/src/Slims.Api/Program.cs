using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Slims.Api.Middleware;
using Slims.Application;
using Slims.Infrastructure;
using Slims.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Lets the packaged API run as a Windows service (3-install-as-service). Does nothing in a console.
builder.Host.UseWindowsService(o => o.ServiceName = "SlimsApi");

// A relative App:UploadsDir is taken from the content root (the API folder), not from wherever the
// process happened to be started - a service starts in C:\Windows\System32.
foreach (var key in new[] { "App:UploadsDir", "App:DownloadsDir" })
{
    var dir = builder.Configuration[key] ?? (key == "App:DownloadsDir" ? "downloads" : null);
    if (!string.IsNullOrWhiteSpace(dir) && !Path.IsPathRooted(dir))
        builder.Configuration[key] = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dir));
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o => { o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never; });
builder.Services.AddEndpointsApiExplorer();
// reservation lapse, escalations, alerts, scheduled reports and e-mail / SMS delivery
builder.Services.AddHostedService<Slims.Api.AutomationWorker>();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "SLIMS — Street Lighting Store & Installation Management API", Version = "v1", Description = "Sign in with POST /api/auth/login, then send the access token as `Authorization: Bearer <token>`. Refresh tokens travel in an httpOnly cookie. Every stock movement is a scanned QR transaction; every installation carries geo-stamped before/after evidence validated against the site geo-fence." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
    var xml = Path.Combine(AppContext.BaseDirectory, "Slims.Api.xml"); if (File.Exists(xml)) o.IncludeXmlComments(xml);
});
var origins = builder.Configuration.GetSection("App:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 64L * 1024 * 1024);
// two base64 photographs from the field app
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64L * 1024 * 1024);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await Seeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), app.Logger);

app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger(); app.UseSwaggerUI(o => { o.RoutePrefix = "swagger"; o.DocumentTitle = "SLIMS API"; });
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/api/health", () => Results.Ok(new { ok = true, service = "SLIMS API", time = DateTime.UtcNow })).AllowAnonymous();

app.Run();
