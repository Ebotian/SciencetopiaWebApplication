using Neo4j.Driver;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public interface IGraphRepository
{
    /// <summary>
    /// 获取所有知识节点与纯标签节点之间的 TAGGED_WITH 关系
    /// </summary>
    Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync();

    /// <summary>
    /// 获取知识节点之间的层次关系（知识层次关系），
    /// 即标签知识节点之间按照标签体系建立的父→子关系
    /// </summary>
    Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync();

    /// <summary>
    /// 利用纯标签节点和 TAGGED_WITH 关系进行二部图投影，
    /// 获取共享同一标签的知识节点之间的投影边，累加权重
    /// </summary>
    Task<List<ProjectedRelationDTO>> GetProjectedRelationsAsync();

    /// <summary>
    /// 获取知识节点与标签层次节点之间的关系
    /// </summary>
    Task<Dictionary<string, string>> GetNodeTagLevelRelationsAsync(IEnumerable<string> nodeIds);

    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<IEnumerable<string>> GetAllNodesRelatedToTagsAsync(IEnumerable<string> tagIds);

    /// <summary>
    /// 根据标签类型获取标签节点的 Id
    /// </summary>
    Task<IEnumerable<string>> GetTagNodeIdsByLabelAsync(string label);
}

public class GraphRepository : IGraphRepository
{
    private readonly IDriver _driver;

    public GraphRepository(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
            MATCH (t:Tags)-[:TAGGED_WITH]->(n:KnowledgeNode)
            RETURN n.id AS SourceId, t.id AS TagId
        ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new TaggedRelationDTO
            {
                SourceId = record["SourceId"].As<string>(),
                TagId = record["TagId"].As<string>()
            }).ToList();
    }

    // Repositories/GraphRepository.cs
    public async Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (parent:Tags)-[:CONTAIN]->(child:Tags)
        RETURN parent.id AS ParentTagId, child.id AS ChildTagId
    ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new TagContainRelationDTO
            {
                ParentTagId = record["ParentTagId"].As<string>(),
                ChildTagId = record["ChildTagId"].As<string>()
            }).ToList();
    }

    public async Task<List<ProjectedRelationDTO>> GetProjectedRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        // 基于纯标签节点和 TAGGED_WITH 关系投影出知识节点间共享的关系
        var cypher = @"
            MATCH (n:KnowledgeNode)<-[:TAGGED_WITH]-(t:Tags)-[:TAGGED_WITH]->(m:KnowledgeNode)
            WHERE n <> m
            WITH n, m, count(*) AS weight
            RETURN n.id AS SourceId, m.id AS TargetId, weight
        ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new ProjectedRelationDTO
            {
                SourceId = record["SourceId"].As<string>(),
                TargetId = record["TargetId"].As<string>(),
                Weight = record["weight"].As<int>()
            }).ToList();
    }

    public async Task<Dictionary<string, string>> GetNodeTagLevelRelationsAsync(IEnumerable<string> nodeIds)
    {
        using var session = _driver.AsyncSession();

        var query = @"
        MATCH (tagLevel:TagLevel)-[:TAGGED_WITH]->(node:KnowledgeNode)
        WHERE toLower(node.id) IN $nodeIds
        RETURN node.id AS NodeId, tagLevel.id AS TagLevelId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToLower()) }
        };

        var result = await session.RunAsync(query, parameters);

        var nodeTagLevels = new Dictionary<string, string>();

        await result.ForEachAsync(record =>
        {
            var nodeId = record["NodeId"].As<string>();
            var tagLevelId = record["TagLevelId"].As<string>();
            nodeTagLevels[nodeId] = tagLevelId;
        });
        
        return nodeTagLevels;
    }

    public async Task<IEnumerable<string>> GetAllNodesRelatedToTagsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tags)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE t.id IN $tagIds
        RETURN n.id AS NodeId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "tagIds", tagIds }
        };

        var result = await session.RunAsync(query, parameters);

        return await result.ToListAsync(record => record["NodeId"].As<string>());
    }

    public async Task<IEnumerable<string>> GetTagNodeIdsByLabelAsync(string label)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tags)
        WHERE t.label = $label
        RETURN t.id AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "label", label }
        };

        var result = await session.RunAsync(query, parameters);

        return await result.ToListAsync(record => record["TagId"].As<string>());
    }
}
