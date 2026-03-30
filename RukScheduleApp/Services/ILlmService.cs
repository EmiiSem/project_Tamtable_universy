namespace RukScheduleApp.Services
{
    public interface ILlmService
    {
        Task<string> AskAboutScheduleAsync(string question, string groupName);
    }
}
