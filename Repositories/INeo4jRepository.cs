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
}
