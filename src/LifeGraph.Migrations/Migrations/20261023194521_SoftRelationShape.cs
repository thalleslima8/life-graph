using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <summary>
    /// Every Soft Relation has the shape the projection writes (DA-177, DA-186): explained by its
    /// method and model, kind related_to, lower id as source. Soft rows are derived data the
    /// projection recomputes (DA-070), so before the check the rows that cannot be repaired go
    /// (no explanation, or the reverse of a pair already stored in order) and the rest are put in
    /// order. No GraphChangeSet holds a Soft row, so nothing else changes.
    /// </summary>
    public partial class SoftRelationShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM relations
                WHERE assertion = 'soft' AND (inference_method IS NULL OR inference_model_id IS NULL);

                DELETE FROM relations reversed
                WHERE reversed.assertion = 'soft'
                  AND reversed.source_node_id > reversed.target_node_id
                  AND EXISTS (
                      SELECT 1 FROM relations ordered
                      WHERE ordered.assertion = 'soft'
                        AND ordered.account_id = reversed.account_id
                        AND ordered.source_node_id = reversed.target_node_id
                        AND ordered.target_node_id = reversed.source_node_id);

                UPDATE relations
                SET kind = 'related_to',
                    source_node_id = LEAST(source_node_id, target_node_id),
                    target_node_id = GREATEST(source_node_id, target_node_id)
                WHERE assertion = 'soft'
                  AND (kind <> 'related_to' OR source_node_id > target_node_id);
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_relations_soft_is_inferred",
                table: "relations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relations_soft_is_inferred",
                table: "relations",
                sql: "assertion = 'hard' OR (origin = 'system' AND confidence IS NOT NULL AND inference_method IS NOT NULL AND inference_model_id IS NOT NULL AND kind = 'related_to' AND source_node_id < target_node_id)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_relations_soft_is_inferred",
                table: "relations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relations_soft_is_inferred",
                table: "relations",
                sql: "assertion = 'hard' OR (origin = 'system' AND confidence IS NOT NULL)");
        }
    }
}
