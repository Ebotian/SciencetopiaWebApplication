using System.Collections.Generic;
using System.Linq;
using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public interface ISqlRepository
{
    /// <summary>
    /// 根据节点 id 列表获取完整的节点详情（例如 Name 和 Description）、创建时间和更新时间
    /// 根据标签 id 列表获取代表节点的完整详情
    /// 创建、更新和删除节点
    /// </summary>
    Task<IEnumerable<string>> GetAllNodeIdsAsync();
    Task<List<Tags>> GetAllTagsAsync();
    Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)> GetNodesDetails(IEnumerable<string> ids);
    Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds);
    Dictionary<string, object> CreateNode(string id, string name, string description);
    bool UpdateNode(string id, string name, string description);
    bool DeleteNode(string id);
}

public class SqlRepository : ISqlRepository
{
    private readonly ApplicationDbContext _context;

    public SqlRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // 获取所有节点Id，转换为字符串
    public async Task<IEnumerable<string>> GetAllNodeIdsAsync()
    {
        return await _context.KnowledgeNodes
                             .Where(node => node.Id != null)
                             .Select(node => node.Id.ToString()!)
                             .ToListAsync();
    }

    // 获取所有Tag信息，转换Tag.Id为字符串
    public async Task<List<Tags>> GetAllTagsAsync()
    {
        return await _context.Tags
                             .Select(tag => new Tags
                             {
                                 // 将 Guid 转换为字符串
                                 Id = tag.Id,
                                 Name = tag.Name,
                                 Description = tag.Description,
                                 TagLevel = tag.TagLevel,
                                 CreatedDate = tag.CreatedDate.HasValue ? tag.CreatedDate.Value.UtcDateTime : default,
                                 UpdatedDate = tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value.UtcDateTime : default
                             })
                             .ToListAsync();
    }

    // 获取节点详情，投影时将 Guid 转为字符串
    public Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)> GetNodesDetails(IEnumerable<string> ids)
    {
        var nodes = _context.KnowledgeNodes
                            .Where(node => ids.Contains(node.Id.ToString()))
                            .Select(node => new
                            {
                                Id = node.Id.ToString(),
                                node.Name,
                                node.Description,
                                // 将 DateTime 转换为 DateTimeOffset（假设这里的 DateTime 为本地时间，可以根据实际情况调整）
                                CreatedDate = node.CreatedDate,
                                UpdatedDate = node.UpdatedDate
                            })
                            .AsEnumerable()
                            .ToList();

        var dict = new Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Id))
            {
                dict[node.Id] = (node.Name ?? string.Empty, node.Description ?? string.Empty, node.CreatedDate.HasValue ? node.CreatedDate.Value : default, node.UpdatedDate.HasValue ? node.UpdatedDate.Value : default);
            }
        }
        return dict;
    }

    // 获取标签的代表节点；注意：此处 tagIds 为字符串，需要将 tag.Id 转换为字符串进行比较
    public Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds)
    {
        var tagNameDict = _context.Tags
                                .Where(tag => tag.Id != null && tagIds.Contains(tag.Id.ToString()))
                                .ToDictionary(tag => tag.Id.ToString()!, tag => tag.Name);

        var nodes = _context.KnowledgeNodes
                    .Where(node => tagNameDict.Values.Contains(node.Name))
                    .ToList();

        var dict = new Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)>();
        foreach (var node in nodes)
        {
            if (node.Id != null)
            {
                dict[node.Id.ToString()] = (node.Name ?? string.Empty, node.Description ?? string.Empty, node.CreatedDate.HasValue ? node.CreatedDate.Value.UtcDateTime : default, node.UpdatedDate.HasValue ? node.UpdatedDate.Value.UtcDateTime : default);
            }
        }
        return dict;
    }

    // 创建知识节点：将传入的 id 转换为 Guid
    public Dictionary<string, object> CreateNode(string id, string name, string description)
    {
        var node = new KnowledgeNode
        {
            Id = Guid.Parse(id),
            Name = name,
            Description = description,
            CreatedDate = DateTime.Now,
            UpdatedDate = DateTime.Now
        };
        _context.KnowledgeNodes.Add(node);
        _context.SaveChanges();
        return new Dictionary<string, object>
        {
            { "Id", node.Id.ToString() },
            { "Name", node.Name },
            { "Description", node.Description },
            { "CreatedDate", node.CreatedDate },
            { "UpdatedDate", node.UpdatedDate }
        };
    }

    // 更新知识节点：使用 Guid.Parse 查找
    public bool UpdateNode(string id, string name, string description)
    {
        var node = _context.KnowledgeNodes.Find(Guid.Parse(id));
        if (node == null) return false;
        node.Name = name;
        node.Description = description;
        node.UpdatedDate = DateTime.Now;
        _context.SaveChanges();
        return true;
    }

    // 删除知识节点：使用 Guid.Parse 查找
    public bool DeleteNode(string id)
    {
        var node = _context.KnowledgeNodes.Find(Guid.Parse(id));
        if (node == null) return false;
        _context.KnowledgeNodes.Remove(node);
        _context.SaveChanges();
        return true;
    }
}
