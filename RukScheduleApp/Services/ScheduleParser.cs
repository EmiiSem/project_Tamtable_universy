using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    /// <summary>
    /// Парсинг страницы https://schedule.ruc.su/employee/ (филиалы и сотрудники через POST формы).
    /// </summary>
    public class ScheduleParser : IScheduleParser
    {
        private const string EmployeePageUrl = "https://schedule.ruc.su/employee/";

        private readonly HttpClient _httpClient;

        /// <summary>Название филиала → value option (GUID).</summary>
        private readonly Dictionary<string, string> _branchIdByName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>ФИО сотрудника → value option (GUID) для последнего загруженного филиала.</summary>
        private readonly Dictionary<string, string> _employeeIdByName = new(StringComparer.OrdinalIgnoreCase);

        private string? _lastBranchId;

        public ScheduleParser(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        }

        public async Task<List<string>> GetBranchesAsync()
        {
            var html = await _httpClient.GetStringAsync(EmployeePageUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            _branchIdByName.Clear();
            var branches = new List<string>();
            var selectNodes = doc.DocumentNode.SelectNodes("//select[@name='branch']//option");

            if (selectNodes != null)
            {
                foreach (var node in selectNodes)
                {
                    var id = node.GetAttributeValue("value", "").Trim();
                    var name = NormalizeText(node.InnerText);
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                        continue;
                    _branchIdByName[name] = id;
                    branches.Add(name);
                }
            }

            return branches;
        }

        public async Task<List<string>> GetTeachersAsync(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return new List<string>();

            if (_branchIdByName.Count == 0)
                await GetBranchesAsync();

            if (!_branchIdByName.TryGetValue(NormalizeText(branchName), out var branchId))
                return new List<string>();

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["branch"] = branchId
            });

            var response = await _httpClient.PostAsync(EmployeePageUrl, content);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            _employeeIdByName.Clear();
            _lastBranchId = branchId;

            var teachers = new List<string>();
            var options = doc.DocumentNode.SelectNodes("//select[@name='employee']//option");

            if (options != null)
            {
                foreach (var node in options)
                {
                    var id = node.GetAttributeValue("value", "").Trim();
                    var name = NormalizeText(node.InnerText);
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                        continue;
                    _employeeIdByName[name] = id;
                    teachers.Add(name);
                }
            }

            teachers.Sort(StringComparer.OrdinalIgnoreCase);
            return teachers;
        }

        public async Task<List<ScheduleItem>> GetScheduleAsync(string teacherName, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(teacherName) || string.IsNullOrEmpty(_lastBranchId))
                return new List<ScheduleItem>();

            var key = NormalizeText(teacherName);
            if (!_employeeIdByName.TryGetValue(key, out var employeeId))
                return new List<ScheduleItem>();

            var dateIso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["branch"] = _lastBranchId,
                ["employee"] = employeeId,
                ["scheduler-date"] = dateIso,
                ["date-search"] = dateIso,
                ["search-date"] = "search-date"
            });

            var response = await _httpClient.PostAsync(EmployeePageUrl, content);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var fromCards = ParseScheduleFromBootstrapCards(doc, teacherName, date);
            if (fromCards.Count > 0)
                return fromCards;

            return ParseScheduleTable(doc, teacherName, date);
        }

        /// <summary>
        /// Актуальная вёрстка schedule.ruc.su: неделя из блоков div.card (заголовок — дата, тело — пары).
        /// </summary>
        private static List<ScheduleItem> ParseScheduleFromBootstrapCards(HtmlDocument doc, string teacherName, DateTime filterDate)
        {
            var items = new List<ScheduleItem>();
            var cards = doc.DocumentNode.SelectNodes("//div[@class='card']");
            if (cards == null)
                return items;

            var filterDay = filterDate.Date;
            var ru = new CultureInfo("ru-RU");

            foreach (var card in cards)
            {
                var header = card.SelectSingleNode(".//div[contains(concat(' ', normalize-space(@class), ' '), ' card-header ')]");
                if (header is null)
                    continue;

                var headerText = NormalizeText(header.InnerText);
                if (!TryParseCardHeaderDate(headerText, out var cardDate))
                    continue;

                if (cardDate.Date != filterDay)
                    continue;

                var dayOfWeek = ExtractDayInParens(headerText)
                                ?? cardDate.ToString("dddd", ru);

                var bodies = card.SelectNodes(".//div[contains(concat(' ', normalize-space(@class), ' '), ' card-body ')]");
                if (bodies is null)
                    continue;

                foreach (var body in bodies)
                {
                    if (!TryParseCardBodyLesson(body, out var pairNo, out var subject, out var group, out var roomLine))
                        continue;

                    items.Add(new ScheduleItem
                    {
                        Time = $"Пара {pairNo}",
                        Subject = subject,
                        Teacher = teacherName,
                        GroupName = group,
                        Room = roomLine,
                        Date = cardDate.Date,
                        DayOfWeek = dayOfWeek
                    });
                }
            }

            return items;
        }

        private static bool TryParseCardHeaderDate(string headerText, out DateTime date)
        {
            date = default;
            var m = Regex.Match(headerText, @"(\d{2})\.(\d{2})\.(\d{4})");
            if (!m.Success)
                return false;
            try
            {
                date = new DateTime(
                    int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractDayInParens(string headerText)
        {
            var m = Regex.Match(headerText, @"\(([^)]+)\)\s*$");
            return m.Success ? NormalizeText(m.Groups[1].Value) : null;
        }

        private static bool TryParseCardBodyLesson(HtmlNode body, out string pairNo, out string subject, out string group, out string room)
        {
            pairNo = subject = group = room = string.Empty;
            var text = CardBodyToPlainLines(body);
            if (text.Count == 0)
                return false;

            var first = text[0];
            var m = Regex.Match(first, @"^(\d+)\.\s*(.+)$");
            if (!m.Success)
                return false;

            pairNo = m.Groups[1].Value;
            subject = NormalizeText(m.Groups[2].Value);

            foreach (var line in text.Skip(1))
            {
                if (line.StartsWith("Группа", StringComparison.OrdinalIgnoreCase))
                    group = NormalizeText(line["Группа".Length..].Trim());
                else if (line.StartsWith("ауд.", StringComparison.OrdinalIgnoreCase))
                    room = NormalizeText(line);
            }

            return !string.IsNullOrEmpty(subject);
        }

        private static List<string> CardBodyToPlainLines(HtmlNode body)
        {
            var sb = new StringBuilder();

            void Walk(HtmlNode n)
            {
                foreach (var c in n.ChildNodes)
                {
                    if (c.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
                        sb.Append('\n');
                    else if (c.NodeType == HtmlNodeType.Text)
                        sb.Append(c.InnerText);
                    else
                        Walk(c);
                }
            }

            Walk(body);
            return sb
                .ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeText)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        private static List<ScheduleItem> ParseScheduleTable(HtmlDocument doc, string teacherName, DateTime date)
        {
            var items = new List<ScheduleItem>();

            var rows = doc.DocumentNode.SelectNodes("//table[contains(@class,'schedule')]//tr")
                          ?? doc.DocumentNode.SelectNodes("//table//tr");

            if (rows == null)
                return items;

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./td");
                if (cells == null || cells.Count < 3)
                    continue;

                var time = NormalizeText(cells[0].InnerText);
                var subject = cells.Count > 1 ? NormalizeText(cells[1].InnerText) : "";
                var room = cells.Count > 3 ? NormalizeText(cells[3].InnerText) : "";

                if (string.IsNullOrEmpty(time) && string.IsNullOrEmpty(subject))
                    continue;

                items.Add(new ScheduleItem
                {
                    Time = time,
                    Subject = subject,
                    Teacher = teacherName,
                    Room = room,
                    Date = date,
                    DayOfWeek = date.ToString("dddd", new CultureInfo("ru-RU"))
                });
            }

            return items;
        }

        private static string NormalizeText(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var t = s.Replace('\u00A0', ' ').Trim();
            while (t.Contains("  ", StringComparison.Ordinal))
                t = t.Replace("  ", " ", StringComparison.Ordinal);
            return t;
        }
    }
}
