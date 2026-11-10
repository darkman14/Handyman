using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Handyman.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "areas",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_areas", x => x.id);
                    table.CheckConstraint("ck_areas_kind", "kind IN ('City', 'Municipality', 'Neighborhood')");
                    table.ForeignKey(
                        name: "fk_areas_areas_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "app",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    area_id = table.Column<int>(type: "integer", nullable: false),
                    referral_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                    table.ForeignKey(
                        name: "fk_stores_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "app",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    home_area_id = table.Column<int>(type: "integer", nullable: true),
                    rating_sum = table.Column<int>(type: "integer", nullable: false),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('Client', 'Worker', 'Admin')");
                    table.ForeignKey(
                        name: "fk_users_areas_home_area_id",
                        column: x => x.home_area_id,
                        principalSchema: "app",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_tokens",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    area_id = table.Column<int>(type: "integer", nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    house_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entrance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    floor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    apartment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    store_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accepted_bid_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_status", "status IN ('Open', 'Assigned', 'Done', 'Closed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_jobs_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "app",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_stores_store_id",
                        column: x => x.store_id,
                        principalSchema: "app",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_trades_trade_id",
                        column: x => x.trade_id,
                        principalSchema: "app",
                        principalTable: "trades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_users_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "worker_areas",
                schema: "app",
                columns: table => new
                {
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_areas", x => new { x.worker_id, x.area_id });
                    table.ForeignKey(
                        name: "fk_worker_areas_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "app",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_worker_areas_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "worker_profiles",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_worker_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "worker_trades",
                schema: "app",
                columns: table => new
                {
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_trades", x => new { x.worker_id, x.trade_id });
                    table.ForeignKey(
                        name: "fk_worker_trades_trades_trade_id",
                        column: x => x.trade_id,
                        principalSchema: "app",
                        principalTable: "trades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_worker_trades_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bids",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arrival_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrival_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    diagnostic_fee = table.Column<int>(type: "integer", nullable: false),
                    hourly_rate = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bids", x => x.id);
                    table.CheckConstraint("ck_bids_arrival_window", "arrival_window_end > arrival_window_start");
                    table.CheckConstraint("ck_bids_money", "diagnostic_fee >= 0 AND hourly_rate >= 0");
                    table.CheckConstraint("ck_bids_status", "status IN ('Active', 'Accepted', 'Rejected', 'Withdrawn')");
                    table.ForeignKey(
                        name: "fk_bids_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "app",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bids_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_photos",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_photos_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "app",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "app",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rater_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ratee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ratings", x => x.id);
                    table.CheckConstraint("ck_ratings_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ratings_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "app",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ratings_users_ratee_id",
                        column: x => x.ratee_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ratings_users_rater_id",
                        column: x => x.rater_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_areas_parent_id",
                schema: "app",
                table: "areas",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_bids_job_id",
                schema: "app",
                table: "bids",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_bids_worker_id",
                schema: "app",
                table: "bids",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id",
                schema: "app",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_photos_job_id",
                schema: "app",
                table: "job_photos",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_area_id",
                schema: "app",
                table: "jobs",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_client_id",
                schema: "app",
                table: "jobs",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_number",
                schema: "app",
                table: "jobs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_area_id",
                schema: "app",
                table: "jobs",
                columns: new[] { "status", "area_id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_store_id",
                schema: "app",
                table: "jobs",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_trade_id",
                schema: "app",
                table: "jobs",
                column: "trade_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_job_id",
                schema: "app",
                table: "notifications",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                schema: "app",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_job_id",
                schema: "app",
                table: "ratings",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_ratee_id",
                schema: "app",
                table: "ratings",
                column: "ratee_id");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_rater_id",
                schema: "app",
                table: "ratings",
                column: "rater_id");

            migrationBuilder.CreateIndex(
                name: "ix_stores_area_id",
                schema: "app",
                table: "stores",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_home_area_id",
                schema: "app",
                table: "users",
                column: "home_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_areas_area_id",
                schema: "app",
                table: "worker_areas",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_trades_trade_id",
                schema: "app",
                table: "worker_trades",
                column: "trade_id");

            // Row-level security on every table in schema app (including the migrations history table),
            // and no policies: only the API's database role can read or write. Add RLS here for any new table.
            migrationBuilder.Sql("""
                DO $$
                DECLARE t record;
                BEGIN
                    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'app' LOOP
                        EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', t.tablename);
                    END LOOP;
                END $$;
                """);

            // On Supabase, also take the Data API roles' privileges away (these roles do not exist on plain Postgres).
            migrationBuilder.Sql("""
                DO $$
                DECLARE r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['anon', 'authenticated'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA app FROM %I', r);
                            EXECUTE format('REVOKE ALL ON SCHEMA app FROM %I', r);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bids",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "job_photos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "worker_areas",
                schema: "app");

            migrationBuilder.DropTable(
                name: "worker_profiles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "worker_trades",
                schema: "app");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "app");

            migrationBuilder.DropTable(
                name: "trades",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "areas",
                schema: "app");
        }
    }
}
