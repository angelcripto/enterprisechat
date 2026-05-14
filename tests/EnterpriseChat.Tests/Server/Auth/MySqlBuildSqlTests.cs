using EnterpriseChat.Server.Auth.Providers.MySql;
using FluentAssertions;

namespace EnterpriseChat.Tests.Server.Auth;

/// <summary>
/// Tests sin red. Validan que el SELECT generado por
/// <see cref="MySqlAuthProvider.BuildSelectSql"/> escapa correctamente
/// identificadores y rechaza inputs maliciosos. La parte que toca un
/// MySQL real queda fuera del CI por requerir infraestructura.
/// </summary>
public class MySqlBuildSqlTests
{
    [Fact]
    public void Builds_basic_select_with_quoted_identifiers()
    {
        var cfg = new MySqlProviderPublicConfig
        {
            Table = "users",
            UsernameColumn = "username",
            PasswordColumn = "password_hash",
        };

        var sql = MySqlAuthProvider.BuildSelectSql(cfg);

        sql.Should().Contain("`users`");
        sql.Should().Contain("`username`");
        sql.Should().Contain("`password_hash`");
        sql.Should().Contain("@username");
        sql.Should().EndWith("LIMIT 1");
    }

    [Fact]
    public void Adds_extra_where_when_provided()
    {
        var cfg = new MySqlProviderPublicConfig
        {
            Table = "users",
            UsernameColumn = "u",
            PasswordColumn = "p",
            ExtraWhere = "is_active = 1",
        };
        var sql = MySqlAuthProvider.BuildSelectSql(cfg);
        sql.Should().Contain("AND (is_active = 1)");
    }

    [Theory]
    [InlineData("users;DROP TABLE x")]
    [InlineData("users--")]
    [InlineData("usuarios con espacios")]
    [InlineData("")]
    public void Rejects_malicious_or_invalid_identifiers(string evilTable)
    {
        var cfg = new MySqlProviderPublicConfig
        {
            Table = evilTable,
            UsernameColumn = "u",
            PasswordColumn = "p",
        };
        Action act = () => MySqlAuthProvider.BuildSelectSql(cfg);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("is_active = 1; DROP TABLE x")]
    [InlineData("1=1 OR (SELECT 1)")]
    [InlineData("1=1 UNION SELECT 1")]
    public void Rejects_malicious_extra_where(string evil)
    {
        var cfg = new MySqlProviderPublicConfig
        {
            Table = "users",
            UsernameColumn = "u",
            PasswordColumn = "p",
            ExtraWhere = evil,
        };
        Action act = () => MySqlAuthProvider.BuildSelectSql(cfg);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allows_email_only_extra_where_with_function_in_lowercase()
    {
        // Caso permitido: filtros funcionales típicos. No es 100% seguro
        // pero la responsabilidad última es del admin.
        var cfg = new MySqlProviderPublicConfig
        {
            Table = "users",
            UsernameColumn = "u",
            PasswordColumn = "p",
            ExtraWhere = "deleted_at IS NULL",
        };
        Action act = () => MySqlAuthProvider.BuildSelectSql(cfg);
        act.Should().NotThrow();
    }
}
