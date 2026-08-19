using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVIDEO.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Quality",
                table: "VideoProjects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quality",
                table: "VideoProjects");
        }
    }
}
