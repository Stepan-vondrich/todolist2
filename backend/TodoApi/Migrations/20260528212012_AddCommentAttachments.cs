using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the new table first
            migrationBuilder.CreateTable(
                name: "CommentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: true),
                    Preview = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentAttachments_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentAttachments_CommentId",
                table: "CommentAttachments",
                column: "CommentId");

            // 2. Migrate existing single-attachment data into the new table
            migrationBuilder.Sql(@"
                INSERT INTO CommentAttachments (CommentId, Path, Type, Preview, SortOrder)
                SELECT Id, Attachment, AttachmentType, AttachmentPreview, 0
                FROM Comments
                WHERE Attachment IS NOT NULL AND Attachment != ''
            ");

            // 3. Drop old attachment columns (EF rebuilds the table internally for SQLite)
            migrationBuilder.DropColumn(name: "Attachment",        table: "Comments");
            migrationBuilder.DropColumn(name: "AttachmentPreview", table: "Comments");
            migrationBuilder.DropColumn(name: "AttachmentType",    table: "Comments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentAttachments");

            migrationBuilder.AddColumn<string>(
                name: "Attachment",
                table: "Comments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPreview",
                table: "Comments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentType",
                table: "Comments",
                type: "TEXT",
                nullable: true);
        }
    }
}
