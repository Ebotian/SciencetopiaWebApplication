using System.Collections;
using Neo4j.Driver;

public class KnowledgeGraphService
{
    private readonly IDriver _driver;
    private readonly IGraphRepository _graphRepository;
    private readonly ISqlRepository _sqlRepository;

    public KnowledgeGraphService(IDriver driver, IGraphRepository graphRepository, ISqlRepository sqlRepository)
    {
        _driver = driver;
        _graphRepository = graphRepository;
        _sqlRepository = sqlRepository;
    }

    public async Task<IEnumerable<string>> GetAllKnowledgeNodeIdsAsync()
    {
        // 从 SQL 获取所有节点 id
        return await _sqlRepository.GetAllNodeIdsAsync();
    }

    public async Task<IEnumerable<string>> GetTagIdsByTagTypeAsync(string tagType)
    {
        // 从 SQL 获取指定标签类型的节点 id
        return await _sqlRepository.GetTagNodeIdsByTagTypeAsync(tagType);
    }

    public async Task<IEnumerable<string>> GetTagIdsByTagTypeAmongNodesAsync(IEnumerable<string> nodeIds, string tagType)
    {
        // 从 SQL 获取指定节点 id 中有的指定类型的标签 id
        // 1. 从 SQL 查标签
        var tagIds = await _sqlRepository.GetTagNodeIdsByTagTypeAsync(tagType);
        if (!tagIds.Any())
        {
            // 没有任何符合条件的标签，直接返回空列表
            return new List<string>();
        }

        // 2. 去 Neo4j 查找 TAGGED_WITH 这些知识节点的标签
        var relatedTypeIds = await _graphRepository.GetAllTagsRelatedToNodesAsync(nodeIds);

        // 3. 返回两个结果的并集
        return tagIds.Intersect(relatedTypeIds);
    }

    public async Task<IEnumerable<string>> GetAllNodesRelatedToTags(IEnumerable<string> tagIds)
    {
        // 从 SQL 获取代表节点的完整详情
        return await _graphRepository.GetAllNodesRelatedToTagsAsync(tagIds);
    }

    public async Task<GraphDTO> GetKnowledgeGraphDataByNodeId(IEnumerable<string> allNodeIds, IEnumerable<string> allTagIds)
    {
        // 获取节点详情
        var nodesDict = await _sqlRepository.GetNodesDetailsAsync(allNodeIds);

        // 转换成 NodeDTO 列表
        var sqlNodes = nodesDict.Select(kvp => new NodeDTO
        {
            Id = kvp.Key.ToLower(),
            Name = kvp.Value.Name,
            Description = kvp.Value.Description,
            CreatedDate = kvp.Value.CreatedDate.UtcDateTime,
            UpdatedDate = kvp.Value.UpdatedDate.UtcDateTime,
            TagLevel = "" // 这里先置空，后续赋值
        }).ToList();

        // 从 SQL 获取所有 Tag 信息
        var sqlTags = await _sqlRepository.GetTagDetailsAsync(allTagIds);

        // 构造仅包含TagLevel节点的映射
        var tagLevelIds = await _graphRepository.GetNodeIdsByLabelAsync("TagLevel");

        var sqlTagLevels = await _sqlRepository.GetTagDetailsAsync(tagLevelIds);

        var tagLevelIdToName = sqlTagLevels
            .Where(t => tagLevelIds.Contains(t.Key))
            .ToDictionary(t => t.Key, t => t.Value.Name);

        // 从neo4j获取节点与TagLevel节点的关系 (节点Id, TagLevel对应的TagId)
        var nodeIdToTagLevelId = await _graphRepository.GetNodeTagLevelRelationsAsync(allNodeIds);

        // 更新节点的 TagLevel （默认为Keyword）
        foreach (var node in sqlNodes)
        {
            if (nodeIdToTagLevelId.TryGetValue(node.Id, out var tagLevelTagId) &&
                tagLevelIdToName.TryGetValue(tagLevelTagId, out var tagLevelName))
            {
                node.TagLevel = tagLevelName;
            }
            else
            {
                node.TagLevel = "Keyword";
            }
        }

        // 从 Neo4j 获取其它关系数据
        var taggedRelations = await _graphRepository.GetTaggedRelationsAsync();
        var projectedRelations = new List<ProjectedRelationDTO>(); // 假设这里返回的是空列表
        var tagContainRelations = await _graphRepository.GetPureTagContainRelationsAsync();

        // 构造层次关系：利用纯标签 CONTAIN 关系转换为知识节点间的层次关系
        var tagIdToName = sqlTags.ToDictionary(t => t.Key, t => t.Value.Name);

        var tagNameToNodeId = sqlNodes
            .Where(n => n.Name != null && tagIdToName.Values.Contains(n.Name))
            .GroupBy(n => n.Name)
            .ToDictionary(g => g.Key!, g => g.First().Id);

        var hierarchyRelations = new List<HierarchyRelationDTO>();
        foreach (var relation in tagContainRelations)
        {
            var parentTagName = sqlTags.FirstOrDefault(t => t.Key.ToString().Equals(relation.ParentTagId, StringComparison.OrdinalIgnoreCase)).Value.Name;
            var childTagName = sqlTags.FirstOrDefault(t => t.Key.ToString().Equals(relation.ChildTagId, StringComparison.OrdinalIgnoreCase)).Value.Name;

            if (!string.IsNullOrEmpty(parentTagName) && !string.IsNullOrEmpty(childTagName))
            {
                if (tagNameToNodeId.TryGetValue(parentTagName, out var parentNodeId) &&
                    tagNameToNodeId.TryGetValue(childTagName, out var childNodeId))
                {
                    hierarchyRelations.Add(new HierarchyRelationDTO
                    {
                        ParentId = parentNodeId,
                        ChildId = childNodeId
                    });
                }
            }
        }

        // 处理 TAGGED_WITH（taggedRelations）关系
        var topicNodesByName = sqlNodes
            .Where(n => n.TagLevel == "Topic" && !string.IsNullOrEmpty(n.Name))
            .GroupBy(n => n.Name)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var replacedTaggedRelations = new List<HierarchyRelationDTO>();
        foreach (var tagged in taggedRelations)
        {
            if (tagIdToName.TryGetValue(tagged.TagId, out var tagName))
            {
                if (topicNodesByName.TryGetValue(tagName, out var topicNodeId))
                {
                    replacedTaggedRelations.Add(new HierarchyRelationDTO
                    {
                        ParentId = topicNodeId,
                        ChildId = tagged.SourceId
                    });
                }
            }
        }

        // 整合所有关系为 links
        var linksDto = new List<LinkDTO>();
        linksDto.AddRange(hierarchyRelations.Select(r => new LinkDTO
        {
            Source = r.ParentId,
            Target = r.ChildId,
            Relation = "CONTAIN"
        }));
        linksDto.AddRange(projectedRelations.Select(r => new LinkDTO
        {
            Source = r.SourceId,
            Target = r.TargetId,
            Relation = "projected",
            Weight = r.Weight
        }));
        linksDto.AddRange(replacedTaggedRelations.Select(r => new LinkDTO
        {
            Source = r.ParentId,
            Target = r.ChildId,
            Relation = "CONTAIN"
        }));

        return new GraphDTO
        {
            Nodes = sqlNodes,
            Links = linksDto
        };
    }

    public async Task<IEnumerable<string>> GetTagIdsByTagNamesAsync(IEnumerable<string> inputTagNames)
    {
        var tags = await _sqlRepository.GetTagsByNameAsync(inputTagNames);
        var tagNameToId = tags.Where(t => t.Name != null).ToDictionary(t => t.Name!, t => t.Id.ToString());

        return inputTagNames.Select(name => tagNameToId.GetValueOrDefault(name, string.Empty));
    }

    public async Task<IEnumerable<string>> GetNodeIdsByTagsAsync(IEnumerable<string> inputTagIds)
    {

        HashSet<string>? intersectionDescendantTagIds = null;

        // **逐个查询每个标签的所有子标签 ID，并计算交集**
        foreach (var tagId in inputTagIds)
        {
            var descendantTagIds = await _graphRepository.GetAllDescendantTagIdsAsync(new List<string> { tagId });

            if (intersectionDescendantTagIds == null)
            {
                // 初始化交集集合
                intersectionDescendantTagIds = new HashSet<string>(descendantTagIds);
            }
            else
            {
                // 取交集
                intersectionDescendantTagIds.IntersectWith(descendantTagIds);
            }

            // 若交集为空，提前返回（没有共同的子标签）
            if (intersectionDescendantTagIds.Count == 0)
                return Enumerable.Empty<string>();
        }

        // **确保交集非空再查询 `TAGGED_WITH` 关系**
        if (intersectionDescendantTagIds != null && intersectionDescendantTagIds.Count > 0)
        {
            var nodeIds = await _graphRepository.GetAllNodesRelatedToTagsAsync(intersectionDescendantTagIds);
            return nodeIds;
        }

        return Enumerable.Empty<string>();
    }

    // public async Task<List<object>> FetchKnowledgeGraphData()
    // {
    //     using var session = _driver.AsyncSession();
    //     var result = await session.RunAsync(@"
    //     // Fetch knowledge nodes and their relationships
    //     MATCH (n)-[r]->(m)
    //     WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword) AND
    //           (m:Subject OR m:Field OR m:Topic OR m:Keyword)
    //     // Optionally match resources linked to these nodes
    //     OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(nr:Resource)
    //     OPTIONAL MATCH (m)-[:HAS_RESOURCE]->(mr:Resource)
    //     WITH n, m, r, COLLECT(DISTINCT nr.link) AS nResources, COLLECT(DISTINCT mr.link) AS mResources
    //     RETURN n AS sourceNode, m AS targetNode, r AS relationship, nResources, mResources
    //     LIMIT 1000
    // ");

    //     var data = new List<object>();

    //     await foreach (var record in result)
    //     {
    //         var sourceNode = record["sourceNode"].As<INode>();
    //         var targetNode = record["targetNode"].As<INode>();
    //         var relationship = record["relationship"].As<IRelationship>();
    //         var nResources = record["nResources"].As<List<string>>();
    //         var mResources = record["mResources"].As<List<string>>();

    //         data.Add(new
    //         {
    //             source = new
    //             {
    //                 Identity = sourceNode.Id,
    //                 Labels = sourceNode.Labels.ToList(),
    //                 Properties = new
    //                 {
    //                     Name = sourceNode.Properties["name"].As<string>(),
    //                     Description = sourceNode.Properties["description"].As<string>(),
    //                 },
    //                 Resources = nResources.Select(link => new { Link = link }).ToList()
    //             },
    //             target = new
    //             {
    //                 Identity = targetNode.Id,
    //                 Labels = targetNode.Labels.ToList(),
    //                 Properties = new
    //                 {
    //                     Name = targetNode.Properties["name"].As<string>(),
    //                     Description = targetNode.Properties["description"].As<string>(),
    //                 },
    //                 Resources = mResources.Select(link => new { Link = link }).ToList()
    //             },
    //             relationship = new
    //             {
    //                 Identity = relationship.Id,
    //                 Start = relationship.StartNodeId,
    //                 End = relationship.EndNodeId,
    //                 Type = relationship.Type
    //             }
    //         });
    //     }

    //     return data;
    // }

    public async Task<object> SearchNodeAsync(string query)
    {
        string lowerCaseQuery = query.ToLower();

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
        MATCH (n)
        WHERE (n:Subject OR n:Topic OR n:Keyword OR n:Tag) AND toLower(n.name) CONTAINS $lowerCaseQuery
        RETURN n, 
               CASE WHEN toLower(n.name) STARTS WITH $lowerCaseQuery THEN 1 ELSE 0 END AS startsWithScore,
               CASE WHEN toLower(n.name) = $lowerCaseQuery THEN 1 ELSE 0 END AS exactMatchScore
        ORDER BY exactMatchScore DESC, startsWithScore DESC, n.name
        LIMIT 1
    ", new { lowerCaseQuery });

        if (await result.FetchAsync())
        {
            var node = result.Current["n"].As<INode>();
            return new
            {
                Identity = node.Id,
                Labels = node.Labels.ToList(),
                Properties = new
                {
                    Link = node.Properties.GetValueOrDefault("link", null)?.As<string>(),
                    Name = node.Properties.GetValueOrDefault("name", null)?.As<string>(),
                    Description = node.Properties.GetValueOrDefault("description", null)?.As<string>()
                }
            };
        }

        return null; // Return null if no node is found
    }

    public async Task<string> CreateNodeAsync(CreateNodeRequest request, string userId)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();

        // Check if the node name already exists
        var nameExistsResult = await session.RunAsync(@"
            MATCH (n:{request.Label})
            WHERE toLower(n.name) = toApiModel{name.ToLower()})
            RETURN n",
            new { name = request.Name });

        if (await nameExistsResult.FetchAsync())
        {
            throw new InvalidOperationException("Node name already exists.");
        }

        var result = await session.RunAsync(@"
            CREATE (n:{request.Label} {name: $name, description: $description})
            SET n:pending_approval
            WITH n
            UNWIND $link AS l
            CREATE (r:Resource {link: l})
            CREATE (n)-[:HAS_RESOURCE]->(r)
            SET r:pending_approval
            WITH n
            MATCH (u:User {id: $userId})
            CREATE (u)-[:CREATED]->(n)
            RETURN n",
            new { name = request.Name, description = request.Description, link = request.Link, userId });

        return "Node created successfully.";
    }

    public async Task<bool> CreateRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType, string userId)
    {
        if (sourceNodeName == null || targetNodeName == null || relationshipType == null)
            throw new ArgumentNullException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();

        // Construct the query dynamically with the relationship type
        var query = $@"
        MATCH (source), (target)
        WHERE source.name = $sourceNodeName AND target.name = $targetNodeName
        CREATE (source)-[:{relationshipType} {{status: 'pending_approval', contributor: $userId}}]->(target)
        RETURN source, target";

        var result = await session.RunAsync(query, new { sourceNodeName, targetNodeName, userId });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ApproveNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.pending_approval)
            REMOVE n:pending_approval, r:pending_approval
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the node was found and updated
    }

    public async Task<bool> DisapproveNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.pending_approval)
            REMOVE n:pending_approval, r:pending_approval
            SET n:disapproved, r:disapproved
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ResubmitNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.disapproved)
            REMOVE n:disapproved, r:disapproved
            SET n:pending_approval, r:pending​​_approval
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ApproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
            REMOVE r.pending_approval
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> DisapproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
            REMOVE r.pending_approval
            SET r:disapproved
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ResubmitRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.disapproved)
            REMOVE r.disapproved
            SET r:pending_approval
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> AddResourceAsync(string nodeName, string link)
    {
        if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(link))
            throw new ArgumentException("Node name and link are required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)
            WHERE n.name = $nodeName
            CREATE (r:Resource {link: $link})
            CREATE (n)-[:HAS_RESOURCE]->(r)
            SET r:pending_approval
            RETURN n",
            new { nodeName, link });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<List<object>> GetPendingNodesAsync()
    {
        var data = new List<object>();
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (u:User)-[:CREATED]->(n:pending_approval)
            OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(r:Resource)
            RETURN n, r, u.id AS userId",
            new { });

        await foreach (var record in result)
        {
            var node = record["n"].As<INode>();
            var resource = record["r"].As<INode>();
            var userId = record["userId"].As<string>();

            data.Add(new
            {
                Node = new
                {
                    Identity = node.Id,
                    Labels = node.Labels.ToList(),
                    Properties = new
                    {
                        Name = node.Properties["name"].As<string>(),
                        Description = node.Properties["description"].As<string>()
                    },
                    Resource = resource != null ? new
                    {
                        Identity = resource.Id,
                        Labels = resource.Labels.ToList(),
                        Properties = new
                        {
                            Link = resource.Properties["link"].As<string>()
                        }
                    } : null
                },
                UserId = userId
            });
        }

        return data;
    }

    public async Task<List<object>> GetPendingNodesByUserIdAsync(string userId)
    {
        var data = new List<object>();
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (u:User {id: $userId})-[:CREATED]->(n:pending_approval)
            OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(r:Resource)
            RETURN n, r",
            new { userId });

        await foreach (var record in result)
        {
            var node = record["n"].As<INode>();
            var resource = record["r"].As<INode>();

            data.Add(new
            {
                Node = new
                {
                    Identity = node.Id,
                    Labels = node.Labels.ToList(),
                    Properties = new
                    {
                        Name = node.Properties["name"].As<string>(),
                        Description = node.Properties["description"].As<string>()
                    },
                    Resource = resource != null ? new
                    {
                        Identity = resource.Id,
                        Labels = resource.Labels.ToList(),
                        Properties = new
                        {
                            Link = resource.Properties["link"].As<string>()
                        }
                    } : null
                },
            });
        }
        return data;
    }

    public async Task<List<int>> CountContributedNodesAndLinks(string userId)
    {
        var data = new List<int>();

        try
        {
            using var session = _driver.AsyncSession();

            // Query to count nodes
            var resultNodesCursor = await session.RunAsync(@"
            MATCH (u:User {id: $userId})-[:CREATED]->(n)
            WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event)
            AND NOT (n:pending_approval)
            RETURN COUNT(n) AS nodeCount", new { userId });

            if (!await resultNodesCursor.FetchAsync())
            {
                throw new Exception("Failed to fetch node count.");
            }

            var nodeCountRecord = resultNodesCursor.Current;
            int nodeCount = nodeCountRecord["nodeCount"].As<int>();

            // Query to count links
            var resultLinksCursor = await session.RunAsync(@"
            MATCH (n)-[rel {userId: $userId}]->(m)
            WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event) AND
                  (m:Subject OR m:Field OR m:Topic OR m:Keyword OR m:People OR m:Works OR m:Event)
            RETURN COUNT(rel) AS linkCount", new { userId });

            if (!await resultLinksCursor.FetchAsync())
            {
                throw new Exception("Failed to fetch link count.");
            }

            var linkCountRecord = resultLinksCursor.Current;
            int linkCount = linkCountRecord["linkCount"].As<int>();

            data.Add(nodeCount);
            data.Add(linkCount);
        }
        catch (Exception ex)
        {
            // Log detailed error information
            Console.WriteLine($"Error in CountContributedNodesAndLinks: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            throw new Exception("An error occurred while counting contributed nodes and links", ex);
        }

        return data;
    }

}