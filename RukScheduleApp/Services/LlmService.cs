using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    public class LlmService : ILlmService
    {
        private readonly IDatabaseService _databaseService;

        public LlmService(IDatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task<string> AskAboutScheduleAsync(string question, string groupName)
        {
            // Получение расписания из кэша
            var schedule = await _databaseService.GetCachedScheduleAsync(
                DateTime.Today,
                DateTime.Today.AddDays(7));

            // Формирование контекста
            var context = BuildScheduleContext(schedule, groupName);

            if (string.IsNullOrWhiteSpace(context))
            {
                return "По выбранной группе в ближайшие 7 дней нет данных в локальном кеше.";
            }

            return $"Вопрос: {question}\n\nНайденные данные в кеше:\n{context}";
        }

        private string BuildScheduleContext(List<ScheduleItem> schedule, string groupName)
        {
            var context = new System.Text.StringBuilder();
            var filteredSchedule = schedule.Where(x => x.GroupName == groupName).ToList();

            foreach (var item in filteredSchedule)
            {
                context.AppendLine($"{item.Date:dd.MM.yyyy} {item.DayOfWeek}:");
                context.AppendLine($"  {item.Time} - {item.Subject} ({item.Room}, {item.Teacher})");
            }

            return context.ToString();
        }
    }
}