using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoundbarKeepAlive;

public enum KeepAliveSignalType
{
    SineWave = 0,       // Onda sub-grave + piloto ultrasónico anti-gate (19.2 kHz)
    PinkNoise = 1,      // Ruido rosa ultra-bajo (ideal para óptico/DSP)
    DualSubTone = 2     // Híbrido Anti-Gate AUX (40 Hz + 19.2 kHz + Micro-Dither)
}

public class AppSettings
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "AudioAwake_KeepAlive";
    private const string LegacyRegistryName = "SoundbarKeepAlive_HS2100";

    public bool IsEnabled { get; set; } = true;
    public string SelectedDeviceId { get; set; } = "";
    public int BrandProfileIndex { get; set; } = 0; // 0 = Hisense HS2100 / Series
    public int StrengthLevel { get; set; } = 2;     // 1..5 (Por defecto Nivel 2 Anti-Gate AUX)
    public KeepAliveSignalType SignalType { get; set; } = KeepAliveSignalType.DualSubTone;
    public double FrequencyHz { get; set; } = 38.0;
    public double VolumePercent { get; set; } = 0.85;
    public bool PulseMode { get; set; } = false;
    public int PulseIntervalSeconds { get; set; } = 15;

    // Si es false (recomendado para AUX con Noise Gate agresivo), nunca corta el piloto para que el amplificador jamás se cierre
    public bool SmartAutoSilence { get; set; } = false;

    // Modo Ahorro de Energía (apagar protección si el usuario se va de la PC y no hay audio)
    public bool IdleSleepEnabled { get; set; } = true;
    public int IdleSleepMinutes { get; set; } = 30;

    // Atajo de Teclado Global (Por defecto: Ctrl + Alt + A)
    public uint HotkeyModifiers { get; set; } = 3; // Ctrl (2) + Alt (1)
    public int HotkeyVirtualKey { get; set; } = (int)Keys.A;
    public string HotkeyText { get; set; } = "Ctrl + Alt + A";

    // Idioma ("es" o "en")
    public string Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase)
            ? "es"
            : "en";

    public bool StartMinimized { get; set; } = false;

    private static string SettingsFilePath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.Language))
                        loaded.Language = "es";
                    if (string.IsNullOrWhiteSpace(loaded.HotkeyText))
                        loaded.HotkeyText = "Ctrl + Alt + A";
                    loaded.StrengthLevel = Math.Clamp(loaded.StrengthLevel, 1, 5);
                    return loaded;
                }
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
        }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, false);
            string? val = key?.GetValue(AppRegistryName) as string ?? key?.GetValue(LegacyRegistryName) as string;
            return !string.IsNullOrEmpty(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupEnabled(bool enable)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
            if (key == null) return;

            if (key.GetValue(LegacyRegistryName) != null)
            {
                key.DeleteValue(LegacyRegistryName, false);
            }

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(AppRegistryName, $"\"{exePath}\" --minimized");
            }
            else
            {
                if (key.GetValue(AppRegistryName) != null)
                {
                    key.DeleteValue(AppRegistryName, false);
                }
            }
        }
        catch
        {
        }
    }
}
