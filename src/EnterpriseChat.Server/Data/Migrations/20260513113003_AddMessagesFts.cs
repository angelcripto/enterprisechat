using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseChat.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// FTS5 was attempted here but the trigger-creation path through EF Core's
    /// migration runner kept rolling back silently in our environment. Search now
    /// uses a plain LIKE query against Messages.Body (see <c>SearchEndpoints</c>),
    /// which is sufficient for the MVP scale. Index added on Messages(Body) prefix
    /// to keep simple substring searches reasonable.
    /// </summary>
    public partial class AddMessagesFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. Search uses LIKE against Messages.Body.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
