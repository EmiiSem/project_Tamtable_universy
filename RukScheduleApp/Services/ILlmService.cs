using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    public interface ILlmService
    {
        /// <param name="groupName">Учебная группа для фильтра (не филиал). Для загрузки с сайта передавайте null.</param>
        /// <param name="scheduleContextOverride">Если задан — используется вместо кэша (например сразу после «Загрузить расписание»).</param>
        Task<string> AskAboutScheduleAsync(string question, string? groupName, IReadOnlyList<ScheduleItem>? scheduleContextOverride = null);
    }
}