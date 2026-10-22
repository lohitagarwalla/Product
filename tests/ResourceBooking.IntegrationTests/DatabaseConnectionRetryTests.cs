using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Transactions;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Exceptions;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Storage;

namespace ResourceBooking.IntegrationTests;

public class DatabaseConnectionRetryTests
{
    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/auth/login")]
    public async Task ApplicationRegistration_CoversCatalogAndLogin(string path)
    {
        var connection = new TestConnection { Failures = 100 };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestJwt();
            builder.ConfigureServices(services =>
            {
                // Retain Program's interceptor registration; replace only its connection.
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
                services.PostConfigure<DatabaseConnectionRetryOptions>(options => options.TimeoutSeconds = 1);
                services.PostConfigure<ImageStorageOptions>(options => options.CleanupEnabled = false);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = path.EndsWith("login")
            ? await client.PostAsJsonAsync(path, new LoginDto { Email = "retry@example.com", Password = "Password123!" })
            : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.InRange(connection.Attempts, 1, 2);
        Assert.DoesNotContain("private SQL diagnostic", await response.Content.ReadAsStringAsync());
    }

    private static ApplicationDbContext Context(TestConnection connection, int seconds = 3,
        HttpContext? http = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(connection)
        .AddInterceptors(new DatabaseConnectionInterceptor(
            Options.Create(new DatabaseConnectionRetryOptions { TimeoutSeconds = seconds }),
            TimeProvider.System, new HttpContextAccessor { HttpContext = http },
            NullLogger<DatabaseConnectionInterceptor>.Instance)).Options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionRecovers_AndEfStillClosesIt(bool synchronous)
    {
        var connection = new TestConnection { Failures = 1 };
        await using var db = Context(connection);
        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
        if (synchronous) db.Database.OpenConnection();
        else await db.Database.OpenConnectionAsync();
        Assert.Equal(2, connection.Attempts);
        Assert.Equal(ConnectionState.Open, connection.State);
        await db.Database.CloseConnectionAsync();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task HealthyConnection_OpensOnlyOnce()
    {
        var connection = new TestConnection();
        await using var db = Context(connection);
        await db.Database.OpenConnectionAsync();
        Assert.Equal(1, connection.Attempts);
    }

    [Theory]
    [InlineData(18456)]
    [InlineData(208)]
    public async Task PermanentErrors_AreNotRetried(int number)
    {
        var connection = new TestConnection { Failures = 10, ErrorNumber = number };
        await using var db = Context(connection);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.OpenConnectionAsync());
        Assert.Equal(1, connection.Attempts);
    }

    [Fact]
    public async Task UnavailableDatabase_ExhaustsBudget()
    {
        var connection = new TestConnection { Failures = 100 };
        await using var db = Context(connection, seconds: 1);
        var error = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => db.Database.OpenConnectionAsync());
        Assert.IsType<SqlException>(error.InnerException);
        Assert.InRange(connection.Attempts, 1, 2);
    }

    [Fact]
    public async Task DeadlineCancelsAnInFlightOpen()
    {
        var connection = new TestConnection { WaitForCancellation = true };
        await using var db = Context(connection, seconds: 1);
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => db.Database.OpenConnectionAsync());
        Assert.Equal(1, connection.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationStopsRetries_EvenWhenServiceOmitsToken(bool useHttpCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new TestConnection { Failures = 100 };
        var http = useHttpCancellation ? new DefaultHttpContext { RequestAborted = cancellation.Token } : null;
        await using var db = Context(connection, http: http);
        var pending = db.Database.OpenConnectionAsync(useHttpCancellation ? default : cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, connection.Attempts);
    }

    [Fact]
    public async Task AlreadyCancelledRequest_DoesNotOpenConnection()
    {
        var connection = new TestConnection();
        await using var db = Context(connection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Database.OpenConnectionAsync(new CancellationToken(true)));
        Assert.Equal(0, connection.Attempts);
    }

    [Fact]
    public async Task CommandFailure_IsNotReplayed()
    {
        var connection = new TestConnection { FailCommand = true };
        await using var db = Context(connection);
        // Exercise an actual EF command boundary after opening successfully.
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Test SET Value = 1"));
        Assert.Equal(1, connection.Commands);
        Assert.Equal(1, connection.Attempts);
    }

    [Fact]
    public async Task WriteExecutesOnceAfterConnectionRecovery()
    {
        var connection = new TestConnection { Failures = 1 };
        await using var db = Context(connection);
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync("UPDATE Test SET Value = 1"));
        Assert.Equal(2, connection.Attempts);
        Assert.Equal(1, connection.Commands);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CommitFailure_IsNotReplayed()
    {
        var connection = new TestConnection { FailCommit = true };
        await using var db = Context(connection);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<SqlException>(() => transaction.CommitAsync());
        Assert.Equal(1, connection.Attempts);
        Assert.Equal(1, connection.Commits);
    }

    [Fact]
    public async Task AmbientTransaction_DoesNotRetryOpening()
    {
        var connection = new TestConnection { Failures = 100 };
        await using var db = Context(connection);
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.OpenConnectionAsync());
        Assert.Equal(1, connection.Attempts);
    }

    private sealed class TestConnection : DbConnection
    {
        private ConnectionState state;
        public int Failures { get; init; }
        public int ErrorNumber { get; init; } = 40613;
        public bool WaitForCancellation { get; init; }
        public bool FailCommand { get; init; }
        public bool FailCommit { get; init; }
        public int Attempts { get; private set; }
        public int Commands { get; private set; }
        public int Commits { get; private set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "Server=test;Database=test";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() => state = ConnectionState.Closed;
        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (Attempts <= Failures) throw SqlFailure(ErrorNumber);
            state = ConnectionState.Open;
        }
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => new TestTransaction(this, isolationLevel);
        protected override DbCommand CreateDbCommand() => new TestCommand(this);

        private sealed class TestTransaction(TestConnection connection, System.Data.IsolationLevel isolationLevel) : DbTransaction
        {
            public override System.Data.IsolationLevel IsolationLevel => isolationLevel;
            protected override DbConnection DbConnection => connection;
            public override void Commit()
            {
                connection.Commits++;
                if (connection.FailCommit) throw SqlFailure(40613);
            }
            public override void Rollback() { }
        }

        private sealed class TestCommand(TestConnection connection) : DbCommand
        {
            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection { get; } = new SqlCommand().Parameters;
            protected override DbParameter CreateDbParameter() => new SqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery()
            {
                connection.Commands++;
                if (connection.FailCommand) throw SqlFailure(40613);
                return 1;
            }
            public override object? ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        }
    }

    // SqlClient exposes no public constructor for server errors.
    internal static SqlException SqlFailure(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(c => c.GetParameters().Length == 8);
        var error = (SqlError)constructor.Invoke([
            number, (byte)0, (byte)20, "test-server", "private SQL diagnostic", "", 0, null
        ]);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
            null, [typeof(SqlErrorCollection), typeof(string)], null)!.Invoke(null, [errors, "test"])!;
    }
}
