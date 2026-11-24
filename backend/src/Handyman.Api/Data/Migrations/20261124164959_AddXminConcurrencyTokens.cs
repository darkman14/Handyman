using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Handyman.Api.Data.Migrations
{
    /// <summary>
    /// Job.Version and Bid.Version map to the Postgres system column xmin (optimistic concurrency token).
    /// xmin exists on every table already, so there is no schema change; this migration only records the model in the snapshot.
    /// </summary>
    public partial class AddXminConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
