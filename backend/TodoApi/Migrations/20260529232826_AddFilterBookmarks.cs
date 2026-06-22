using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFilterBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilterBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    NameFilter = table.Column<string>(type: "TEXT", nullable: false),
                    ListFilter = table.Column<string>(type: "TEXT", nullable: false),
                    StatusFilter = table.Column<string>(type: "TEXT", nullable: false),
                    PrioritaExcluded = table.Column<string>(type: "TEXT", nullable: false),
                    RelatedFilter = table.Column<string>(type: "TEXT", nullable: false),
                    DetailRelatedFilter = table.Column<string>(type: "TEXT", nullable: false),
                    DateFrom = table.Column<string>(type: "TEXT", nullable: false),
                    DateTo = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilterBookmarks", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilterBookmarks");
        }
    }
}
