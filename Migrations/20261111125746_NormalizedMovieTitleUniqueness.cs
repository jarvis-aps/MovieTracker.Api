using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizedMovieTitleUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Movies_Title_Year",
                table: "Movies");

            migrationBuilder.AddColumn<string>(
                name: "TitleKey",
                table: "Movies",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(btrim(\"Title\"))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TitleKey_Year",
                table: "Movies",
                columns: new[] { "TitleKey", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Movies_TitleKey_Year",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "TitleKey",
                table: "Movies");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Title_Year",
                table: "Movies",
                columns: new[] { "Title", "Year" },
                unique: true);
        }
    }
}
