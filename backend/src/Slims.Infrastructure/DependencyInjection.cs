using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Slims.Application.Interfaces;
using Slims.Infrastructure.Identity;
using Slims.Infrastructure.Persistence;
using Slims.Infrastructure.Storage;

namespace Slims.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure(3)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IQrRenderer, QrRenderer>();
        services.AddSingleton<IImageStamper, Messaging.SkiaImageStamper>();
        services.AddHttpClient("sms", c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<IMessageSender, Messaging.MessageSender>();

        var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
        {
            // Development convenience: derive a stable key from the machine + connection string. Set Jwt:Key in production.
            jwt.Key = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + "|" + config.GetConnectionString("Default"))));
        }
        services.AddSingleton(jwt);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        services.AddAuthorization();

        var uploads = Path.GetFullPath(config["App:UploadsDir"] ?? Path.Combine(AppContext.BaseDirectory, "uploads"));
        Directory.CreateDirectory(uploads);
        services.AddSingleton<IFileStorage>(new LocalFileStorage(uploads));
        return services;
    }
}
