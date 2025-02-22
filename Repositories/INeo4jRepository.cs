using Neo4j.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface INeo4jRepository
{
    /// <summary>
    /// 运行任意 Cypher 查询
    /// </summary>
    Task<IResultCursor> RunQueryAsync(string cypher, object parameters = null);

    /// <summary>
    /// 构造虚拟边（仅使用节点存储的 id），逻辑如下：
    /// 1. 对于同一标签组：
    ///    匹配 (k:Keyword)-[:HAS_TAG]->(t:Tags)，
    ///    由于 Neo4j 中只存储 id，代表节点的判断需要借助 SQL 补全后的信息，
    ///    这里只返回该标签组内所有节点 id 与标签 id 的对应关系；
    /// 2. 父子标签关系：匹配 childTag 具有 parent 属性的情况，返回子标签对应的所有 Keyword 节点 id 以及父标签 id。
    /// </summary>
    Task<List<IRecord>> FetchVirtualEdgesAsync();
    Task<List<IRecord>> CreateNodesAsync(string nodeId);
    Task<List<IRecord>> AddTagToNodeAsync(string nodeId, string tagId);
    Task<List<IRecord>> RemoveTagFromNodeAsync(string nodeId, string tagId);
    Task<List<IRecord>> DeleteNodeAsync(string nodeId);
    Task<List<IRecord>> DeleteTagAsync(string tagId);
    Task<List<IRecord>> CreateTagAsync(string tagId);
}

public class Neo4jRepository : INeo4jRepository
{
    private readonly IDriver _driver;

    public Neo4jRepository(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<IResultCursor> RunQueryAsync(string cypher, object parameters = null)
    {
        using var session = _driver.AsyncSession();
        return await session.RunAsync(cypher, parameters);
    }

    // 实现 INeo4jRepository 接口中的方法
    // 获取虚拟边
    public async Task<List<IRecord>> FetchVirtualEdgesAsync()
    {
        using var session = _driver.AsyncSession();

        // 由于 Neo4j 中的 Keyword 和 Tags 节点只存储 id，
        // 这里我们只构造出基于标签关联的虚拟边，返回的每条记录中包含：
        // sourceNode.id、targetNode.id 以及关联标签的 id（tagId）。
        //
        // Part 1：同一标签组内：依然采用“代表节点”逻辑，
        //         但由于 Neo4j 中没有 name 属性，这里的代表节点标识暂时通过后续 SQL 补全后比对。
        //         此处我们返回同组中所有 Keyword 节点与 Tags 节点的对应关系。
        //
        // Part 2：父子标签关系：返回子标签节点与其父标签关联的 Keyword 节点关系。
        //
        // 注意：由于代表节点的判断依赖 name 信息，因此实际在 Service 层会利用 SQL 补全后，
        // 根据 SQL 中查到的 Name 与标签对应的 Name（存储在 SQL 中的 Tags 数据）判断代表性，
        // 这里仅返回节点的 id 信息。
        var cypherQuery = @"
            // Part 1: 同一标签组内，将所有 (k:Keyword)-[:HAS_TAG]->(t:Tags) 的关系返回，
            // 后续在 Service 层根据 SQL 补全的节点详情来判断代表节点
            MATCH (k:Keyword)-[:HAS_TAG]->(t:Tags)
            RETURN k.id AS sourceId, t.id AS tagId
            UNION
            // Part 2: 父子标签关系：返回子标签对应的 Keyword 节点以及其父标签 id
            MATCH (child:Keyword)-[:HAS_TAG]->(childTag:Tags)
            WHERE EXISTS {
            MATCH (parentTag:Tags)-[:CONTAIN]->(childTag:Tags)
            }
            RETURN child.id AS sourceId, parentTag.id AS tagId
            LIMIT 1000
        ";

        var result = await session.RunAsync(cypherQuery);
        return await result.ToListAsync();
    }

    // 创建节点
    public async Task<List<IRecord>> CreateNodesAsync(string nodeId)
    {
        using var session = _driver.AsyncSession();

        // 创建节点
        var cypherQuery = @"
            CREATE (k:Keyword { id: $nodeId })
            RETURN k.id AS nodeId
        ";

        var result = await session.RunAsync(cypherQuery, new { nodeId });
        return await result.ToListAsync();
    }

    // 为节点添加标签
    public async Task<List<IRecord>> AddTagToNodeAsync(string nodeId, string tagId)
    {
        using var session = _driver.AsyncSession();

        // 为节点添加标签，如果节点不存在则创建，如果标签不存在则创建
        var cypherQuery = @"
            MERGE (k:Keyword { id: $nodeId })
            MERGE (t:Tags { id: $tagId })
            MERGE (k)-[:HAS_TAG]->(t)
            RETURN k.id AS nodeId, t.id AS tagId
        ";

        var result = await session.RunAsync(cypherQuery, new { nodeId, tagId });
        return await result.ToListAsync();
    }

    // 从节点中移除标签
    public async Task<List<IRecord>> RemoveTagFromNodeAsync(string nodeId, string tagId)
    {
        using var session = _driver.AsyncSession();

        // 从节点中移除标签
        var cypherQuery = @"
            MATCH (k:Keyword { id: $nodeId })-[r:HAS_TAG]->(t:Tags { id: $tagId })
            DELETE r
            RETURN k.id AS nodeId, t.id AS tagId
        ";

        var result = await session.RunAsync(cypherQuery, new { nodeId, tagId });
        return await result.ToListAsync();
    }

    // 删除节点
    public async Task<List<IRecord>> DeleteNodeAsync(string nodeId)
    {
        using var session = _driver.AsyncSession();

        // 删除节点
        var cypherQuery = @"
            MATCH (k:Keyword { id: $nodeId })
            DETACH DELETE k
            RETURN k.id AS nodeId
        ";

        var result = await session.RunAsync(cypherQuery, new { nodeId });
        return await result.ToListAsync();
    }

    // 删除标签
    public async Task<List<IRecord>> DeleteTagAsync(string tagId)
    {
        using var session = _driver.AsyncSession();

        // 删除标签
        var cypherQuery = @"
            MATCH (t:Tags { id: $tagId })
            DETACH DELETE t
            RETURN t.id AS tagId
        ";

        var result = await session.RunAsync(cypherQuery, new { tagId });
        return await result.ToListAsync();
    }

    // 创建标签
    public async Task<List<IRecord>> CreateTagAsync(string tagId)
    {
        using var session = _driver.AsyncSession();

        // 创建标签
        var cypherQuery = @"
            CREATE (t:Tags { id: $tagId })
            RETURN t.id AS tagId
        ";

        var result = await session.RunAsync(cypherQuery, new { tagId });
        return await result.ToListAsync();
    }

    // 根据标签 id 获取这些标签的并集的下游所有知识节点
    public async Task<List<IRecord>> GetNodesByTagsAsync(IEnumerable<string> tagIds)
    {
        var cypher = @"
        // 从输入的标签中收集所有起始标签
        MATCH (t:Tags)
        WHERE t.id IN $tagIds
        WITH collect(t) AS startTags
        // 递归查找所有 descendant 标签（包含起始标签本身，0..层级）
        CALL {
            WITH startTags
            UNWIND startTags AS t0
            MATCH (t0)-[:CONTAIN*0..]->(descendant:Tags)
            RETURN collect(DISTINCT descendant.id) AS descendantIds
        }
        // 找到与 descendant 标签关联的所有知识节点
        MATCH (n:Keyword)-[:HAS_TAG]->(tag:Tags)
        WHERE tag.id IN descendantIds
        RETURN DISTINCT n.id AS nodeId
    ";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { tagIds = tagIds.ToArray() });
        return await result.ToListAsync();
    }

    // 根据标签 id 获取这些标签的交集的下游所有知识节点
    public async Task<List<IRecord>> GetNodesIntersectionByTagsAsync(IEnumerable<string> tagIds)
    {
        var cypher = @"
        WITH $tagIds AS inputTagIds
        // 对每个输入标签，获取其所有后代标签（包括自身）
        UNWIND inputTagIds AS inputTagId
        CALL {
            WITH inputTagId
            MATCH (t:Tags {id: inputTagId})-[:CONTAIN*0..]->(descendant:Tags)
            RETURN collect(descendant.id) AS descendantIds
        }
        // 将每个输入标签对应的 descendantIds 收集成一个列表
        WITH collect(descendantIds) AS descendantSets
        // 匹配所有与某些标签关联的 Keyword 节点，并收集其关联的标签 id
        MATCH (n:Keyword)-[:HAS_TAG]->(t:Tags)
        WITH n, collect(DISTINCT t.id) AS nodeTagIds, descendantSets
        // 只有当对于 descendantSets 中的每个集合，都能在 nodeTagIds 中找到至少一个匹配项时，
        // 才返回该节点（即该节点与所有输入标签组都有关联）
        WHERE ALL(set IN descendantSets WHERE ANY(x IN set WHERE x IN nodeTagIds))
        RETURN DISTINCT n.id AS nodeId
        ";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { tagIds = tagIds.ToArray() });
        return await result.ToListAsync();
    }
}
