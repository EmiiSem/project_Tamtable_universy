using RukScheduleApp.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RukScheduleApp.Services
{
    public class LlmService : ILlmService
    {
        private readonly IDatabaseService _databaseService;
        private readonly IScheduleParser _parser;
        private readonly LlmApiService _llmApiService;

        public LlmService(IDatabaseService databaseService, IScheduleParser parser, LlmApiService llmApiService)
        {
            _databaseService = databaseService;
            _parser = parser;
            _llmApiService = llmApiService;
        }

        public async Task<string> AskAboutScheduleAsync(string question, string groupName)
        {
            try
            {
                // Лог в файл
                var logPath = Path.Combine(Path.GetTempPath(), "debug.log");
                System.IO.File.WriteAllText(logPath, "");

                System.IO.File.AppendAllText(logPath, $"DEBUG: SelectedBranch (groupName): {groupName ?? "null"}{Environment.NewLine}");

                // Получение расписания из кэша
                var schedule = await _databaseService.GetCachedScheduleAsync(
                    DateTime.Today,
                    DateTime.Today.AddDays(7));

                if (schedule.Count == 0)
                {
                    // Если к, можно попробовать получить напрямую через парсер
                    // Пока оставим пустым, если нужно — добавим логику
                    System.IO.File.AppendAllText(logPath, $"DEBUG: Кэш пуст, расписание не найдено.{Environment.NewLine}");
                }

                System.IO.File.AppendAllText(logPath, $"DEBUG: Всего записей в кэше: {schedule.Count}{Environment.NewLine}");

                // Определение даты
                var targetDate = ParseDateFromQuestion(question);
                System.IO.File.AppendAllText(logPath, $"DEBUG: Определённая дата: {targetDate?.ToString("dd.MM.yyyy") ?? "не распознана"}{Environment.NewLine}");

                List<ScheduleItem> filteredSchedule = null;
                if (targetDate.HasValue)
                {
                    filteredSchedule = schedule.Where(x => x.Date.Date == targetDate.Value.Date).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по дате: {filteredSchedule.Count}{Environment.NewLine}");
                }
                else
                {
                    filteredSchedule = schedule;
                }

                // Извлечение преподавателя
                var teacherName = ExtractTeacherNameFromQuestion(question);
                if (!string.IsNullOrEmpty(teacherName))
                {
                    filteredSchedule = filteredSchedule.Where(x => x.Teacher.Contains(teacherName, StringComparison.OrdinalIgnoreCase)).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по преподавателю '{teacherName}': {filteredSchedule.Count}{Environment.NewLine}");
                }

                // Извлечение аудитории
                var room = ExtractRoomFromQuestion(question);
                if (!string.IsNullOrEmpty(room))
                {
                    filteredSchedule = filteredSchedule.Where(x => x.Room.Contains(room, StringComparison.OrdinalIgnoreCase)).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по аудитории '{room}': {filteredSchedule.Count}{Environment.NewLine}");
                }

                // Извлечение типа занятия
                var lessonType = ExtractLessonTypeFromQuestion(question);
                if (!string.IsNullOrEmpty(lessonType))
                {
                    filteredSchedule = filteredSchedule.Where(x => x.Subject.ToLower().Contains(lessonType.ToLower())).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтра занятия '{lessonType}': {filteredSchedule.Count}{Environment.NewLine}");
                }

                // Фильтрация по группе (если указана)
                if (!string.IsNullOrEmpty(groupName))
                {
                    filteredSchedule = filteredSchedule.Where(x => x.GroupName == groupName).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по группе '{groupName}': {filteredSchedule.Count}{Environment.NewLine}");
                }

                System.IO.File.AppendAllText(logPath, $"DEBUG: Передаётся {filteredSchedule.Count} записей в LLM.{Environment.NewLine}");
                foreach (var item in filteredSchedule)
                {
                    System.IO.File.AppendAllText(logPath, $"  - {item.Date:dd.MM.yyyy} | {item.Teacher} | {item.Subject} | {item.Room}{Environment.NewLine}");
                }

                return await _llmApiService.GetAnswerAsync(question, filteredSchedule);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в AskAboutScheduleAsync: {ex.Message}");
                return "Произошла ошибка при обработке запроса.";
            }
        }

        private DateTime? ParseDateFromQuestion(string question)
        {
            var lower = question.ToLowerInvariant();

            // Сегодня
            if (lower.Contains("сегодня"))
            {
                return DateTime.Today;
            }

            // Завтра
            if (lower.Contains("завтра"))
            {
                return DateTime.Today.AddDays(1);
            }

            // Послезавтра
            if (lower.Contains("послезавтра"))
            {
                return DateTime.Today.AddDays(2);
            }

            // Попробуем распознать дату в формате "5 апреля", "12 марта", "1 января 2026"
            var culture = CultureInfo.GetCultureInfo("ru-RU");
            var dateFormats = new[]
            {
                "d MMMM",
                "d MMMM yyyy",
                "dd.MM.yyyy",
                "d.MM.yyyy",
                "yyyy-MM-dd"
            };

            foreach (var format in dateFormats)
            {
                if (DateTime.TryParseExact(lower, format, culture, DateTimeStyles.None, out var parsedDate))
                {
                    return parsedDate;
                }
            }

            // Попробуем распознать "апрель", "май" и т.д. — ближайший месяц
            var months = new Dictionary<string, int>
            {
                ["январь"] = 1, ["февраль"] = 2, ["март"] = 3, ["апрель"] = 4,
                ["май"] = 5, ["июнь"] = 6, ["июль"] = 7, ["август"] = 8,
                ["сентябрь"] = 9, ["октябрь"] = 10, ["ноябрь"] = 11, ["декабрь"] = 12
            };

            foreach (var month in months)
            {
                if (lower.Contains(month.Key))
                {
                    var year = DateTime.Today.Year;
                    var date = new DateTime(year, month.Value, 1);
                    if (date < DateTime.Today)
                        year++;
                    date = new DateTime(year, month.Value, 1);
                    return date; // Возвращает первый день месяца — можно доработать
                }
            }

            // Не распознали дату
            return null;
        }

        private string? ExtractTeacherNameFromQuestion(string question)
        {
            // Ищем ФИО по шаблону: Фамилия Имя Отчество
            // Пример: Иванов Андрей Геннадьевич
            var regex = new Regex(@"([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)", RegexOptions.IgnoreCase);
            var match = regex.Match(question);
            if (match.Success)
            {
                return match.Value.Trim();
            }

            return null;
        }

        private string? ExtractRoomFromQuestion(string question)
        {
            // Ищем аудиторию по шаблону: цифры и буквы (например, 426, 101а, 305-б)
            var regex = new Regex(@"(\d+[а-яё]?\-?[а-яё]?)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(question);
            foreach (Match match in matches)
            {
                var room = match.Value;
                if (room.Length >= 2) // например, 426
                {
                    return room;
                }
            }

            return null;
        }

        private string? ExtractLessonTypeFromQuestion(string question)
        {
            var lower = question.ToLowerInvariant();

            if (lower.Contains("лекция"))
                return "лекция";
            if (lower.Contains("практика") || lower.Contains("практические"))
                return "практические";
            if (lower.Contains("лабораторная"))
                return "лабораторная";

            return null;
        }
    }
}