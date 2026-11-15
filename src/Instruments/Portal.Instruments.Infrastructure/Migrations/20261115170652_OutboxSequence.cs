using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Portal.Instruments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OutboxSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox_messages");

            migrationBuilder.AddColumn<long>(
                name: "seq",
                table: "outbox_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox_messages",
                column: "seq",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "seq",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }
    }
}
