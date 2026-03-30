using RukScheduleApp.Data;
using RukScheduleApp.Services;
using RukScheduleApp.ViewModels;

namespace RukScheduleApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            // Регистрация сервисов
            var dbPath = Path.Combine(
                FileSystem.AppDataDirectory,
                "schedule.db");

            builder.Services.AddSingleton(new ScheduleDbContext(dbPath));
            builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
            builder.Services.AddSingleton<IScheduleParser, ScheduleParser>();
            builder.Services.AddSingleton<ILlmService, LlmService>();

            // Регистрация HttpClient
            builder.Services.AddHttpClient<IScheduleParser, ScheduleParser>();
            builder.Services.AddHttpClient<LlmApiService>();

            // Регистрация ViewModel
            builder.Services.AddTransient<MainViewModel>();

            // Регистрация Views
            builder.Services.AddTransient<Views.SchedulePage>();

            return builder.Build();
        }
    }
}