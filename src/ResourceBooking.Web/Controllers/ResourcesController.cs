using ResourceBooking.Core.Constants;
using System.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class ResourcesController : ControllerBase
{
    private readonly IGenericRepository<Resource> _resourceRepository;
    private readonly IOutputCacheStore _cacheStore;

    public ResourcesController(
        IGenericRepository<Resource> resourceRepository,
        IOutputCacheStore cacheStore)
    {
        _resourceRepository = resourceRepository;
        _cacheStore = cacheStore;
    }

    /// <summary>
    /// Retrieve active resources with Output Caching enabled.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = "ResourceCatalogCache")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<ResourceResponseDto>))]
    public async Task<ActionResult<IEnumerable<ResourceResponseDto>>> GetAll()
    {
        var resources = await _resourceRepository.FindAsync(r => r.IsActive);
        var response = resources.Select(r => new ResourceResponseDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            Type = r.Type,
            Capacity = r.Capacity,
            Location = r.Location,
            IsActive = r.IsActive
        });

        return Ok(response);
    }

    /// <summary>
    /// Create a new resource and invalidate the 'resources' cache tag.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ResourceResponseDto))]
    public async Task<ActionResult<ResourceResponseDto>> Create(
        [FromBody] ResourceCreateDto dto,
        CancellationToken cancellationToken)
    {
        var resource = new Resource
        {
            Name = dto.Name,
            Description = dto.Description,
            Type = dto.Type,
            Capacity = dto.Capacity,
            Location = dto.Location,
            IsActive = true
        };

        await _resourceRepository.AddAsync(resource);
        await _resourceRepository.SaveChangesAsync();

        // Evict cached resource list so users immediately see newly added items
        await _cacheStore.EvictByTagAsync("resources", cancellationToken);

        var response = new ResourceResponseDto
        {
            Id = resource.Id,
            Name = resource.Name,
            Description = resource.Description,
            Type = resource.Type,
            Capacity = resource.Capacity,
            Location = resource.Location,
            IsActive = resource.IsActive
        };

        return CreatedAtAction(nameof(GetById), new { id = resource.Id }, response);
    }

    /// <summary>
    /// Soft delete resource and invalidate cache.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken cancellationToken)
    {
        var resource = await _resourceRepository.GetByIdAsync(id);
        if (resource == null) return NotFound();

        _resourceRepository.Delete(resource);
        await _resourceRepository.SaveChangesAsync();

        // Purge cached catalog tag
        await _cacheStore.EvictByTagAsync("resources", cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ResourceResponseDto>> GetById(int id)
    {
        var resource = await _resourceRepository.GetByIdAsync(id);
        if (resource == null || !resource.IsActive) return NotFound();

        return Ok(new ResourceResponseDto
        {
            Id = resource.Id,
            Name = resource.Name,
            Description = resource.Description,
            Type = resource.Type,
            Capacity = resource.Capacity,
            Location = resource.Location,
            IsActive = resource.IsActive
        });
    }
}
