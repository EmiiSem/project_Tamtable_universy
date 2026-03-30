using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    public interface IDatabaseService
    {
        Task CacheScheduleAsync(List<ScheduleItem> items);
        Task<List<ScheduleItem>> GetCachedScheduleAsync(DateTime startDate, DateTime endDate);
        Task ClearCacheAsync();
    }
}
