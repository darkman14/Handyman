using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Handyman.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ratings_job_id",
                schema: "app",
                table: "ratings");

            migrationBuilder.DropIndex(
                name: "ix_bids_job_id",
                schema: "app",
                table: "bids");

            migrationBuilder.CreateIndex(
                name: "ix_users_phone",
                schema: "app",
                table: "users",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ratings_job_id_rater_id",
                schema: "app",
                table: "ratings",
                columns: new[] { "job_id", "rater_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bids_job_id_worker_id",
                schema: "app",
                table: "bids",
                columns: new[] { "job_id", "worker_id" },
                unique: true);

            // Referral codes are unique regardless of letter case and surrounding spaces.
            // Not modelled in EF (no expression indexes); the model snapshot is unaffected.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_stores_referral_code_ci ON app.stores (lower(btrim(referral_code)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS app.ux_stores_referral_code_ci;");

            migrationBuilder.DropIndex(
                name: "ix_users_phone",
                schema: "app",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_ratings_job_id_rater_id",
                schema: "app",
                table: "ratings");

            migrationBuilder.DropIndex(
                name: "ix_bids_job_id_worker_id",
                schema: "app",
                table: "bids");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_job_id",
                schema: "app",
                table: "ratings",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_bids_job_id",
                schema: "app",
                table: "bids",
                column: "job_id");
        }
    }
}
