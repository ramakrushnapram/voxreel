using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVIDEO.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    AspectRatio = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UseRag = table.Column<bool>(type: "boolean", nullable: false),
                    ScriptText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoProjects_MediaAssets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    NarrationText = table.Column<string>(type: "text", nullable: false),
                    VisualPrompt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    AudioAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenes_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_VideoProjectId_Index",
                table: "Scenes",
                columns: new[] { "VideoProjectId", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_OutputAssetId",
                table: "VideoProjects",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_UserId_CreatedUtc",
                table: "VideoProjects",
                columns: new[] { "UserId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "VideoProjects");
        }
    }
}
