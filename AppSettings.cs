using System.Text.Json;

namespace KeyboardLayoutSwitcher;

// Правило: имя exe (в нижнем регистре) → код раскладки ("RU", "EN", ...).
public class LayoutRule
{
    public string ProcessName { get; set; } = "";   // например "notepad.exe"
    public string Layout { get; set; } = "";        // например "RU" или "EN"
}

public class AppSettings
{
    public List<LayoutRule> Rules { get; set; } = new();
    public bool StartWithWindows { get; set; } = false;
    public bool ShowBalloonOnSwitch { get; set; } = false;   // всплывающее уведомление о смене
    public bool EnableLogging { get; set; } = true;          // журнал переключений (switcher.log)

    private static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "KeyboardLayoutSwitcher");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (s != null) return s;
            }
        }
        catch
        {
            // повреждённый файл — используем значения по умолчанию
        }
        return CreateDefault();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static AppSettings CreateDefault()
    {
        var s = new AppSettings
        {
            Rules = new List<LayoutRule>
            {
                new() { ProcessName = "notepad.exe", Layout = "RU" },
                new() { ProcessName = "plsqldev.exe", Layout = "EN" },
            },
            StartWithWindows = false,
            ShowBalloonOnSwitch = false,
            EnableLogging = true,
        };
        s.Save();
        return s;
    }
}
