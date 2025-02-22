using System.Collections.Generic;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Sciencetopia.Data;
// using Dapper;

public interface ISqlRepository
{
    /// <summary>
    /// 根据节点 id 列表获取完整的节点详情（例如 Name 和 Description）、创建时间和更新时间
    /// 根据标签 id 列表获取代表节点的完整详情
    /// 创建、更新和删除节点
    /// </summary>
    Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetNodesDetails(IEnumerable<string> ids);
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

    // 实现 ISqlRepository 接口中的方法
    // 获取节点Id相应的节点详情
    public Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetNodesDetails(IEnumerable<string> ids)
    {
        var nodes = _context.KnowledgeNodes
                            .Where(node => ids.Contains(node.Id))
                            .Select(node => new { node.Id, node.Name, node.Description, node.CreatedDate, node.UpdatedDate })
                            .AsEnumerable()
                            .ToList();

        var dict = new Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)>();
        foreach (var node in nodes)
        {
            if (node.Id != null)
            {
                dict[node.Id] = (node.Name ?? string.Empty, node.Description ?? string.Empty, node.CreatedDate, node.UpdatedDate);
            }
        }
        return dict;
    }

    // 获取标签Id相应的标签的代表节点
    public Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds)
    {
        var tagNameDict = _context.Tags
                                .Where(tag => tagIds.Contains(tag.Id) && tag.Id != null)
                                .ToDictionary(tag => tag.Id!, tag => tag.Name);

        var nodes = _context.KnowledgeNodes
                    .Where(node => tagNameDict.Values.Contains(node.Name))
                    .ToList();

        var dict = new Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)>();
        foreach (var node in nodes)
        {
            if (node.Id != null)
            {
                dict[node.Id] = (node.Name ?? string.Empty, node.Description ?? string.Empty, node.CreatedDate, node.UpdatedDate);
            }
        }
        return dict;
    }

    // 创建知识节点
    public Dictionary<string, object> CreateNode(string id, string name, string description)
    {
        var node = new KnowledgeNode { Id = id, Name = name, Description = description, CreatedDate = DateTime.Now, UpdatedDate = DateTime.Now };
        _context.KnowledgeNodes.Add(node);
        _context.SaveChanges();
        return new Dictionary<string, object> { { "Id", node.Id }, { "Name", node.Name }, { "Description", node.Description }, { "CreatedDate", node.CreatedDate }, { "UpdatedDate", node.UpdatedDate } };
    }

    // 更新知识节点
    public bool UpdateNode(string id, string name, string description)
    {
        var node = _context.KnowledgeNodes.Find(id);
        if (node == null) return false;
        node.Name = name;
        node.Description = description;
        node.UpdatedDate = DateTime.Now;
        _context.SaveChanges();
        return true;
    }

    // 删除知识节点
    public bool DeleteNode(string id)
    {
        var node = _context.KnowledgeNodes.Find(id);
        if (node == null) return false;
        _context.KnowledgeNodes.Remove(node);
        _context.SaveChanges();
        return true;
    }
}
