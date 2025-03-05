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
            MATCH (n:Keyword)-[:HAS_TAG]->(t:Tags)
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
            MATCH (n:Keyword)-[:HAS_TAG]->(t:Tags)<-[:HAS_TAG]-(m:Keyword)
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
}
