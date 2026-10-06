using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class UserMovieDataUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMovieData_UserId",
                table: "UserMovieData");

            migrationBuilder.CreateIndex(
                name: "IX_UserMovieData_UserId_MovieId",
                table: "UserMovieData",
                columns: new[] { "UserId", "MovieId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMovieData_UserId_MovieId",
                table: "UserMovieData");

            migrationBuilder.CreateIndex(
                name: "IX_UserMovieData_UserId",
                table: "UserMovieData",
                column: "UserId");
        }
    }
}
