using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVIDEO.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Subtitles",
                table: "VideoProjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Subtitles",
                table: "VideoProjects");
        }
    }
}
