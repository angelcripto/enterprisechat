using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseChat.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Users",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceProviderId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    HashAlgorithm = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaintextRiskAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedSecretsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SourceProviderId_ExternalId",
                table: "Users",
                columns: new[] { "SourceProviderId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuthProviders_Priority",
                table: "AuthProviders",
                column: "Priority");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_AuthProviders_SourceProviderId",
                table: "Users",
                column: "SourceProviderId",
                principalTable: "AuthProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_AuthProviders_SourceProviderId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "AuthProviders");

            migrationBuilder.DropIndex(
                name: "IX_Users_SourceProviderId_ExternalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SourceProviderId",
                table: "Users");
        }
    }
}
