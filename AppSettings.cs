using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoundbarKeepAlive;

public enum KeepAliveSignalType
{
    SineWave = 0,
    PinkNoise = 1,
    DualSubTone = 2
}

public class AppSettings
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "AudioAwake_KeepAlive";
    private const string LegacyRegistryName = "SoundbarKeepAlive_HS2100";

    // Hashes SHA-256 de validación PRO (imposibles de revertir desde el código público)
    private static readonly string[] ValidProKeyHashes =
    {
        "DC6409196386FE6F48755962FF3C17B7AC4672F3D07C99F2585AD4101C96B285",
        "B548D2E2DAC030218AAC1B0B55AAA81DCB4BAE31264F5975539C185EC0CDB469",
        "78279CB55596DF7395D2A63130D2505429F9858C0A89FAED71B2E93EA75EFC4A",
        "738CA2AEF52B8B0C1CD01158698E12E17EB8787626247D0248D24D6C8C391757",
        "A083AAD912C8D3828E05F8104AAB989EA1043452E55420320AA5220BF046549C"
    };

    public bool IsEnabled { get; set; } = true;
    public string SelectedDeviceId { get; set; } = "";
    public int BrandProfileIndex { get; set; } = 0;
    public int StrengthLevel { get; set; } = 2;
    public KeepAliveSignalType SignalType { get; set; } = KeepAliveSignalType.DualSubTone;
    public double FrequencyHz { get; set; } = 38.0;
    public double VolumePercent { get; set; } = 0.85;
    public bool PulseMode { get; set; } = false;
    public int PulseIntervalSeconds { get; set; } = 15;
    public bool SmartAutoSilence { get; set; } = false;

    public bool IdleSleepEnabled { get; set; } = true;
    public int IdleSleepMinutes { get; set; } = 30;

    public uint HotkeyModifiers { get; set; } = 3;
    public int HotkeyVirtualKey { get; set; } = (int)Keys.A;
    public string HotkeyText { get; set; } = "Ctrl + Alt + A";

    public string Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase)
            ? "es"
            : "en";

    public bool StartMinimized { get; set; } = false;
    public string LicenseKey { get; set; } = "";

#if LITE_BUILD
    public static bool IsCompiledAsFreeLite => true;
#else
    public static bool IsCompiledAsFreeLite => false;
#endif

#if GAMER_BUILD
    public static bool IsGamerEdition => true;
#else
    public static bool IsGamerEdition => false;
#endif

    public bool IsProUnlocked => !IsCompiledAsFreeLite || VerifyLicenseKey(LicenseKey);

    public static bool VerifyLicenseKey(string? inputKey)
    {
        if (string.IsNullOrWhiteSpace(inputKey)) return false;
        string clean = inputKey.Trim().ToUpperInvariant();

        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("ASTOSTUDIO_SALT_" + clean));
        string hex = Convert.ToHexString(hashBytes);

        foreach (string validHash in ValidProKeyHashes)
        {
            if (string.Equals(hex, validHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string SettingsFilePath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
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
                    settings = loaded;
                }
            }
        }
        catch
        {
        }

        // Si es la versión Gratis de GitHub y no tiene licencia PRO, limitar a Nivel 1 y desactivar funciones PRO
        if (!settings.IsProUnlocked)
        {
            settings.StrengthLevel = 1;
            settings.BrandProfileIndex = 0;
            settings.IdleSleepEnabled = false;
        }

        return settings;
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
