using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public interface ITagRepository
{
    Task<IEnumerable<string>> GetTagNodeIdsByTagTypeAsync(string tagType);
    Task<List<Tags>> GetAllTagsAsync();
    Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames);
    Task<Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetTagDetailsAsync(IEnumerable<string> ids);
    Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds);
}

public class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;

    public TagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<string>> GetTagNodeIdsByTagTypeAsync(string tagType)
    {
        return await (from tag in _context.Tags
                      join tagTypeEntity in _context.TagTypes on tag.Id equals tagTypeEntity.TagId
                      join typeOfTag in _context.TypesOfTags on tagTypeEntity.TypeId equals typeOfTag.Id
                      where typeOfTag.Type == tagType
                      select tag)
                 .Select(tag => tag.Id.ToString()!.ToLower())
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
                                 CreatedDate = tag.CreatedDate.HasValue ? tag.CreatedDate.Value.UtcDateTime : default,
                                 UpdatedDate = tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value.UtcDateTime : default
                             })
                             .ToListAsync();
    }

    public async Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames)
    {
        if (inputTagNames == null || !inputTagNames.Any())
        {
            return new List<TagDTO>(); // 输入为空，返回空列表
        }

        // 使用 EF Core 查询匹配的标签
        var tags = await _context.Tags
            .Where(t => inputTagNames.Contains(t.Name))
            .Select(t => new TagDTO
            {
                Id = t.Id.ToString()!.ToLower(),
                Name = t.Name
            })
            .ToListAsync();

        return tags;
    }

    public async Task<Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetTagDetailsAsync(IEnumerable<string> ids)
    {
        var tags = await _context.Tags
                            .Where(tag => ids.Contains(tag.Id.ToString()!.ToLower()))
                            .Select(tag => new
                            {
                                Id = tag.Id.ToString()!.ToLower(),
                                tag.Name,
                                tag.Description,
                                // 将 DateTime 转换为 DateTimeOffset（假设这里的 DateTime 为本地时间，可以根据实际情况调整）
                                CreatedDate = tag.CreatedDate,
                                UpdatedDate = tag.UpdatedDate
                            })
                            .ToListAsync();

        var dict = new Dictionary<string, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        foreach (var tag in tags)
        {
            if (!string.IsNullOrEmpty(tag.Id))
            {
                dict[tag.Id] = (tag.Name ?? string.Empty, tag.Description ?? string.Empty, tag.CreatedDate.HasValue ? tag.CreatedDate.Value : default, tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value : default);
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
}