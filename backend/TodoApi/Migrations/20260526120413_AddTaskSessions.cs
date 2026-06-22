using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TodoId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActiveCountAtStart = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSessions_Todos_TodoId",
                        column: x => x.TodoId,
                        principalTable: "Todos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskOverlaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlappingSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TodoId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlappingTodoId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlapStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OverlapEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OverlapMinutes = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskOverlaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskOverlaps_TaskSessions_OverlappingSessionId",
                        column: x => x.OverlappingSessionId,
                        principalTable: "TaskSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskOverlaps_TaskSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TaskSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskOverlaps_OverlappingSessionId",
                table: "TaskOverlaps",
                column: "OverlappingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskOverlaps_SessionId",
                table: "TaskOverlaps",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSessions_TodoId",
                table: "TaskSessions",
                column: "TodoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskOverlaps");

            migrationBuilder.DropTable(
                name: "TaskSessions");
        }
    }
}
