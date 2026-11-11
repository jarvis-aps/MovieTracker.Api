using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueMovieTitleAndYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Movies_Title_Year",
                table: "Movies",
                columns: new[] { "Title", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Movies_Title_Year",
                table: "Movies");
        }
    }
}
