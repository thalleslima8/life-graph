using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// Reversibility (E2 phase 2): tombstones on Nodes and Relations (DA-021), the link from an
    /// Undo to the GraphChangeSet it undoes (DA-020) and purged change entries. The xmin row
    /// versions are Postgres system columns: they add no SQL.
    /// </summary>
    public partial class GraphReversibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_status",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_operation",
                table: "change_entries");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "types",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "relations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "relations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "property_definitions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reverts_changeset_id",
                table: "changesets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "changesets",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purged_at",
                table: "change_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_relations_account_id_deleted_at",
                table: "relations",
                columns: new[] { "account_id", "deleted_at" },
                filter: "deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_account_id_deleted_at",
                table: "nodes",
                columns: new[] { "account_id", "deleted_at" },
                filter: "deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_changesets_account_id_reverts_changeset_id",
                table: "changesets",
                columns: new[] { "account_id", "reverts_changeset_id" },
                unique: true,
                filter: "reverts_changeset_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_status",
                table: "changesets",
                sql: "status IN ('applied', 'reverted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_operation",
                table: "change_entries",
                sql: "operation IN ('created', 'deleted', 'restored', 'updated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_purged_empty",
                table: "change_entries",
                sql: "purged_at IS NULL OR (before IS NULL AND after IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_changesets_reverts_changeset",
                table: "changesets",
                columns: new[] { "account_id", "reverts_changeset_id" },
                principalTable: "changesets",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_changesets_reverts_changeset",
                table: "changesets");

            migrationBuilder.DropIndex(
                name: "ix_relations_account_id_deleted_at",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "ix_nodes_account_id_deleted_at",
                table: "nodes");

            migrationBuilder.DropIndex(
                name: "uq_changesets_account_id_reverts_changeset_id",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changesets_status",
                table: "changesets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_operation",
                table: "change_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_entries_purged_empty",
                table: "change_entries");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "types");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "relations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "relations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "property_definitions");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "reverts_changeset_id",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "changesets");

            migrationBuilder.DropColumn(
                name: "purged_at",
                table: "change_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changesets_status",
                table: "changesets",
                sql: "status IN ('applied')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_entries_operation",
                table: "change_entries",
                sql: "operation IN ('created', 'updated')");
        }
    }
}
