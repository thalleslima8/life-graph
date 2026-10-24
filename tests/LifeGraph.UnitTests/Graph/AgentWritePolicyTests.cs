using System.Text.Json;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The central agent write policy (E5): what is applied at once and what waits for the person.</summary>
public sealed class AgentWritePolicyTests
{
    private static readonly Guid Agent = Guid.CreateVersion7();
    private static readonly Guid OtherAgent = Guid.CreateVersion7();
    private static readonly AgentWriteOptions Limits = new();

    [Fact]
    public void Creations_and_edits_of_the_persons_entities_are_applied_and_only_the_edits_count()
    {
        var (facts, persons, _) = Facts();
        var assessment = Assess([new AgentCreateNode("New"), new AgentUpdateNode(persons, 1) { Title = "Edited" }], facts);

        Assert.Empty(assessment.Reasons);
        Assert.Equal(1, assessment.OnPersonsEntities);
    }

    [Fact]
    public void A_delete_is_sensitive_even_of_the_agents_own_node()
    {
        var (facts, _, own) = Facts();

        Assert.Equal([ProposalReason.Sensitive], Assess([new AgentDeleteNode(own, 1)], facts).Reasons);
    }

    // DA-042, DA-133: the Type of the person's Node is a proposal; of the agent's own, not.
    [Fact]
    public void Changing_the_type_is_a_proposal_only_on_the_persons_node()
    {
        var (facts, persons, own) = Facts();
        var type = new TypeAssignment(Guid.CreateVersion7());

        Assert.Equal([ProposalReason.TypeChange], Assess([new AgentUpdateNode(persons, 1) { Type = type }], facts).Reasons);
        Assert.Empty(Assess([new AgentUpdateNode(own, 1) { Type = type }], facts).Reasons);
    }

    // DA-133: another agent's creation is the person's entity for this agent.
    [Fact]
    public void Another_agents_creation_is_the_persons_entity()
    {
        var node = Guid.CreateVersion7();
        var facts = new AgentWriteFacts(
            new Dictionary<Guid, NodeFact> { [node] = new(node, null, OtherAgent) },
            new Dictionary<Guid, RelationFact>());

        Assert.Equal(1, Assess([new AgentUpdateNode(node, 1) { Title = "x" }], facts).OnPersonsEntities);
        Assert.False(AgentWritePolicy.IsAgentOwned(OtherAgent, Agent));
        Assert.True(AgentWritePolicy.IsAgentOwned(Agent, Agent));
    }

    // DA-046, DA-132: a capture that brings back the person's Resource changes their entity; a new link does not.
    [Fact]
    public void A_capture_counts_only_when_it_brings_back_the_persons_resource()
    {
        var facts = new AgentWriteFacts(
            new Dictionary<Guid, NodeFact>(),
            new Dictionary<Guid, RelationFact>(),
            new HashSet<string> { "https://example.test/persons" });

        Assert.Equal(1, Assess([new AgentCaptureResource("https://example.test/persons")], facts).OnPersonsEntities);
        Assert.Equal(0, Assess([new AgentCaptureResource("https://example.test/new")], facts).OnPersonsEntities);
    }

    // Testes obrigatórios — DA-046: 20 changes to the person's entities pass, 21 are a bulk.
    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void More_than_twenty_changes_to_the_persons_entities_are_a_bulk(int count, bool isBulk)
    {
        var nodes = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToList();
        var facts = new AgentWriteFacts(
            nodes.ToDictionary(id => id, id => new NodeFact(id, null, null)),
            new Dictionary<Guid, RelationFact>());
        var ownCreations = Enumerable.Range(0, 10).Select(index => (AgentOperation)new AgentCreateNode($"Own {index}"));

        var assessment = Assess([.. nodes.Select(id => (AgentOperation)new AgentUpdateNode(id, 1) { Title = "x" }), .. ownCreations], facts);

        Assert.Equal(isBulk, assessment.Reasons.Contains(ProposalReason.Bulk));
        Assert.Equal(count, assessment.OnPersonsEntities);
    }

    [Fact]
    public void Removing_the_agents_own_relation_does_not_count_but_the_persons_does()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var facts = new AgentWriteFacts(
            new Dictionary<Guid, NodeFact>(),
            new Dictionary<Guid, RelationFact> { [mine] = new(mine, Agent), [theirs] = new(theirs, null) });

        Assert.Equal(1, Assess([new AgentRemoveRelation(mine), new AgentRemoveRelation(theirs)], facts).OnPersonsEntities);
    }

    // DA-162, DA-132: tagging or untagging the person's Node changes it; the agent's own Node does not.
    [Fact]
    public void Tagging_counts_only_on_the_persons_node()
    {
        var (facts, persons, own) = Facts();

        Assert.Equal(2, Assess([new AgentTagNode(persons, "livro"), new AgentUntagNode(persons, "livro")], facts).OnPersonsEntities);
        Assert.Equal(0, Assess([new AgentTagNode(own, "livro"), new AgentUntagNode(own, "livro")], facts).OnPersonsEntities);
        Assert.Empty(Assess([new AgentTagNode(persons, "livro")], facts).Reasons);
    }

    [Fact]
    public void A_connection_that_requires_approval_turns_every_write_into_a_proposal()
    {
        var (facts, _, _) = Facts();

        var assessment = AgentWritePolicy.Assess([new AgentCreateNode("New")], facts, new WritingAgent(Agent, RequiresApproval: true), Limits);

        Assert.Equal([ProposalReason.ApprovalRequired], assessment.Reasons);
        Assert.True(AgentWritePolicy.RequiresUser(assessment.Reasons));
        Assert.False(AgentWritePolicy.RequiresUser([ProposalReason.Suggested]));
    }

    // Testes obrigatórios — DA-132: 20 per agent and 40 per Account; at the limit it passes.
    [Theory]
    [InlineData(19, 19, 1, false)]
    [InlineData(19, 19, 2, true)]
    [InlineData(0, 39, 1, false)]
    [InlineData(0, 40, 1, true)]
    [InlineData(20, 40, 0, false)]
    public void The_window_is_crossed_past_the_agent_or_the_account_ceiling(int byAgent, int byAccount, int operations, bool crosses) =>
        Assert.Equal(crosses, AgentWritePolicy.CrossesWindow(operations, new WindowUsage(byAgent, byAccount), Limits));

    private static AgentWriteAssessment Assess(IReadOnlyList<AgentOperation> operations, AgentWriteFacts facts) =>
        AgentWritePolicy.Assess(operations, facts, new WritingAgent(Agent, RequiresApproval: false), Limits);

    private static (AgentWriteFacts Facts, Guid Persons, Guid Own) Facts()
    {
        var persons = Guid.CreateVersion7();
        var own = Guid.CreateVersion7();
        return (new AgentWriteFacts(
            new Dictionary<Guid, NodeFact> { [persons] = new(persons, null, null), [own] = new(own, null, Agent) },
            new Dictionary<Guid, RelationFact>()), persons, own);
    }
}

/// <summary>The stored form of a proposal: canonical, so the same request has the same hash (DA-130).</summary>
public sealed class AgentOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_operations_have_the_same_hash_whatever_the_order_of_their_values()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        AgentOperation Update(params (Guid Id, int Value)[] values) => new AgentUpdateNode(node, 3)
        {
            Properties = values.ToDictionary(value => value.Id, value => JsonSerializer.SerializeToElement(value.Value)),
        };

        var one = ChangeProposal.ContentHashOf(AgentOperations.Serialize([Update((first, 1), (second, 2))]));
        var other = ChangeProposal.ContentHashOf(AgentOperations.Serialize([Update((second, 2), (first, 1))]));
        var newerBase = ChangeProposal.ContentHashOf(AgentOperations.Serialize([new AgentUpdateNode(node, 4)
        {
            Properties = new Dictionary<Guid, JsonElement> { [first] = JsonSerializer.SerializeToElement(1), [second] = JsonSerializer.SerializeToElement(2) },
        }]));

        Assert.Equal(one, other);
        Assert.NotEqual(one, newerBase);
        Assert.Equal(64, one.Length);
    }

    [Fact]
    public void Operations_read_back_as_they_were_stored()
    {
        var node = Guid.CreateVersion7();
        var type = Guid.CreateVersion7();
        IReadOnlyList<AgentOperation> operations =
        [
            new AgentCreateNode("Seneca", "Notes", Ref: "seneca", SuggestedType: "Person"),
            new AgentUpdateNode(node, 2) { Title = "Letters", Type = new TypeAssignment(type) },
            new AgentCreateRelation(NodeRef.Of(node), new NodeRef(null, "seneca"), "written_by"),
            new AgentRemoveRelation(Guid.CreateVersion7()),
            new AgentDeleteNode(node, 2),
        ];

        var stored = AgentOperations.Serialize(operations);
        var read = AgentOperations.Deserialize(stored);

        Assert.Equal(stored, AgentOperations.Serialize(read));
        Assert.Equal([node], AgentOperations.TargetNodeIds(read));
    }

    // DA-161, DA-162: a tag operation is stored with the name as the agent gave it, and becomes
    // the pipeline's operation with the Tag found by its key; a name without a Tag names none.
    [Fact]
    public void Tag_operations_round_trip_and_take_the_tag_by_its_key()
    {
        var node = Guid.CreateVersion7();
        var tag = Guid.CreateVersion7();
        IReadOnlyList<AgentOperation> agentOperations = [new AgentTagNode(node, "#Ficção  Científica"), new AgentUntagNode(node, "unknown")];

        var read = AgentOperations.Deserialize(AgentOperations.Serialize(agentOperations));
        var (operations, _) = AgentOperations.ToGraphOperations(
            read,
            Now,
            new Dictionary<string, Guid> { [AgentOperations.TagKeyOf("ficcao cientifica")] = tag });

        Assert.Equal(agentOperations, read);
        Assert.Equal([node], AgentOperations.TargetNodeIds(read));
        Assert.Equal(new TagNode(node, tag), operations[0]);
        Assert.Equal(new UntagNode(node, Guid.Empty), operations[1]);
    }

    // DA-049: a new Node without Type goes to the Inbox with the hint; refs resolve to the new
    // ids. The hint travels as the agent sent it: the Node drops it when it has a Type.
    [Fact]
    public void Graph_operations_put_an_untyped_node_in_the_inbox_and_resolve_refs()
    {
        var existing = Guid.CreateVersion7();
        var type = Guid.CreateVersion7();

        var (operations, created) = AgentOperations.ToGraphOperations(
            [
                new AgentCreateNode("Seneca", Ref: "seneca", SuggestedType: "Person"),
                new AgentCreateNode("Letters", TypeId: type, SuggestedType: "ignored"),
                new AgentCreateRelation(NodeRef.Of(existing), new NodeRef(null, "seneca"), "about"),
            ],
            Now);

        var untyped = Assert.IsType<CreateNode>(operations[0]);
        var typed = Assert.IsType<CreateNode>(operations[1]);
        var relation = Assert.IsType<CreateRelation>(operations[2]);
        Assert.Equal((true, "Person"), (untyped.InInbox, untyped.SuggestedType));
        Assert.Equal((false, "ignored"), (typed.InInbox, typed.SuggestedType));
        Assert.Equal(created["seneca"], relation.TargetNodeId);
        Assert.Equal(typed.NodeId, created["operations[1]"]);
    }
}

/// <summary>The lifecycle of a proposal (DA-043, DA-048, DA-130).</summary>
public sealed class ChangeProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_pending_proposal_past_its_lifetime_reads_as_expired_before_its_job_runs()
    {
        var proposal = Propose();

        Assert.Equal(ProposalStatus.Proposed, proposal.StatusAt(Now.AddDays(7).AddSeconds(-1)));
        Assert.Equal(ProposalStatus.Expired, proposal.StatusAt(Now.AddDays(7)));
        Assert.False(proposal.Expire(Now.AddDays(6)));
        Assert.True(proposal.Expire(Now.AddDays(7)));
        Assert.Equal(ProposalStatus.Expired, proposal.Status);
    }

    // DA-043 item 4: once the person's to approve, it stays so.
    [Fact]
    public void Requiring_the_person_is_for_good_and_keeps_every_reason()
    {
        var proposal = Propose();

        proposal.RequireUser([ProposalReason.Window], Now);
        proposal.RequireUser([ProposalReason.Bulk], Now);

        Assert.True(proposal.RequiresUser);
        Assert.Equal([ProposalReason.Suggested, ProposalReason.Bulk, ProposalReason.Window], proposal.Reasons);
    }

    [Fact]
    public void Only_a_pending_proposal_is_decided()
    {
        var proposal = Propose();
        proposal.Withdraw(Now);

        Assert.Throws<InvalidOperationException>(() => proposal.Reject(Now));
        Assert.Throws<InvalidOperationException>(() => proposal.AppliedByTheAgent(AppliedChanges.None, "k1", Now));
        Assert.Throws<InvalidOperationException>(() => proposal.ApprovedByThePerson(AppliedChanges.None, [], Now));
        Assert.Equal(ProposalStatus.Withdrawn, proposal.Status);
    }

    [Fact]
    public void Applying_part_of_it_keeps_the_rejected_operations_and_redacting_empties_it()
    {
        var proposal = Propose();
        var changeSet = Guid.CreateVersion7();

        proposal.ApprovedByThePerson(new AppliedChanges(changeSet, new Dictionary<string, Guid>()), [2, 1, 2], Now);
        proposal.Redact([Guid.CreateVersion7()], Now);

        Assert.Equal((ProposalStatus.Applied, changeSet), (proposal.Status, proposal.ChangeSetId));
        Assert.Equal([1, 2], proposal.RejectedOperations);
        Assert.Equal("[]", proposal.Operations);
        Assert.Null(proposal.Source);
    }

    // DA-043 item 7, DA-048: the Nodes an application created are found by Purge, which empties
    // the operations that hold their content; the names the agent's answer gave them stay for a
    // retry, less the name of a Node purged itself. The person's approval keeps no names.
    [Fact]
    public void Purge_finds_an_applied_proposal_by_its_new_nodes_and_forgets_only_the_purged_ones_names()
    {
        var (kept, purged) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var applied = Propose();
        applied.AppliedByTheAgent(
            new AppliedChanges(Guid.CreateVersion7(), new Dictionary<string, Guid> { ["kept"] = kept, ["operations[1]"] = purged }),
            "k1",
            Now);
        var approved = Propose();
        approved.ApprovedByThePerson(new AppliedChanges(Guid.CreateVersion7(), new Dictionary<string, Guid> { ["x"] = kept }), [], Now);

        Assert.Contains(kept, applied.TargetNodeIds);
        Assert.Contains(purged, applied.TargetNodeIds);
        Assert.Contains(kept, approved.TargetNodeIds);
        Assert.Empty(approved.CreatedNodes);

        applied.Redact([Guid.CreateVersion7()], Now);
        Assert.Equal(["kept", "operations[1]"], applied.CreatedNodes.Keys.Order(StringComparer.Ordinal));

        applied.Redact([purged], Now);
        Assert.Equal("[]", applied.Operations);
        Assert.Equal(kept, Assert.Single(applied.CreatedNodes).Value);
    }

    // DB-007: every change of the row moves updated_at.
    [Fact]
    public void Every_change_of_a_proposal_stamps_its_last_change()
    {
        var proposal = Propose();
        Assert.Equal(Now, proposal.UpdatedAt);

        proposal.RequireUser([ProposalReason.Window], Now.AddMinutes(1));
        Assert.Equal(Now.AddMinutes(1), proposal.UpdatedAt);

        proposal.Reject(Now.AddMinutes(2));
        Assert.Equal(Now.AddMinutes(2), proposal.UpdatedAt);

        proposal.Redact([Guid.CreateVersion7()], Now.AddMinutes(3));
        Assert.Equal(Now.AddMinutes(3), proposal.UpdatedAt);

        var expiring = Propose();
        expiring.Expire(Now.AddDays(8));
        Assert.Equal(Now.AddDays(8), expiring.UpdatedAt);

        var withdrawn = Propose();
        withdrawn.Withdraw(Now.AddMinutes(4));
        Assert.Equal(Now.AddMinutes(4), withdrawn.UpdatedAt);

        var applied = Propose();
        applied.AppliedByTheAgent(new AppliedChanges(Guid.CreateVersion7(), new Dictionary<string, Guid>()), "k1", Now.AddMinutes(5));
        Assert.Equal(Now.AddMinutes(5), applied.UpdatedAt);
    }

    // DA-043 item 7: a key answers what it answered the first time, even after the person decided.
    [Fact]
    public void A_key_answers_what_it_answered_the_first_time()
    {
        var applied = Propose();
        applied.AppliedByTheAgent(AppliedChanges.None, "k1", Now);

        var leftToThePerson = Propose();
        leftToThePerson.LeaveToThePerson([ProposalReason.Window], "k2", Now);
        leftToThePerson.ApprovedByThePerson(new AppliedChanges(Guid.CreateVersion7(), new Dictionary<string, Guid>()), [], Now.AddMinutes(1));

        Assert.Equal(KeyedAnswer.Applied, applied.AnswerTo("k1"));
        Assert.Equal(KeyedAnswer.None, applied.AnswerTo("other"));
        Assert.Equal(KeyedAnswer.Pending, leftToThePerson.AnswerTo("k2"));
        Assert.Equal("k2", leftToThePerson.IdempotencyKey);
        Assert.True(leftToThePerson.RequiresUser);
        Assert.Equal(KeyedAnswer.None, Propose().AnswerTo("k1"));
    }

    // GEN-021: the decision time as of now follows the status as of now.
    [Fact]
    public void An_expired_proposal_was_decided_when_it_expired_even_before_its_job_runs()
    {
        var proposal = Propose();
        var withdrawn = Propose();
        withdrawn.Withdraw(Now.AddMinutes(1));

        Assert.Null(proposal.DecidedAtAsOf(Now));
        Assert.Equal(proposal.ExpiresAt, proposal.DecidedAtAsOf(proposal.ExpiresAt.AddDays(1)));
        Assert.Equal(Now.AddMinutes(1), withdrawn.DecidedAtAsOf(Now.AddDays(30)));
    }

    // GEN-021: the query predicates say what StatusAt says.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void The_pending_and_expired_queries_agree_with_the_status_as_of_now(int secondsFromExpiry)
    {
        var proposal = Propose();
        var at = proposal.ExpiresAt.AddSeconds(secondsFromExpiry);

        Assert.Equal(proposal.IsPendingAt(at), ChangeProposal.PendingAt(at).Compile()(proposal));
        Assert.Equal(proposal.StatusAt(at) == ProposalStatus.Expired, ChangeProposal.ExpiredAt(at).Compile()(proposal));
    }

    private static ChangeProposal Propose() => ChangeProposal.Propose(
        new ProposalDraft(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "[{\"op\":\"create_node\",\"title\":\"x\"}]",
            3,
            "hash",
            RequiresUser: false,
            [ProposalReason.Suggested],
            [],
            "chat",
            TimeSpan.FromDays(7)),
        Now);
}

/// <summary>The field-by-field diff (v2 §65–66).</summary>
public sealed class FieldDiffsTests
{
    [Fact]
    public void Each_property_value_is_a_field_and_the_version_is_not()
    {
        var property = Guid.CreateVersion7();
        var before = FieldDiffs.Flatten($$$"""{"title":"A","body":"","version":1,"properties":{"{{{property}}}":1}}""");
        var after = FieldDiffs.Flatten("""{"title":"B","body":"","version":2,"properties":{}}""");

        var changed = FieldDiffs.Changed(before, after);

        Assert.Equal(["properties." + property, "title"], changed.Select(change => change.Field));
        Assert.Null(changed[0].After);
    }
}

/// <summary>"Criação própria do agente" (DA-133): monotonic.</summary>
public sealed class AgentOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Another_principals_write_makes_the_node_the_persons_for_good()
    {
        var agent = Guid.CreateVersion7();
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), "Note", CreatedByAgentIdentityId: agent), Now).Value!;

        node.WrittenBy(agent);
        Assert.True(AgentWritePolicy.IsAgentOwned(node.CreatedByAgentIdentityId, agent));

        node.WrittenBy(writerAgentIdentityId: null);
        node.WrittenBy(agent);
        Assert.False(AgentWritePolicy.IsAgentOwned(node.CreatedByAgentIdentityId, agent));
    }

    [Fact]
    public void Another_agents_write_also_ends_the_ownership_of_a_relation()
    {
        var agent = Guid.CreateVersion7();
        var relation = Relation.AssertHard(
            new RelationDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "about", RelationOrigin.Agent, agent),
            Now).Value!;

        relation.WrittenBy(Guid.CreateVersion7());

        Assert.False(AgentWritePolicy.IsAgentOwned(relation.CreatedByAgentIdentityId, agent));
    }

    // DA-049: the hint is only for a Node without Type, and is bounded.
    [Fact]
    public void A_suggested_type_is_kept_only_without_a_type_and_is_bounded()
    {
        var account = Guid.CreateVersion7();
        var book = NodeType.Create(Guid.CreateVersion7(), account, "Book", [], Now).Value!;
        var untyped = Node.Create(new NodeDraft(Guid.CreateVersion7(), account, "Note", SuggestedType: " Person "), Now).Value!;
        var typed = Node.Create(new NodeDraft(Guid.CreateVersion7(), account, "Note", Schema: new TypeSchema(book, new Dictionary<Guid, PropertyDefinition>()), SuggestedType: "Person"), Now).Value!;
        var tooLong = Node.Create(new NodeDraft(Guid.CreateVersion7(), account, "Note", SuggestedType: new string('x', GraphLimits.NameMaxLength + 1)), Now);

        Assert.Equal("Person", untyped.SuggestedType);
        Assert.Null(typed.SuggestedType);
        Assert.False(tooLong.IsSuccess);
    }

    // DA-049: a stored hint means nothing once the Node has a Type (an edit gave it one).
    [Fact]
    public void A_suggested_type_is_shown_only_while_the_node_has_no_type()
    {
        Assert.Equal("Person", Node.SuggestedTypeOf(typeId: null, "Person"));
        Assert.Null(Node.SuggestedTypeOf(Guid.CreateVersion7(), "Person"));
        Assert.Null(Node.SuggestedTypeOf(typeId: null, suggestedType: null));
    }
}
