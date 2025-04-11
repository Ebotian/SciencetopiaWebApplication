using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.SearchEngine
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly IKnowledgeNodeRepository _knowledgeRepo;
        private readonly SearchService _searchService;

        public SearchController(
            IKnowledgeNodeRepository knowledgeRepo,
            SearchService searchService)
        {
            _knowledgeRepo = knowledgeRepo;
            _searchService = searchService;
        }

        [HttpGet("SearchKnowledgeBase")]
        public async Task<IActionResult> SearchKnowledgeBaseAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _knowledgeRepo.SearchKnowledgeNodesAsync(query, skip, pageSize);
            return Ok(result);
        }

        [HttpGet("SearchResources")]
        public async Task<IActionResult> SearchResourcesAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _searchService.SearchResourcesWithLinkedNodesAsync(query, skip, pageSize);
            return Ok(result);
        }
    }
}
