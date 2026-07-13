using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IntegrationDbContext _db;
    public UserRepository(IntegrationDbContext db) => _db = db;

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _db.Users.FirstOrDefaultAsync(x => x.Username == username, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<User>> GetAllAsync(CancellationToken ct = default)
        => _db.Users.OrderBy(x => x.Username).ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _db.Users.AddAsync(user, ct);

    public void Update(User user) => _db.Users.Update(user);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
