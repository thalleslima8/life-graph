using System;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PurgeLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purge_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash_version = table.Column<short>(type: "smallint", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purge_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_purge_ledger_entries_hash_version", "hash_version >= 1");
                    table.CheckConstraint("ck_purge_ledger_entries_receipt_only_for_accounts", "(subject_kind = 'account') = (receipt_reference IS NOT NULL)");
                    table.CheckConstraint("ck_purge_ledger_entries_subject_hash_length", "octet_length(subject_hash) = 32");
                    table.CheckConstraint("ck_purge_ledger_entries_subject_kind", "subject_kind IN ('account', 'node')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_purge_ledger_entries_subject_hash",
                table: "purge_ledger_entries",
                column: "subject_hash");

            migrationBuilder.CreateIndex(
                name: "uq_purge_ledger_entries_receipt_reference",
                table: "purge_ledger_entries",
                column: "receipt_reference",
                unique: true);

            // The ledger has no account_id and no RLS (DA-253, exception to BE-022): its rows
            // outlive the Account. The application role only appends; reading or changing a row
            // is for an operator or E14's restore tooling, never for a web request (GEN-061).
            // Stated in full rather than left to the default privileges: everything revoked, then INSERT alone.
            migrationBuilder.Sql($"""
                REVOKE ALL ON purge_ledger_entries FROM {DatabaseRoles.Application};
                GRANT INSERT ON purge_ledger_entries TO {DatabaseRoles.Application};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purge_ledger_entries");
        }
    }
}
