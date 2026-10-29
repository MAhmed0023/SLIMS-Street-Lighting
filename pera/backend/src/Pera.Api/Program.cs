using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Pera.Api.Middleware;
using Pera.Application;
using Pera.Application.Interfaces;
using Pera.Infrastructure;
using Pera.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Lets the packaged API run as a Windows service. Does nothing in a console.
builder.Host.UseWindowsService(o => o.ServiceName = "PeraApi");

// A relative App:UploadsDir is taken from the content root (the API folder), not from wherever the process started.
var uploads = builder.Configuration["App:UploadsDir"];
if (!string.IsNullOrWhiteSpace(uploads) && !Path.IsPathRooted(uploads))
    builder.Configuration["App:UploadsDir"] = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, uploads));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
// reminders and escalations (FR-024)
builder.Services.AddHostedService<Pera.Api.AutomationWorker>();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "PERA — Task, Document Movement, Approval & Signature API", Version = "v1",
        Description = "Sign in with POST /api/auth/login, then send the access token as `Authorization: Bearer <token>`. Refresh tokens travel in an httpOnly cookie. " +
                      "Every permission, hierarchy, stage and authority check is enforced here on the server (SR-SEC-02)." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
    var xml = Path.Combine(AppContext.BaseDirectory, "Pera.Api.xml"); if (File.Exists(xml)) o.IncludeXmlComments(xml);
});
var origins = builder.Configuration.GetSection("App:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 210L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 210L * 1024 * 1024);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    await Seeder.RunAsync(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IFileStorage>(), app.Logger, app.Configuration.GetValue("App:SeedDemo", true));
    if (sp.GetRequiredService<ISigningKey>() is Pera.Infrastructure.Identity.SigningKey { Derived: true })
        app.Logger.LogWarning("Signing:Key is not set; the signature seal key is derived from this machine. Set Signing:Key from a secret store before production (SR-SEC-04).");
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger(); app.UseSwaggerUI(o => { o.RoutePrefix = "swagger"; o.DocumentTitle = "PERA API"; });
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/api/health", () => Results.Ok(new { ok = true, service = "PERA API", time = DateTime.UtcNow })).AllowAnonymous();

app.Run();

/// <summary>Exposed for integration tests.</summary>
public partial class Program;
