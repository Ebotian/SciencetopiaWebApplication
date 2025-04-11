using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Services;
using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;


namespace Sciencetopia.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KnowledgeGraphController : ControllerBase
    {
        private readonly IDriver _driver;
        private readonly ApplicationDbContext _context;
        private readonly KnowledgeGraphService _knowledgeGraphService;

        public KnowledgeGraphController(IDriver driver, ApplicationDbContext context, KnowledgeGraphService knowledgeGraphService)
        {
            // Initialize Neo4j driver
            _driver = driver;
            _context = context;
            _knowledgeGraphService = knowledgeGraphService;
        }

        /// <summary>
        /// 获取知识网络数据，用于前端可视化展示（支持多种视图类型）
        /// </summary>
        /// <param name="tagSystem">标签体系名称，默认为 "MainTag"</param>
        /// <param name="viewType">
        /// 可视化视图类型，支持以下取值：
        /// - "network"：默认值，返回节点-边形式的知识网络图数据（网状图）
        /// - "venn"：返回集合-子集结构的数据，适用于韦恩图展示（标签作为集合，节点作为元素）
        /// 未来可扩展更多视图类型，如："hierarchy"、"heatmap" 等
        /// </param>
        /// <returns>JSON 格式的图数据，用于前端渲染</returns>
        [HttpGet("GetNodes")]
        public async Task<IActionResult> GetKnowledgeGraph(
            [FromQuery] string tagSystem = "MainTag",
            [FromQuery] string viewType = "network")
        {
            string userId = User?.Identity?.IsAuthenticated == true
                ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty
                : string.Empty;

            var result = await _knowledgeGraphService.GetKnowledgeGraphAsync(tagSystem, viewType, userId);
            return Ok(result);
        }

        // [HttpGet("GetNodes")]
        // public async Task<IActionResult> GetKnowledgeGraph([FromQuery] string? tagSystem)
        // {
        //     // Determine if the user is authenticated
        //     string userId = User?.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty : string.Empty;
        //     // Get all Tag Ids related to Tag System
        //     var allTagIds = tagSystem != null ? await _knowledgeGraphService.GetTagIdsByTagTypeAsync(tagSystem) : await _knowledgeGraphService.GetTagIdsByTagTypeAsync("MainTag");
        //     // Get all node Ids related to Tag Ids 
        //     var allNodeIds = await _knowledgeGraphService.GetAllNodesRelatedToTags(allTagIds);
        //     // Get knowledge graph data from all node Ids
        //     var data = await _knowledgeGraphService.GetKnowledgeGraphDataByNodeId(allNodeIds, allTagIds);
        //     if (userId != string.Empty)
        //     {
        //         var data_pending = await _knowledgeGraphService.GetPendingNodesByUserIdAsync(userId);
        //         return Ok(new { data, data_pending });
        //     }
        //     return Ok(new { data });
        // }

        [HttpGet("FilterByTags")]
        public async Task<IActionResult> FilterByTags([FromQuery] List<string> tags, string? tagSystem)
        {
            if (tags == null || tags.Count == 0)
            {
                return BadRequest("At least one tag is required.");
            }

            try
            {
                var tagIds = await _knowledgeGraphService.GetTagIdsByTagNamesAsync(tags);
                var nodeIds = await _knowledgeGraphService.GetNodeIdsByTagsAsync(tagIds);
                var relatedTagIds = tagSystem != null ? await _knowledgeGraphService.GetTagIdsByTagTypeAmongNodesAsync(nodeIds, tagSystem) : await _knowledgeGraphService.GetTagIdsByTagTypeAmongNodesAsync(nodeIds, "MainTag");
                var data = await _knowledgeGraphService.GetKnowledgeGraphDataByNodeId(nodeIds, relatedTagIds);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("Search")]
        public async Task<IActionResult> SearchNode([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required.");
            }

            try
            {
                var result = await _knowledgeGraphService.SearchNodeAsync(query);
                if (result != null)
                {
                    return Ok(result);
                }

                return NotFound("No node found matching the query.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("CreateNode")]
        public async Task<IActionResult> CreateNode([FromBody] CreateNodeRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Node name is required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                var responseMessage = await _knowledgeGraphService.CreateNodeAsync(request, userId);
                return Ok(responseMessage);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("CreateRelationship")]
        public async Task<IActionResult> CreateRelationship([FromBody] CreateRelationshipRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.SourceNodeName) || string.IsNullOrWhiteSpace(request.TargetNodeName) || string.IsNullOrWhiteSpace(request.RelationshipType))
            {
                return BadRequest("Source node name, target node name, and relationship type are all required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.CreateRelationshipAsync(request.SourceNodeName, request.TargetNodeName, request.RelationshipType, userId);
                if (success)
                    return Ok();
                else
                    return NotFound("Source node or target node not found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ApproveNode")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> ApproveNode(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return BadRequest("Node name is required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.ApproveNodeAsync(nodeName);
                if (success)
                    return Ok("Node approval successful.");
                else
                    return NotFound("Node not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("RejectNode")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> DisapproveNode(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return BadRequest("Node name is required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.DisapproveNodeAsync(nodeName);
                if (success)
                    return Ok("Node disapproval successful.");
                else
                    return NotFound("Node not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ResubmitNode")]
        public async Task<IActionResult> ResubmitNode(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return BadRequest("Node name is required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.ResubmitNodeAsync(nodeName);
                if (success)
                    return Ok("Node resubmission successful.");
                else
                    return NotFound("Node not found or not marked as disapproved.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ApproveRelationship")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> ApproveRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            {
                return BadRequest("Source node name, target node name, and relationship type are all required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.ApproveRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
                if (success)
                    return Ok("Relationship approval successful.");
                else
                    return NotFound("Relationship not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("RejectRelationship")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> DisapproveRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            {
                return BadRequest("Source node name, target node name, and relationship type are all required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.DisapproveRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
                if (success)
                    return Ok("Relationship disapproval successful.");
                else
                    return NotFound("Relationship not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ResubmitRelationship")]
        public async Task<IActionResult> ResubmitRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            {
                return BadRequest("Source node name, target node name, and relationship type are all required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.ResubmitRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
                if (success)
                    return Ok("Relationship resubmission successful.");
                else
                    return NotFound("Relationship not found or not marked as disapproved.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("AddResource")]
        public async Task<IActionResult> AddResource([FromBody] AddResourceRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.NodeName) || string.IsNullOrWhiteSpace(request.Link))
            {
                return BadRequest("Node name and resource link are both required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.AddResourceAsync(request.NodeName, request.Link);
                if (success)
                    return Ok();
                else
                    return NotFound("Node not found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetPendingNodes")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> GetPendingNodes()
        {
            try
            {
                var data = await _knowledgeGraphService.GetPendingNodesAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("GetPendingNodeByUserId")]
        public async Task<IActionResult> GetPendingNodeByUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.GetPendingNodesByUserIdAsync(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetPendingTags")]
        public async Task<IActionResult> GetPendingTags()
        {
            try
            {
                var data = await _knowledgeGraphService.GetPendingTagsAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("GetPendingTagsByUserId")]
        public async Task<IActionResult> GetPendingTagsByUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.GetPendingTagsByUserIdAsync(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("CountContributedNodesAndLinksByUserId")]
        public async Task<IActionResult> CountContributedNodesAndLinks(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.CountContributedNodesAndLinks(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                // Log the detailed error
                Console.WriteLine($"Controller error in CountContributedNodesAndLinks: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}