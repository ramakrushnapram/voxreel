using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVIDEO.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenerationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SourceImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Length = table.Column<int>(type: "integer", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AspectRatio = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    GenerateAudio = table.Column<bool>(type: "boolean", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    PolloTaskId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Credit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextPollUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RemoteExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_GenerationRequests_GenerationRequestId",
                        column: x => x.GenerationRequestId,
                        principalTable: "GenerationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRequests_CreatedUtc",
                table: "GenerationRequests",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRequests_PolloTaskId",
                table: "GenerationRequests",
                column: "PolloTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRequests_Status_NextPollUtc",
                table: "GenerationRequests",
                columns: new[] { "Status", "NextPollUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_CreatedUtc",
                table: "MediaAssets",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_GenerationRequestId",
                table: "MediaAssets",
                column: "GenerationRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "GenerationRequests");
        }
    }
}
