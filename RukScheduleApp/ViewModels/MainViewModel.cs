using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RukScheduleApp.Models;
using RukScheduleApp.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace RukScheduleApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string WelcomeText =
            "Здравствуйте! Выберите филиал, преподавателя и дату, затем нажмите «Загрузить расписание» — расписание появится в чате; при наличии ключа AI добавит краткое пояснение.";

        private readonly IScheduleParser _parser;
        private readonly LlmApiService _llmService;

        [ObservableProperty]
        private List<string> _branches;

        [ObservableProperty]
        private string _selectedBranch;

        [ObservableProperty]
        private List<string> _teachers;

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

        private List<ScheduleItem> _currentScheduleContext = new();

        public MainViewModel(IScheduleParser parser, LlmApiService llmService)
        {
            _parser = parser;
            _llmService = llmService;
            ChatHistory = new ObservableCollection<ChatMessage>();
            ChatHistory.Add(new ChatMessage { Role = "assistant", Content = WelcomeText });
        }

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
                // Часто VPN/SSL или проблемы сети ломают загрузку филиалов.
                // Чтобы приложение не вылетало — покажем ошибку в чате.
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
            if (string.IsNullOrEmpty(value))
                return;
            _ = LoadTeachersForBranchAsync(value);
        }

        private async Task LoadTeachersForBranchAsync(string branchName)
        {
            SelectedTeacher = null;
            Teachers = null;
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
                _currentScheduleContext = await _parser.GetScheduleAsync(SelectedTeacher, SelectedDate);

                if (_currentScheduleContext == null || !_currentScheduleContext.Any())
                {
                    ChatHistory.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = "На выбранную дату в ответе сайта не найдено занятий. Проверьте дату или выберите другой день."
                    });
                    return;
                }

                var formatted = FormatScheduleForChat(SelectedTeacher, SelectedDate, _currentScheduleContext);
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = formatted });

                var ai = await _llmService.GetAnswerAsync(
                    "Кратко опиши это расписание для преподавателя списком: номер пары, предмет, группа, аудитория и тип занятия. Один-два абзаца, по-русски.",
                    _currentScheduleContext);

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
                var answer = await _llmService.GetAnswerAsync(question, _currentScheduleContext);
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
                var who = m.Role == "user" ? "Вы" : "Ассистент";
                sb.AppendLine($"{who}: {m.Content}");
                sb.AppendLine();
            }
            await Clipboard.Default.SetTextAsync(sb.ToString().TrimEnd());
        }
    }
}
