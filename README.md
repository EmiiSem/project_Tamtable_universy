# RukScheduleApp

Клиентское приложение для **просмотра расписания преподавателя** с сайта учебного заведения, с **кэшем в локальной базе** и опциональным **AI-ассистентом** (Yandex Cloud / OpenAI-совместимый API по конфигурации).

> **Важно.** Это **учебно-специфичное** приложение: оно заточено под конкретную схему получения данных (парсинг HTML, филиалы, списки сотрудников). Для другого вуза или другого сайта потребуется доработка парсера и, при необходимости, логики API.

---

## Возможности

- Выбор **филиала**, **преподавателя** (с поиском по ФИО) и **даты**
- Загрузка расписания с сайта и отображение в чате
- Кэширование расписания в **SQLite** (Entity Framework Core)
- Диалог с **AI-ассистентом** по загруженному контексту (при настроенном ключе API)
- Платформы: **Windows**, **Android** (в solution также могут быть iOS / Mac Catalyst при сборке на macOS)

---

## Технологии

| Область | Стек |
|--------|------|
| UI | **.NET MAUI**, XAML, MVVM (**CommunityToolkit.Mvvm**) |
| Язык | **C#**, .NET **10** |
| Данные | **SQLite**, **Entity Framework Core** |
| Сеть / парсинг | **HttpClient**, **HtmlAgilityPack** |
| AI | **LlmApiService** (HTTP к облачному API), опционально **LangChain** / **OllamaSharp** в зависимостях проекта |

---

## Что нужно для разработки

1. [**Visual Studio 2022**](https://visualstudio.microsoft.com/) с рабочей нагрузкой **«Разработка мобильных приложений на .NET»** *или* [**Visual Studio Code**](https://code.visualstudio.com/) + [.NET SDK](https://dotnet.microsoft.com/download).
2. Установленный **.NET SDK 10** (версия должна соответствовать `TargetFrameworks` в `RukScheduleApp.csproj`).
3. Для **Android**: Android SDK / эмулятор или устройство с отладкой по USB.
4. Для **Windows**: Windows 10 версии **19041** и выше (см. TFM `net10.0-windows10.0.19041.0`).

Проверка SDK:

```bash
dotnet --info
```

Установка рабочей нагрузки MAUI (при необходимости):

```bash
dotnet workload install maui
```

---

## Конфигурация AI (необязательно)

Ключ и параметры API задаются в ресурсах приложения (например, `Resources/Raw/openai_config.json` — см. актуальные файлы в репозитории и примеры `*.example.json`). Без ключа часть функций AI может быть недоступна или вернёт подсказку в интерфейсе.

---

## Команды `dotnet`

Все команды выполняйте из **каталога проекта**, где лежит `RukScheduleApp.csproj`:

```bash
cd путь\к\RukScheduleApp
```

### 1. Сборка приложения (общая)

Сборка для **всех** целевых платформ, указанных в проекте (на машине без macOS iOS/Mac Catalyst могут быть пропущены):

```bash
dotnet build
```

Сборка в **Release**:

```bash
dotnet build -c Release
```

### 2. Сборка под мобильную платформу (Android)

```bash
dotnet build -f net10.0-android -c Release
```

При необходимости Debug:

```bash
dotnet build -f net10.0-android -c Debug
```

### 3. Сборка проекта под Windows

```bash
dotnet build -f net10.0-windows10.0.19041.0
```

С конфигурацией Release:

```bash
dotnet build -f net10.0-windows10.0.19041.0 -c Release
```

---

## Запуск на Windows

```bash
dotnet run -f net10.0-windows10.0.19041.0
```

---

## Публикация APK (Android)

Команда:

```bash
dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

Готовый подписанный пакет обычно находится здесь (относительно папки проекта):

```
bin\Release\net10.0-android\com.companyname.rukscheduleapp-Signed.apk
```

Полный путь в типичном случае:

```
RukScheduleApp\bin\Release\net10.0-android\com.companyname.rukscheduleapp-Signed.apk
```

> Имя файла может слегка отличаться в зависимости от версии SDK и настроек подписи; ищите `*-Signed.apk` в выходной папке `publish` или `android`-артефактов.

---

## Структура решения (кратко)

- `Views/` — страницы MAUI (например, экран расписания и чата)
- `ViewModels/` — логика экранов (MVVM)
- `Services/` — парсер расписания, БД, вызовы LLM
- `Models/`, `Data/` — модели и контекст EF Core
- `Resources/` — стили, шрифты, сырьевые конфиги

---

## Лицензия и использование

Проект предназначен для **учебных и внутренних** сценариев в рамках конкретной организации. Перед публикацией в магазинах приложений проверьте **лицензионные условия** источника расписания и **политику персональных данных**.
