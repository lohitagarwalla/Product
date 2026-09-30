using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Repositories;

public class GenericRepository<T> : IGenericRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext _context;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<T?> GetByIdAsync(int id) => await _context.Set<T>().FindAsync(id);

    public async Task<IReadOnlyList<T>> GetAllAsync() =>
        await _context.Set<T>().AsNoTracking().ToListAsync();

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        await _context.Set<T>().Where(predicate).AsNoTracking().ToListAsync();

    public async Task AddAsync(T entity) => await _context.Set<T>().AddAsync(entity);

    public void Update(T entity) => _context.Set<T>().Update(entity);

    // DbContext soft-deletes ISoftDeletable entities; other entities are permanently removed.
    public void Delete(T entity) => _context.Set<T>().Remove(entity);

    public async Task<bool> SaveChangesAsync() => await _context.SaveChangesAsync() > 0;
}
