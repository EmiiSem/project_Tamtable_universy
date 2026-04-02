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

                // Определяем нужный диапазон дат из вопроса, чтобы не ограничиваться "сегодня + 7 дней"
                var dateRange = ResolveDateRange(question);
                System.IO.File.AppendAllText(
                    logPath,
                    $"DEBUG: Диапазон запроса: {dateRange.Start:dd.MM.yyyy}..{dateRange.End:dd.MM.yyyy} (kind={dateRange.Kind}){Environment.NewLine}");

                // Получение расписания из кэша
                var schedule = await _databaseService.GetCachedScheduleAsync(dateRange.Start, dateRange.End);

                System.IO.File.AppendAllText(logPath, $"DEBUG: Всего записей в кэше: {schedule.Count}{Environment.NewLine}");

                // Фильтруем строго под выбранный диапазон (на случай, если в БД есть шире)
                var filteredSchedule = schedule
                    .Where(x => x.Date.Date >= dateRange.Start.Date && x.Date.Date <= dateRange.End.Date)
                    .ToList();
                System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по диапазону: {filteredSchedule.Count}{Environment.NewLine}");

                // Извлечение преподавателя
                var teacherName = ExtractTeacherNameFromQuestion(question);
                if (!string.IsNullOrEmpty(teacherName))
                {
                    filteredSchedule = filteredSchedule.Where(x => x.Teacher.Contains(teacherName, StringComparison.OrdinalIgnoreCase)).ToList();
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по преподавателю '{teacherName}': {filteredSchedule.Count}{Environment.NewLine}");
                }

                // Извлечение аудитории (не применять к сообщению, которое выглядит только как дата — иначе «03» из 03.04.2026 даёт ложный фильтр)
                var room = LooksLikeDateOnlyMessage(question) ? null : ExtractRoomFromQuestion(question);
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
                    System.IO.File.AppendAllText(logPath, $"DEBUG: После фильтрации по типу занятия '{lessonType}': {filteredSchedule.Count}{Environment.NewLine}");
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

                // return await _llmApiService.GetAnswerAsync(question, filteredSchedule);
                // Вместо оригинального вопроса
                var adjustedQuestion = BuildNormalizedQuestion(question, dateRange);

                return await _llmApiService.GetAnswerAsync(adjustedQuestion, filteredSchedule);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в AskAboutScheduleAsync: {ex.Message}");
                return "Произошла ошибка при обработке запроса.";
            }
        }

        private enum DateRangeKind
        {
            DefaultNext7Days,
            ExactDay,
            WeekRange
        }

        private readonly record struct DateRange(DateTime Start, DateTime End, DateRangeKind Kind);

        private DateRange ResolveDateRange(string question)
        {
            var lower = (question ?? string.Empty).ToLowerInvariant();

            // Неделя (приоритетнее единичной даты в тексте)
            if (lower.Contains("на этой неделе") || lower.Contains("пары на этой неделе") || lower.Contains("расписание на этой неделе"))
            {
                var start = StartOfWeek(DateTime.Today);
                var end = start.AddDays(6);
                return new DateRange(start, end, DateRangeKind.WeekRange);
            }

            // Явные относительные дни (важно: «послезавтра» раньше «завтра», иначе сработает подстрока «завтра»)
            if (lower.Contains("сегодня"))
                return new DateRange(DateTime.Today, DateTime.Today, DateRangeKind.ExactDay);
            if (lower.Contains("послезавтра"))
            {
                var d = DateTime.Today.AddDays(2);
                return new DateRange(d, d, DateRangeKind.ExactDay);
            }
            if (lower.Contains("завтра"))
            {
                var d = DateTime.Today.AddDays(1);
                return new DateRange(d, d, DateRangeKind.ExactDay);
            }

            // Дни недели: "в понедельник", "на среду", "пары в пятницу"
            if (TryExtractWeekdayTargetDate(lower, out var weekdayDate))
            {
                return new DateRange(weekdayDate, weekdayDate, DateRangeKind.ExactDay);
            }

            // Явная дата внутри фразы: 04.04.2026 / 4.4.2026 / 2026-04-04
            if (TryExtractExplicitDate(lower, out var explicitDate))
            {
                return new DateRange(explicitDate, explicitDate, DateRangeKind.ExactDay);
            }

            // Текстовая дата внутри фразы: "5 апреля", "12 марта 2026"
            if (TryExtractRuTextDate(lower, out var ruTextDate))
            {
                return new DateRange(ruTextDate, ruTextDate, DateRangeKind.ExactDay);
            }

            // По умолчанию — ближайшие 7 дней (как было раньше)
            return new DateRange(DateTime.Today, DateTime.Today.AddDays(7), DateRangeKind.DefaultNext7Days);
        }

        private static DateTime StartOfWeek(DateTime dt)
        {
            // ru-RU: неделя начинается с понедельника
            var diff = ((int)dt.DayOfWeek + 6) % 7; // Monday=0..Sunday=6
            return dt.Date.AddDays(-diff);
        }

        private static bool TryExtractExplicitDate(string text, out DateTime date)
        {
            // dd.MM.yyyy / dd/MM/yyyy / dd,MM,yyyy (или d.M.yyyy) — где угодно в строке
            var m1 = Regex.Match(text, @"(?<!\d)(\d{1,2})[./,](\d{1,2})[./,](\d{4})(?!\d)");
            if (m1.Success
                && int.TryParse(m1.Groups[1].Value, out var d)
                && int.TryParse(m1.Groups[2].Value, out var mo)
                && int.TryParse(m1.Groups[3].Value, out var y))
            {
                try
                {
                    date = new DateTime(y, mo, d);
                    return true;
                }
                catch { /* ignore */ }
            }

            // yyyy-MM-dd где угодно в строке
            var m2 = Regex.Match(text, @"(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)");
            if (m2.Success
                && int.TryParse(m2.Groups[1].Value, out var y2)
                && int.TryParse(m2.Groups[2].Value, out var mo2)
                && int.TryParse(m2.Groups[3].Value, out var d2))
            {
                try
                {
                    date = new DateTime(y2, mo2, d2);
                    return true;
                }
                catch { /* ignore */ }
            }

            // Фолбэк: одна дата в строке (в т.ч. «03.04.2026» с нестандартной точкой)
            var trimmed = text.Trim();
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            if (DateTime.TryParse(trimmed, ru, DateTimeStyles.None, out var parsed))
            {
                date = parsed.Date;
                return true;
            }

            date = default;
            return false;
        }

        private static bool TryExtractRuTextDate(string text, out DateTime date)
        {
            // "5 апреля" / "5 апреля 2026" внутри строки
            // Включаем разные падежи (апреля, мартa, ...), поэтому матчим по корню.
            var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["янв"] = 1, ["фев"] = 2, ["мар"] = 3, ["апрел"] = 4,
                ["ма"] = 5, ["июн"] = 6, ["июл"] = 7, ["авг"] = 8,
                ["сент"] = 9, ["окт"] = 10, ["ноябр"] = 11, ["декабр"] = 12
            };

            var m = Regex.Match(text, @"(?<!\d)(\d{1,2})\s+(январ[ьяе]|феврал[ьяе]|март[а]?|апрел[ьяе]|ма[йя]|июн[ьяе]|июл[ьяе]|август[ае]?|сентябр[ьяе]|октябр[ьяе]|ноябр[ьяе]|декабр[ьяе])(\s+(\d{4}))?(?!\d)");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var day))
            {
                date = default;
                return false;
            }

            var monthToken = m.Groups[2].Value;
            var month = months.FirstOrDefault(kv => monthToken.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;
            if (month <= 0)
            {
                date = default;
                return false;
            }

            var year = DateTime.Today.Year;
            if (m.Groups[4].Success && int.TryParse(m.Groups[4].Value, out var parsedYear))
                year = parsedYear;

            // Если год не указан и дата уже прошла в этом году — считаем, что имели в виду следующий год
            if (!m.Groups[4].Success)
            {
                try
                {
                    var candidate = new DateTime(year, month, day);
                    if (candidate.Date < DateTime.Today.Date)
                        year++;
                }
                catch { /* ignore */ }
            }

            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                date = default;
                return false;
            }
        }

        private static bool TryExtractWeekdayTargetDate(string lowerText, out DateTime date)
        {
            // Примеры: "в понедельник", "на среду", "пары в пятницу"
            // Берём ближайший наступающий день недели (включая сегодня, если совпадает).
            var map = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
            {
                ["понедельник"] = DayOfWeek.Monday,
                ["вторник"] = DayOfWeek.Tuesday,
                ["среда"] = DayOfWeek.Wednesday,
                ["четверг"] = DayOfWeek.Thursday,
                ["пятница"] = DayOfWeek.Friday,
                ["суббота"] = DayOfWeek.Saturday,
                ["воскресенье"] = DayOfWeek.Sunday
            };

            // Слово целиком (избегаем ложных вхождений подстрок в других словах)
            foreach (var kv in map)
            {
                var pattern = $@"(?<![а-яё]){Regex.Escape(kv.Key)}(?![а-яё])";
                if (Regex.IsMatch(lowerText, pattern, RegexOptions.IgnoreCase))
                {
                    var target = kv.Value;
                    var today = DateTime.Today;
                    var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
                    date = today.AddDays(delta);
                    return true;
                }
            }

            date = default;
            return false;
        }

        private static string BuildNormalizedQuestion(string originalQuestion, DateRange range)
        {
            var lower = (originalQuestion ?? string.Empty).ToLowerInvariant();

            // Если пользователь просто написал "завтра" / "сегодня" и т.п., нормализуем к конкретной дате —
            // так LLM всегда видит явную дату.
            if (range.Kind == DateRangeKind.ExactDay)
            {
                // Сохраним исходный запрос (он может содержать фильтры типа аудитории/препода),
                // но добавим явную дату, чтобы модель не гадала.
                return $"{originalQuestion}\n\n(Запрос-дата: {range.Start:dd.MM.yyyy})";
            }

            if (range.Kind == DateRangeKind.WeekRange)
            {
                return $"{originalQuestion}\n\n(Запрос-диапазон: {range.Start:dd.MM.yyyy}–{range.End:dd.MM.yyyy})";
            }

            // По умолчанию не меняем
            if (lower.Contains("на этой неделе"))
                return $"{originalQuestion}\n\n(Запрос-диапазон: {range.Start:dd.MM.yyyy}–{range.End:dd.MM.yyyy})";

            return originalQuestion;
        }

        private string? ExtractTeacherNameFromQuestion(string question)
        {
            // Исключим служебные слова из вопроса
            var ignoreWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "покажи", "расписание", "на", "для", "по", "в", "и", "или", "а", "но", "же", "бы", "то", "как", "что", 
                "все", "всё", "пары", "лекции", "сегодня", "завтра", "послезавтра", "неделе", "этой", "все", "всё", 
                "пары", "лекции", "сегодня", "завтра", "послезавтра", "неделе", "этой", "на", "этой"
            };

            var words = question.Split(new[] { ' ', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanWords = new List<string>();

            foreach (var word in words)
            {
                if (!ignoreWords.Contains(word))
                {
                    cleanWords.Add(word);
                }
            }

            // Попробуем найти ФИО в очищенном списке слов
            if (cleanWords.Count >= 2)
            {
                for (int i = 0; i < cleanWords.Count - 2; i++)
                {
                    var word1 = cleanWords[i];
                    var word2 = cleanWords[i + 1];
                    var word3 = cleanWords[i + 2];

                    if (char.IsUpper(word1[0]) && char.IsUpper(word2[0]) && char.IsUpper(word3[0]))
                    {
                        return $"{word1} {word2} {word3}";
                    }
                }
            }

            // Также можно использовать регулярку на очищенной строке
            var cleanQuestion = string.Join(" ", cleanWords);
            var regex = new Regex(@"([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)", RegexOptions.IgnoreCase);
            var match = regex.Match(cleanQuestion);
            if (match.Success)
            {
                return match.Value.Trim();
            }

            return null;
        }

        private string? ExtractRoomFromQuestion(string question)
        {
            // Удалим даты из строки (точка/слэш/запятая как разделитель)
            var withoutDates = Regex.Replace(question, @"\d{1,2}[./,]\d{1,2}[./,]\d{4}", "");

            var regex = new Regex(@"(\d+[а-яё]?\-?[а-яё]?)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(withoutDates);
            foreach (Match match in matches)
            {
                var room = match.Value;
                if (room.Length >= 2) // например, 426
                {
                    // Проверим, не является ли это числом из даты (например, 01 из 01.04.2026)
                    if (!Regex.IsMatch(question, $@"{Regex.Escape(room)}\.\d{{2}}\.\d{{4}}")) // не dd.MM.yyyy
                    {
                        return room;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Сообщение — только дата (без слов), чтобы не выделять «03» как аудиторию из «03.04.2026».
        /// Не срабатывает на «покажи расписание на 03.04.2026» — там аудиторию по цифрам всё ещё можно искать.
        /// </summary>
        private static bool LooksLikeDateOnlyMessage(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return false;
            var t = question.Trim();
            // dd.MM.yyyy / dd/MM/yyyy / dd,MM,yyyy
            if (Regex.IsMatch(t, @"^\d{1,2}[./,]\d{1,2}[./,]\d{4}$"))
                return true;
            // yyyy-MM-dd
            if (Regex.IsMatch(t, @"^\d{4}-\d{1,2}-\d{1,2}$"))
                return true;
            // одна строка без букв — попытка распознать дату (редкие форматы)
            if (!Regex.IsMatch(t, @"[а-яёa-z]", RegexOptions.IgnoreCase)
                && DateTime.TryParse(t, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out _))
                return true;
            return false;
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