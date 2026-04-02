using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RukScheduleApp.Models;
using RukScheduleApp.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RukScheduleApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string WelcomeText =
            "Здравствуйте! Выберите филиал, преподавателя и дату, затем нажмите «Загрузить расписание» — расписание появится в чате, либо задайте вопрос к AI-ассистенту";

        private readonly IScheduleParser _parser;
        private readonly ILlmService _llmService;
        private readonly IDatabaseService _databaseService;

        [ObservableProperty]
        private List<string> _branches;

        [ObservableProperty]
        private string _selectedBranch;

        [ObservableProperty]
        private List<string> _teachers;

        [ObservableProperty]
        private ObservableCollection<string> _filteredTeachers = new();

        [ObservableProperty]
        private string _teacherSearchText = string.Empty;

        [ObservableProperty]
        private bool _showTeacherEmptySearchMessage;

        [ObservableProperty]
        private string _selectedTeacher;

        [ObservableProperty]
        private DateTime _selectedDate = DateTime.Today;

        [ObservableProperty]
        private ObservableCollection<ChatMessage> _chatHistory;

        [ObservableProperty]
        private string _userInput;

        [ObservableProperty]
        private bool _isBusy;

        public MainViewModel(IScheduleParser parser, ILlmService llmService, IDatabaseService databaseService)
        {
            _parser = parser;
            _llmService = llmService;
            _databaseService = databaseService;
            ChatHistory = new ObservableCollection<ChatMessage>();
            ChatHistory.Add(new ChatMessage { Role = "assistant", Content = WelcomeText });
        }

        /// <summary>Подсказка до выбора филиала (список преподавателей ещё не загружен).</summary>
        public bool ShowTeacherBranchHint =>
            string.IsNullOrEmpty(SelectedBranch) && Teachers is null && !IsBusy;

        /// <summary>Индикатор загрузки списка после выбора филиала.</summary>
        public bool ShowTeacherLoadingRow =>
            !string.IsNullOrEmpty(SelectedBranch) && Teachers is null && IsBusy;

        [RelayCommand]
        private async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                Branches = await _parser.GetBranchesAsync();
            }
            catch (Exception ex)
            {
                ChatHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"Не удалось загрузить филиалы: {ex.Message}"
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

        partial void OnSelectedBranchChanged(string value)
        {
            OnPropertyChanged(nameof(ShowTeacherBranchHint));
            OnPropertyChanged(nameof(ShowTeacherLoadingRow));
            if (string.IsNullOrEmpty(value))
                return;
            _ = LoadTeachersForBranchAsync(value);
        }

        private async Task LoadTeachersForBranchAsync(string branchName)
        {
            SelectedTeacher = null;
            Teachers = null;
            TeacherSearchText = string.Empty;
            IsBusy = true;
            try
            {
                Teachers = await _parser.GetTeachersAsync(branchName);
            }
            catch (Exception ex)
            {
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = $"Не удалось загрузить список преподавателей: {ex.Message}" });
            }
            finally
            {
                IsBusy = false;
            }
        }

        partial void OnTeachersChanged(List<string> value)
        {
            RefreshFilteredTeachers();
            OnPropertyChanged(nameof(ShowTeacherBranchHint));
            OnPropertyChanged(nameof(ShowTeacherLoadingRow));
        }

        partial void OnTeacherSearchTextChanged(string value) => RefreshFilteredTeachers();

        partial void OnIsBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowTeacherBranchHint));
            OnPropertyChanged(nameof(ShowTeacherLoadingRow));
        }

        private void RefreshFilteredTeachers()
        {
            FilteredTeachers.Clear();
            ShowTeacherEmptySearchMessage = false;
            if (Teachers is null || Teachers.Count == 0)
                return;

            var q = (TeacherSearchText ?? string.Empty).Trim();
            IEnumerable<string> rows = Teachers;
            if (!string.IsNullOrEmpty(q))
                rows = Teachers.Where(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));

            foreach (var t in rows)
                FilteredTeachers.Add(t);

            ShowTeacherEmptySearchMessage = !string.IsNullOrEmpty(q) && FilteredTeachers.Count == 0;
        }

        [RelayCommand]
        private void SelectTeacher(string? teacher)
        {
            if (string.IsNullOrWhiteSpace(teacher))
                return;
            SelectedTeacher = teacher;
        }

        [RelayCommand]
        private async Task LoadScheduleAsync()
        {
            if (string.IsNullOrEmpty(SelectedTeacher))
            {
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = "Пожалуйста, выберите преподавателя." });
                return;
            }

            IsBusy = true;
            try
            {
                var scheduleContext = await _parser.GetScheduleAsync(SelectedTeacher, SelectedDate);

                if (scheduleContext == null || !scheduleContext.Any())
                {
                    ChatHistory.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = "На выбранную дату в ответе сайта не найдено занятий. Проверьте дату или выберите другой день."
                    });
                    return;
                }

                // Сохранение расписания в кэш
                await _databaseService.CacheScheduleAsync(scheduleContext);

                var formatted = FormatScheduleForChat(SelectedTeacher, SelectedDate, scheduleContext);
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = formatted });

                // Филиал ≠ учебная группа: передаю готовые строки с сайта, иначе LLM получает пустой контекст (фильтр по GroupName).
                var ai = await _llmService.AskAboutScheduleAsync(
                    "Кратко опиши это расписание для преподавателя списком: номер пары, предмет, группа, аудитория и тип занятия. Один-два абзаца, по-русски.",
                    groupName: null,
                    scheduleContextOverride: scheduleContext);

                if (!string.IsNullOrWhiteSpace(ai) && !LooksLikeConfigOrTransportError(ai))
                    ChatHistory.Add(new ChatMessage { Role = "assistant", Content = ai });
            }
            catch (Exception ex)
            {
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = $"Ошибка загрузки: {ex.Message}" });
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool LooksLikeConfigOrTransportError(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            var t = text.TrimStart();
            return t.StartsWith("Укажите ключ", StringComparison.Ordinal)
                   || t.StartsWith("Ошибка AI API", StringComparison.Ordinal)
                   || t.StartsWith("Не удалось связаться", StringComparison.Ordinal);
        }

        private static string FormatScheduleForChat(string teacher, DateTime date, List<ScheduleItem> items)
        {
            var ru = new CultureInfo("ru-RU");
            var sb = new StringBuilder();
            sb.AppendLine($"Расписание: {teacher}");
            sb.AppendLine($"Дата: {date:dd.MM.yyyy} ({date.ToString("dddd", ru)})");
            sb.AppendLine();
            var n = 1;
            foreach (var x in items)
            {
                sb.AppendLine($"{n}. {x.Time} — {x.Subject}");
                if (!string.IsNullOrWhiteSpace(x.GroupName))
                    sb.AppendLine($"   Группа: {x.GroupName}");
                if (!string.IsNullOrWhiteSpace(x.Room))
                    sb.AppendLine($"   {x.Room}");
                sb.AppendLine();
                n++;
            }
            return sb.ToString().TrimEnd();
        }

        [RelayCommand]
        private async Task SendChatMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput))
                return;

            ChatHistory.Add(new ChatMessage { Role = "user", Content = UserInput });
            var question = UserInput;
            UserInput = string.Empty;

            IsBusy = true;
            try
            {
                string groupFilter = null;
                if (question.Contains("группа", StringComparison.OrdinalIgnoreCase))
                {
                    groupFilter = SelectedBranch; // только если запрос про группу
                }

                var answer = await _llmService.AskAboutScheduleAsync(question, groupFilter);
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = answer });
            }
            catch (Exception ex)
            {
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = $"Ошибка связи с AI: {ex.Message}" });
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void ClearChat()
        {
            ChatHistory.Clear();
            ChatHistory.Add(new ChatMessage { Role = "assistant", Content = WelcomeText });
        }

        [RelayCommand]
        private async Task CopyMessageAsync(ChatMessage? message)
        {
            if (message?.Content is null)
                return;
            await Clipboard.Default.SetTextAsync(message.Content);
        }

        [RelayCommand]
        private async Task CopyAllChatAsync()
        {
            var sb = new StringBuilder();
            foreach (var m in ChatHistory)
            {
                var who = m.Role == "user" ? "Вы" : "AI-ассистент";
                sb.AppendLine($"{who}: {m.Content}");
                sb.AppendLine();
            }
            await Clipboard.Default.SetTextAsync(sb.ToString().TrimEnd());
        }
    }
}