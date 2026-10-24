using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace LifeGraph.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class NodeEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "node_embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_node_embeddings", x => x.id);
                    table.UniqueConstraint("ak_node_embeddings_account_id_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_node_embeddings_content_hash", "char_length(content_hash) = 64");
                    table.CheckConstraint("ck_node_embeddings_dimension", "dimension = vector_dims(embedding)");
                    table.ForeignKey(
                        name: "fk_node_embeddings_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_node_embeddings_node",
                        columns: x => new { x.account_id, x.node_id },
                        principalTable: "nodes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_node_embeddings_embedding",
                table: "node_embeddings",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "uq_node_embeddings_account_id_node_id_model_id",
                table: "node_embeddings",
                columns: new[] { "account_id", "node_id", "model_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE node_embeddings ENABLE ROW LEVEL SECURITY;
                CREATE POLICY node_embeddings_isolation ON node_embeddings
                    USING (account_id = app.current_account_id())
                    WITH CHECK (account_id = app.current_account_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "node_embeddings");
        }
    }
}
