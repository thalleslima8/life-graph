using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Turns the Nodes a read found into what an agent receives: free text in envelopes, cut to
/// size (DA-038, DA-036), the values of the Type and Outras propriedades, and a summary of
/// Provenance. Only Nodes the central read filter already let through come here, so the
/// history it reads is theirs alone.
/// </summary>
internal sealed class NodeViews(LifeGraphDbContext db, GraphReadFilter filter)
{
    /// <summary>
    /// The views of the given Nodes, in the given order; the ones the reader no longer sees are
    /// left out. <paramref name="distances"/> gives each Node's distance from where a walk
    /// started; a Node it does not name is at 0.
    /// </summary>
    public async Task<List<GraphNodeView>> ViewsAsync(
        IReadOnlyList<Guid> nodeIds,
        ViewLimits limits,
        IReadOnlyDictionary<Guid, int>? distances = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = await filter.Nodes().Where(node => nodeIds.Contains(node.Id)).ToDictionaryAsync(node => node.Id, cancellationToken);
        var typeIds = nodes.Values.Select(node => node.TypeId).OfType<Guid>().Distinct().ToList();
        var types = await filter.Types().Include(type => type.Properties)
            .Where(type => typeIds.Contains(type.Id))
            .ToDictionaryAsync(type => type.Id, cancellationToken);
        var definitionIds = types.Values.SelectMany(type => type.Properties.Select(property => property.PropertyDefinitionId))
            .Concat(nodes.Values.SelectMany(node => node.Properties.Values.Keys))
            .Distinct()
            .ToList();
        var definitions = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);
        var provenances = await ProvenanceOfAsync([.. nodes.Keys], cancellationToken);

        var views = new List<GraphNodeView>(nodes.Count);
        foreach (var nodeId in nodeIds)
        {
            if (!nodes.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            var provenance = provenances.GetValueOrDefault(node.Id, NodeProvenance.Unrecorded);
            var type = node.TypeId is { } typeId ? types.GetValueOrDefault(typeId) : null;
            var attachedIds = type?.Properties.OrderBy(property => property.Position).Select(property => property.PropertyDefinitionId).ToList() ?? [];
            var (body, bodyTruncated) = Cut(node.Body, limits.BodyMaxLength);

            GraphPropertyValue? ValueOf(Guid definitionId) =>
                definitions.TryGetValue(definitionId, out var definition)
                    ? PropertyValueOf(definition, node.Properties.Values.TryGetValue(definitionId, out var stored) ? stored : null, provenance, limits.TextMaxLength)
                    : null;

            views.Add(new GraphNodeView(
                node.Id,
                provenance.Envelope(Cut(node.Title, limits.TextMaxLength).Text),
                type?.Name,
                provenance.Envelope(body),
                bodyTruncated,
                distances?.GetValueOrDefault(node.Id) ?? 0,
                [.. attachedIds.Select(ValueOf).OfType<GraphPropertyValue>()],
                [.. node.Properties.Values.Keys
                    .Where(definitionId => !attachedIds.Contains(definitionId))
                    .Select(ValueOf)
                    .OfType<GraphPropertyValue>()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)],
                provenance.Summary,
                node.UpdatedAt));
        }

        return views;
    }

    /// <summary>The headlines of the given Nodes, in the given order, for hits, candidates and the other end of a Relation.</summary>
    public async Task<List<NodeHeadline>> HeadlinesAsync(IReadOnlyList<Guid> nodeIds, int textMaxLength, CancellationToken cancellationToken)
    {
        var nodes = await HeadlineNodesAsync(nodeIds, cancellationToken);
        var provenances = await ProvenanceOfAsync([.. nodes.Keys], cancellationToken);
        return [.. nodeIds.Where(nodes.ContainsKey).Select(nodeId =>
        {
            var (node, typeName) = nodes[nodeId];
            return new NodeHeadline(node.Id, provenances.GetValueOrDefault(nodeId, NodeProvenance.Unrecorded).Envelope(Cut(node.Title, textMaxLength).Text), typeName, node.UpdatedAt);
        })];
    }

    public async Task<List<SearchHit>> HitsAsync(IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var nodes = await HeadlineNodesAsync(nodeIds, cancellationToken);
        var provenances = await ProvenanceOfAsync([.. nodes.Keys], cancellationToken);
        return [.. nodeIds.Where(nodes.ContainsKey).Select(nodeId =>
        {
            var (node, typeName) = nodes[nodeId];
            var provenance = provenances.GetValueOrDefault(nodeId, NodeProvenance.Unrecorded);
            return new SearchHit(
                node.Id,
                provenance.Envelope(Cut(node.Title, GraphReadLimits.ContextTextMaxLength).Text),
                typeName,
                provenance.Envelope(Cut(node.Body, GraphReadLimits.ExcerptMaxLength).Text),
                node.UpdatedAt);
        })];
    }

    /// <summary>
    /// Cuts a text to at most <paramref name="maxLength"/> characters, never between the two
    /// halves of a surrogate pair.
    /// </summary>
    public static (string Text, bool Truncated) Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return (text, false);
        }

        var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return (text[..length], true);
    }

    private async Task<Dictionary<Guid, (Node Node, string? TypeName)>> HeadlineNodesAsync(IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var types = filter.Types();
        var rows = await (
            from node in filter.Nodes()
            where nodeIds.Contains(node.Id)
            join type in types on node.TypeId equals (Guid?)type.Id into typed
            from type in typed.DefaultIfEmpty()
            select new { Node = node, TypeName = type == null ? null : type.Name })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Node.Id, row => (row.Node, (string?)row.TypeName));
    }

    // Text and URL values are free text, in the envelope; a Select gives the labels it chose.
    private static GraphPropertyValue PropertyValueOf(PropertyDefinition definition, JsonElement? stored, NodeProvenance provenance, int textMaxLength)
    {
        if (stored is not { } value)
        {
            return new GraphPropertyValue(definition.Name, definition.ValueKind, null, null);
        }

        string LabelOf(JsonElement choice) =>
            definition.Options.FirstOrDefault(option => option.Id.ToString() == choice.GetString())?.Label ?? RemovedOptionLabel;

        return definition.ValueKind switch
        {
            PropertyValueKind.Text or PropertyValueKind.Url when value.ValueKind == JsonValueKind.String =>
                new GraphPropertyValue(definition.Name, definition.ValueKind, null, provenance.Envelope(Cut(value.GetString()!, textMaxLength).Text)),
            PropertyValueKind.Select when value.ValueKind == JsonValueKind.String =>
                new GraphPropertyValue(definition.Name, definition.ValueKind, JsonSerializer.SerializeToElement(LabelOf(value)), null),
            PropertyValueKind.MultiSelect when value.ValueKind == JsonValueKind.Array =>
                new GraphPropertyValue(definition.Name, definition.ValueKind, JsonSerializer.SerializeToElement(value.EnumerateArray().Select(LabelOf).ToList()), null),
            _ => new GraphPropertyValue(definition.Name, definition.ValueKind, value.Clone(), null),
        };
    }

    /// <summary>What a value still pointing at an option the property no longer has shows.</summary>
    public const string RemovedOptionLabel = "(removed option)";

    /// <summary>
    /// The Provenance of each Node, from its own history: the first and the latest change, and
    /// whether an agent or an import ever wrote it. Once one did, its text is untrusted for
    /// good, even after the person edits it: the person may not have rewritten every part
    /// (DA-038). Aggregated in SQL, one row per Node, however long its history (DB-023).
    /// </summary>
    private async Task<Dictionary<Guid, NodeProvenance>> ProvenanceOfAsync(List<Guid> nodeIds, CancellationToken cancellationToken)
    {
        if (nodeIds.Count == 0)
        {
            return [];
        }

        var rows = await SqlRows.QueryAsync(
            db,
            ProvenanceSql,
            [new NpgsqlParameter("node_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = nodeIds.ToArray() }],
            reader =>
            {
                ProvenanceStamp StampAt(string prefix) => new(
                    SqlRows.EnumOf<ChangeActorKind>(reader.GetString(reader.GetOrdinal($"{prefix}_actor_kind"))),
                    reader.GetFieldValue<Guid?>(reader.GetOrdinal($"{prefix}_agent_identity_id")),
                    SqlRows.EnumOf<WriteChannel>(reader.GetString(reader.GetOrdinal($"{prefix}_channel"))),
                    reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal($"{prefix}_at")));

                var source = reader.GetBoolean(reader.GetOrdinal("by_agent")) ? TextSource.Agent
                    : reader.GetBoolean(reader.GetOrdinal("by_import")) ? TextSource.Import
                    : TextSource.User;
                return (NodeId: reader.GetGuid(reader.GetOrdinal("node_id")), Provenance: new NodeProvenance(source, new ProvenanceSummary(StampAt("first"), StampAt("last"))));
            },
            cancellationToken);
        return rows.ToDictionary(row => row.NodeId, row => row.Provenance);
    }

    private const string NodeChangesSql = """
        FROM change_entries e
        JOIN changesets cs ON cs.id = e.changeset_id
        WHERE e.entity_kind = 'node' AND e.entity_id = n.id
        """;

    private const string ProvenanceSql = $"""
        SELECT n.id AS node_id,
               f.actor_kind AS first_actor_kind, f.agent_identity_id AS first_agent_identity_id, f.channel AS first_channel, f.created_at AS first_at,
               l.actor_kind AS last_actor_kind, l.agent_identity_id AS last_agent_identity_id, l.channel AS last_channel, l.created_at AS last_at,
               w.by_agent, w.by_import
        FROM unnest(@node_ids) AS n(id)
        CROSS JOIN LATERAL (
            SELECT cs.actor_kind, cs.agent_identity_id, cs.channel, cs.created_at
            {NodeChangesSql}
            ORDER BY cs.created_at, cs.id
            LIMIT 1) f
        CROSS JOIN LATERAL (
            SELECT cs.actor_kind, cs.agent_identity_id, cs.channel, cs.created_at
            {NodeChangesSql}
            ORDER BY cs.created_at DESC, cs.id DESC
            LIMIT 1) l
        CROSS JOIN LATERAL (
            SELECT bool_or(cs.actor_kind = 'agent_identity') AS by_agent, bool_or(cs.channel = 'import') AS by_import
            {NodeChangesSql}) w
        """;

    /// <summary>Who wrote a Node and its Provenance summary; <see cref="Unrecorded"/> when its history holds nothing.</summary>
    private sealed record NodeProvenance(TextSource Source, ProvenanceSummary? Summary)
    {
        public static readonly NodeProvenance Unrecorded = new(TextSource.User, null);

        public EnvelopedText Envelope(string text) =>
            new(text, Source == TextSource.User ? TextTrust.Trusted : TextTrust.Untrusted, Source);
    }
}

/// <summary>How long the free text of a Node view may be: its title and values, and its body.</summary>
internal sealed record ViewLimits(int TextMaxLength, int BodyMaxLength)
{
    /// <summary>A Node read on its own: the body gets more room than in a context.</summary>
    public static readonly ViewLimits Node = new(GraphReadLimits.ContextTextMaxLength, GraphReadLimits.NodeBodyMaxLength);

    /// <summary>A Node in a context, where every text is cut alike.</summary>
    public static readonly ViewLimits Context = new(GraphReadLimits.ContextTextMaxLength, GraphReadLimits.ContextTextMaxLength);
}
