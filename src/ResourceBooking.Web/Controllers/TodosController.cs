using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/todos")]
[Produces("application/json")]
[Authorize]
public class TodosController(ITodoService todoService) : ControllerBase
{
    // Omit isDone for all items; use true for completed or false for incomplete.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TodoResponseDto>>> GetTodos([FromQuery] bool? isDone = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return Ok(await todoService.GetUserTodosAsync(userId, isDone));
    }

    [HttpPost]
    public async Task<ActionResult<TodoResponseDto>> Create(TodoCreateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        //var userId = User.;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var item = await todoService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetTodos), item);
    }

    [HttpPatch("{id:int}/done")]
    public async Task<IActionResult> MarkDone(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await todoService.MarkDoneAsync(id, userId)
            ? NoContent() : NotFound(new { message = "Todo item not found." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await todoService.DeleteAsync(id, userId)
            ? NoContent() : NotFound(new { message = "Todo item not found." });
    }

    [HttpDelete("deleteAllDone")]
    public async Task<IActionResult> DeleteAllDone()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var todos = await todoService.GetUserTodosAsync(userId, true);
        if (todos == null) return NoContent();
        foreach (var todo in todos)
        {
            await todoService.DeleteAsync(todo.Id, userId);
        }
        return NoContent();
    }
}
