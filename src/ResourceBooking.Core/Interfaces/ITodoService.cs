using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Core.Interfaces;

public interface ITodoService
{
    Task<IReadOnlyList<TodoResponseDto>> GetUserTodosAsync(string userId, bool? isDone = null);
    Task<TodoResponseDto> CreateAsync(TodoCreateDto dto, string userId);
    Task<bool> MarkDoneAsync(int id, string userId);
    Task<bool> DeleteAsync(int id, string userId);
}
