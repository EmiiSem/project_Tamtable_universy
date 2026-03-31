using RukScheduleApp.Data;
using RukScheduleApp.Models;
using Microsoft.EntityFrameworkCore;

namespace RukScheduleApp.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly ScheduleDbContext _context;

        public DatabaseService(ScheduleDbContext context)
        {
            _context = context;
        }

        public async Task CacheScheduleAsync(List<ScheduleItem> items)
        {
            foreach (var item in items)
            {
                var existing = await _context.ScheduleItems
                    .FirstOrDefaultAsync(x => x.Date == item.Date &&
                                             x.Time == item.Time &&
                                             x.GroupName == item.GroupName);

                if (existing != null)
                {
                    _context.ScheduleItems.Update(item);
                }
                else
                {
                    await _context.ScheduleItems.AddAsync(item);
                }
            }
            await _context.SaveChangesAsync();
        }

        public async Task<List<ScheduleItem>> GetCachedScheduleAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.ScheduleItems
                .Where(x => x.Date.Date >= startDate.Date && x.Date.Date <= endDate.Date)
                .ToListAsync();
        }

        public async Task ClearCacheAsync()
        {
            _context.ScheduleItems.RemoveRange(_context.ScheduleItems);
            await _context.SaveChangesAsync();
        }
    }
}