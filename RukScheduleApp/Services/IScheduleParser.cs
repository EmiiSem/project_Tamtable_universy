using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    public interface IScheduleParser
    {
        Task<List<string>> GetBranchesAsync();
        Task<List<string>> GetTeachersAsync(string branchName);
        Task<List<ScheduleItem>> GetScheduleAsync(string teacherName, DateTime date);
    }
}
