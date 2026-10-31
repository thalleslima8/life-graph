using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>
/// The Raiz da Conta (DA-189, DA-190, DA-203): a Node of its system Type with a neutral title,
/// renamable, never deleted alone, hidden from agents or given another Type, and the only Node
/// of its Type.
/// </summary>
public sealed class AccountRootTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private readonly TypeSchema _rootType = Schema(NodeType.CreateSystem(
        Guid.CreateVersion7(), Account, SystemOntology.AccountRootTypeKey, SystemOntology.AccountRootTypeName, [], Now));

    private readonly TypeSchema _book = Schema(NodeType.Create(Guid.CreateVersion7(), Account, "Book", [], Now).Value!);

    [Fact]
    public void The_root_takes_its_system_type_a_neutral_title_and_stays_out_of_the_inbox()
    {
        var root = Root();

        Assert.True(root.IsAccountRoot);
        Assert.Equal(SystemOntology.AccountRootTypeKey, root.SystemKey);
        Assert.Equal(_rootType.Type.Id, root.TypeId);
        Assert.Equal("Meu Life Graph", root.Title);
        Assert.False(root.IsInInbox);
        Assert.True(Node.IsAccountRootPredicate.Compile()(root));
        Assert.False(Node.IsNotAccountRootPredicate.Compile()(root));
    }

    [Fact]
    public void A_node_of_the_user_is_not_the_root()
    {
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Now).Value!;

        Assert.False(node.IsAccountRoot);
        Assert.Null(node.SystemKey);
        Assert.True(Node.IsNotAccountRootPredicate.Compile()(node));
        Assert.True(node.CheckDeletable().IsSuccess);
    }

    [Fact]
    public void The_root_is_renamed_and_its_body_edited()
    {
        var root = Root();

        var edited = root.Edit(new NodeEdit("Minha vida", "Tudo começa aqui.", _rootType, null), Later);

        Assert.True(edited.Value);
        Assert.Equal("Minha vida", root.Title);
        Assert.Equal("Tudo começa aqui.", root.Body);
    }

    [Fact]
    public void The_root_is_never_deleted_hidden_from_agents_nor_given_another_type()
    {
        var root = Root();

        Assert.Equal(GraphErrors.AccountRootLocked.Code, root.CheckDeletable().Error!.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, root.Edit(new NodeEdit(null, null, _rootType, null, HiddenFromAgents: true), Later).Error!.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, root.Edit(new NodeEdit(null, null, _book, null), Later).Error!.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, root.Edit(new NodeEdit(null, null, null, null), Later).Error!.Code);
        Assert.False(root.HiddenFromAgents);
        Assert.Equal(_rootType.Type.Id, root.TypeId);
        Assert.Equal(Node.FirstVersion, root.Version);
    }

    [Fact]
    public void No_other_node_takes_the_root_type()
    {
        var created = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Another root", Schema: _rootType), Now);
        var captured = Node.CaptureIntoInbox(new NodeDraft(Guid.CreateVersion7(), Account, "Another root", Schema: _rootType), Now);
        var node = Node.Create(new NodeDraft(Guid.CreateVersion7(), Account, "Diary"), Now).Value!;

        var retyped = node.Edit(new NodeEdit(null, null, _rootType, null), Later);

        Assert.Equal(GraphErrors.AccountRootLocked.Code, created.Error!.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, captured.Error!.Code);
        Assert.Equal(GraphErrors.AccountRootLocked.Code, retyped.Error!.Code);
        Assert.Null(node.TypeId);
    }

    [Fact]
    public void The_root_type_is_never_hidden_from_agents_and_its_name_is_reserved()
    {
        var hidden = _rootType.Type.SetHiddenFromAgents(true, Later);

        Assert.Equal(GraphErrors.AccountRootLocked.Code, hidden.Error!.Code);
        Assert.False(_rootType.Type.HiddenFromAgents);
        Assert.False(_rootType.Type.SetHiddenFromAgents(false, Later).Value);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, _rootType.Type.Rename("Home", Later).Error!.Code);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, _rootType.Type.CheckDeletable().Error!.Code);
        Assert.True(SystemOntology.IsReservedTypeName(" raiz da conta "));
        Assert.Equal(GraphErrors.NameReserved.Code, NodeType.Create(Guid.CreateVersion7(), Account, "RAIZ DA CONTA", [], Now).Error!.Code);
        Assert.Equal(GraphErrors.NameReserved.Code, _book.Type.Rename("Raiz da Conta", Later).Error!.Code);
    }

    private Node Root() =>
        Node.CreateAccountRoot(new NodeDraft(Guid.CreateVersion7(), Account, SystemOntology.AccountRootDefaultTitle, Schema: _rootType), Now).Value!;

    private static TypeSchema Schema(NodeType type) => new(type, new Dictionary<Guid, PropertyDefinition>());
}
