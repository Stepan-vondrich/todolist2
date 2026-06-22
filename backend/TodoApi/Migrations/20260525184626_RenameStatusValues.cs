using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameStatusValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = '' WHERE \"Status\" = 'nothing'");
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = 'in-process' WHERE \"Status\" = 'in-progress'");
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = 'failed' WHERE \"Status\" = 'stuck'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = 'nothing' WHERE \"Status\" = ''");
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = 'in-progress' WHERE \"Status\" = 'in-process'");
            migrationBuilder.Sql("UPDATE \"Todos\" SET \"Status\" = 'stuck' WHERE \"Status\" = 'failed'");
        }
    }
}
