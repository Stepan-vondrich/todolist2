using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskManifestAndPlannerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskManifests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TodoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Odhad = table.Column<string>(type: "TEXT", nullable: false),
                    MuzuZacit = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    JenVPraci = table.Column<bool>(type: "INTEGER", nullable: false),
                    Dependencies = table.Column<string>(type: "TEXT", nullable: false),
                    Kdy = table.Column<string>(type: "TEXT", nullable: false),
                    MuzeBezetS = table.Column<string>(type: "TEXT", nullable: false),
                    CekaNaCloveka = table.Column<string>(type: "TEXT", nullable: false),
                    PevnyCas = table.Column<string>(type: "TEXT", nullable: false),
                    Periodicita = table.Column<string>(type: "TEXT", nullable: false),
                    AttentionSplit = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskManifests_Todos_TodoId",
                        column: x => x.TodoId,
                        principalTable: "Todos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskManifests_Slug",
                table: "TaskManifests",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskManifests_TodoId",
                table: "TaskManifests",
                column: "TodoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerSettings");

            migrationBuilder.DropTable(
                name: "TaskManifests");
        }
    }
}
