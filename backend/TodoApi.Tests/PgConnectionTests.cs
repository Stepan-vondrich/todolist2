using Npgsql;
using TodoApi;

namespace TodoApi.Tests;

public class PgConnectionTests
{
    [Fact]
    public void Normalize_ConvertsNeonUri_ToNpgsqlConnectionString()
    {
        var uri = "postgresql://alice:s3cret@ep-cool-123.eu-central-1.aws.neon.tech/tododb?sslmode=require";
        var csb = new NpgsqlConnectionStringBuilder(PgConnection.Normalize(uri));
        Assert.Equal("ep-cool-123.eu-central-1.aws.neon.tech", csb.Host);
        Assert.Equal("alice", csb.Username);
        Assert.Equal("s3cret", csb.Password);
        Assert.Equal("tododb", csb.Database);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void Normalize_DecodesPercentEncodedCredentials()
    {
        var uri = "postgres://u:p%40ss%3Aword@host/db";
        var csb = new NpgsqlConnectionStringBuilder(PgConnection.Normalize(uri));
        Assert.Equal("p@ss:word", csb.Password);
    }

    [Fact]
    public void Normalize_LeavesKeyValueFormUnchanged()
    {
        var kv = "Host=myhost;Username=bob;Password=pw;Database=db;SSL Mode=Require";
        Assert.Equal(kv, PgConnection.Normalize(kv));
    }
}
