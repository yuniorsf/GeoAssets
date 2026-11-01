using FluentAssertions;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

public class PostgresConnectionStringHelperTests
{
    private const string BaseConnectionString = "Host=localhost;Port=5432;Database=geoassets;Username=postgres;Password=secret";

    [Fact]
    public void WithStatementTimeout_NoExistingOptions_AddsTheStatementTimeoutOption()
    {
        var result = PostgresConnectionStringHelper.WithStatementTimeout(BaseConnectionString, TimeSpan.FromSeconds(30));

        new NpgsqlConnectionStringBuilder(result).Options.Should().Be("-c statement_timeout=30000");
    }

    [Fact]
    public void WithStatementTimeout_ExistingOptions_AppendsRatherThanOverwriting()
    {
        var withExistingOptions = $"{BaseConnectionString};Options=-c lock_timeout=5000";

        var result = PostgresConnectionStringHelper.WithStatementTimeout(withExistingOptions, TimeSpan.FromSeconds(10));

        new NpgsqlConnectionStringBuilder(result).Options.Should().Be("-c lock_timeout=5000 -c statement_timeout=10000");
    }

    [Fact]
    public void WithStatementTimeout_PreservesEveryOtherConnectionStringField()
    {
        var result = PostgresConnectionStringHelper.WithStatementTimeout(BaseConnectionString, TimeSpan.FromSeconds(1));

        var builder = new NpgsqlConnectionStringBuilder(result);
        builder.Host.Should().Be("localhost");
        builder.Port.Should().Be(5432);
        builder.Database.Should().Be("geoassets");
        builder.Username.Should().Be("postgres");
        builder.Password.Should().Be("secret");
    }

    [Fact]
    public void WithStatementTimeout_ZeroOrNegativeTimeout_Throws()
    {
        var act = () => PostgresConnectionStringHelper.WithStatementTimeout(BaseConnectionString, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithStatementTimeout_NullOrWhitespaceConnectionString_Throws()
    {
        var act = () => PostgresConnectionStringHelper.WithStatementTimeout(" ", TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentException>();
    }
}
