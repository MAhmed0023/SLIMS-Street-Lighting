using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pera.Application;
using Pera.Application.Interfaces;
using Pera.Infrastructure.Documents;
using Pera.Infrastructure.Identity;
using Pera.Infrastructure.Persistence;
using Pera.Infrastructure.Storage;

namespace Pera.Tests;

/// <summary>Who the next scope acts as.</summary>
public sealed class TestUser : ICurrentUser
{
    public string? UserId { get; set; }
    public string? UserAgent => "tests";
    public string? Ip => "127.0.0.1";
    public string? CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CleanScanner : IFileScanner
{
    public Task<string?> ScanAsync(byte[] content, string fileName, CancellationToken ct = default)
        => Task.FromResult(fileName.Contains("eicar", StringComparison.OrdinalIgnoreCase) ? "EICAR-Test-File" : null);
}

/// <summary>The real application and infrastructure services over an in-memory SQLite database, seeded with the
/// illustrative hierarchy (no demo tasks). Each <see cref="As"/> call opens a fresh scope signed in as that user.</summary>
public sealed class TestHost : IAsyncDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly ServiceProvider _root;
    public string Uploads { get; } = Path.Combine(Path.GetTempPath(), "pera-tests-" + Guid.NewGuid().ToString("N"));
    public const string Password = "Passw0rd!x";

    TestHost()
    {
        _conn.Open();
        Directory.CreateDirectory(Uploads);
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddApplication();
        s.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn, x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        s.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        s.AddScoped<TestUser>();
        s.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestUser>());
        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        s.AddSingleton<IFileStorage>(new LocalFileStorage(Uploads));
        s.AddSingleton<IFileScanner, CleanScanner>();
        s.AddSingleton<IPdfStamper, PdfStamper>();
        s.AddSingleton<IReportWriter, XlsxReportWriter>();
        s.AddSingleton<ISigningKey>(new SigningKey("unit-test-signing-key-0123456789abcdef", ""));
        s.AddSingleton(new JwtOptions { Key = new string('k', 48) });
        s.AddSingleton<ITokenService, JwtTokenService>();
        _root = s.BuildServiceProvider();
    }

    public static async Task<TestHost> StartAsync()
    {
        var h = new TestHost();
        using var scope = h._root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Seeder.RunAsync(db, h._root.GetRequiredService<IFileStorage>(), NullLogger.Instance, demo: false);
        // everyone gets a known password so signing (which re-checks the password) can be exercised
        var hasher = h._root.GetRequiredService<IPasswordHasher>();
        foreach (var u in await db.Users.ToListAsync()) { var p = hasher.Hash(Password); u.PasswordHash = p.Hash; u.PasswordSalt = p.Salt; }
        await db.SaveChangesAsync();
        return h;
    }

    public Scope As(string userId)
    {
        var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestUser>().UserId = userId;
        return new Scope(scope);
    }

    public sealed class Scope(IServiceScope scope) : IDisposable
    {
        public T Get<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();
        public AppDbContext Db => Get<AppDbContext>();
        public void Dispose() => scope.Dispose();
    }

    /// <summary>Runs one call as a user in its own scope.</summary>
    public async Task<TResult> Run<TService, TResult>(string userId, Func<TService, Task<TResult>> call) where TService : notnull
    {
        using var s = As(userId);
        return await call(s.Get<TService>());
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _conn.DisposeAsync();
        try { Directory.Delete(Uploads, true); } catch { /* temp */ }
    }
}
