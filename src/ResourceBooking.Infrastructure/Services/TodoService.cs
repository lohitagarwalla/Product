using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Infrastructure.Services;

public class TodoService(IGenericRepository<TodoItem> repository) : ITodoService
{
    public async Task<IReadOnlyList<TodoResponseDto>> GetUserTodosAsync(string userId, bool? isDone = null)
    {
        var items = await repository.FindAsync(t => t.UserId == userId &&
            (!isDone.HasValue || t.IsDone == isDone.Value));
        return items.OrderBy(t => t.Id).Select(ToDto).ToList();
    }

    public async Task<TodoResponseDto> CreateAsync(TodoCreateDto dto, string userId)
    {
        var item = new TodoItem { Title = dto.Title.Trim(), UserId = userId };
        await repository.AddAsync(item);
        await repository.SaveChangesAsync();
        return ToDto(item);
    }

    public async Task<bool> MarkDoneAsync(int id, string userId)
    {
        var item = (await repository.FindAsync(t => t.Id == id && t.UserId == userId)).SingleOrDefault();
        if (item is null) return false;
        if (!item.IsDone)
        {
            item.IsDone = true;
            repository.Update(item);
            await repository.SaveChangesAsync();
        }
        return true;
    }

    public async Task<bool> DeleteAsync(int id, string userId)
    {
        var item = (await repository.FindAsync(t => t.Id == id && t.UserId == userId)).SingleOrDefault();
        if (item is null) return false;
        repository.Delete(item);
        await repository.SaveChangesAsync();
        return true;
    }

    private static TodoResponseDto ToDto(TodoItem item) => new()
    {
        Id = item.Id, Title = item.Title, IsDone = item.IsDone
    };
}
