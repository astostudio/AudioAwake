using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SoundbarKeepAlive;

public record AudioOutputDeviceItem(string Id, string FriendlyName, bool IsPreferredInterface);

public record SpeakerProfilePreset(
    string NameEs,
    string NameEn,
    KeepAliveSignalType DefaultSignal,
    double BaseFrequencyHz,
    double Level1Volume,
    double Level2Volume,
    double Level3Volume,
    double Level4Volume,
    double Level5Volume);

public class AudioKeepAliveEngine : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static readonly List<SpeakerProfilePreset> Profiles = new()
    {
        new(
            "Hisense Soundbars (HS2100 / HS205 / AX — Anti-Gate AUX)",
            "Hisense Soundbars (HS2100 / HS205 / AX — AUX Anti-Gate)",
            KeepAliveSignalType.DualSubTone, 38.0, 0.45, 0.95, 1.85, 3.10, 4.80),
        new(
            "Samsung / LG / Sony Soundbar (AUX / Analógico)",
            "Samsung / LG / Sony Soundbar (AUX / Analog)",
            KeepAliveSignalType.DualSubTone, 35.0, 0.35, 0.80, 1.60, 2.70, 4.20),
        new(
            "JBL Bar / Creative Stage / Bose Solo",
            "JBL Bar / Creative Stage / Bose Solo",
            KeepAliveSignalType.DualSubTone, 36.0, 0.40, 0.90, 1.75, 2.90, 4.50),
        new(
            "Monitores de Estudio (KRK Rokit / Edifier / PreSonus / Yamaha)",
            "Studio Monitors (KRK Rokit / Edifier / PreSonus / Yamaha)",
            KeepAliveSignalType.DualSubTone, 38.0, 0.45, 1.00, 1.95, 3.20, 5.00),
        new(
            "Cable Óptico Digital (SPDIF) / HDMI / Bluetooth",
            "Optical Digital (SPDIF) / HDMI / Bluetooth",
            KeepAliveSignalType.PinkNoise, 32.0, 0.20, 0.45, 0.90, 1.50, 2.40),
        new(
            "Universal — Cualquier Altavoz o Barra de Sonido",
            "Universal — Any Speaker or Soundbar",
            KeepAliveSignalType.DualSubTone, 36.0, 0.40, 0.85, 1.70, 2.80, 4.50)
    };

    private readonly AppSettings _settings;
    private WasapiOut? _wasapiOut;
    private MMDevice? _activeDevice;
    private KeepAliveSignalProvider? _signalProvider;
    private DateTime _lastRealAudioTime = DateTime.UtcNow;
    private DateTime _wakeBoostUntil = DateTime.MinValue;
    private bool _wasIdleSleeping = false;
    private readonly object _lock = new();

    public string CurrentDeviceName { get; private set; } = "Iniciando...";
    public bool IsRunning => _wasapiOut?.PlaybackState == PlaybackState.Playing && _settings.IsEnabled;
    public bool IsSuppressedByRealAudio => false;
    public bool IsSleepingDueToIdle => _signalProvider?.SuppressedByIdleSleep ?? false;
    public bool IsCurrentlyEmitting => _signalProvider?.IsCurrentlyEmitting ?? false;
    public float CurrentExternalAudioPeak { get; private set; }
    public double CurrentIdleMinutes { get; private set; }

    public event Action? StateChanged;

    public AudioKeepAliveEngine(AppSettings settings)
    {
        _settings = settings;
        SyncProfileParameters();
    }

    public void SyncProfileParameters()
    {
        int idx = Math.Clamp(_settings.BrandProfileIndex, 0, Profiles.Count - 1);
        var profile = Profiles[idx];

        _settings.SignalType = profile.DefaultSignal;
        _settings.FrequencyHz = profile.BaseFrequencyHz;
        _settings.VolumePercent = _settings.StrengthLevel switch
        {
            1 => profile.Level1Volume,
            2 => profile.Level2Volume,
            3 => profile.Level3Volume,
            4 => profile.Level4Volume,
            _ => profile.Level5Volume
        };
    }

    public List<AudioOutputDeviceItem> GetOutputDevices(bool isEnglish)
    {
        string defaultLabel = isEnglish
            ? "Windows Default Output (Automatic)"
            : "Salida Predeterminada de Windows (Automático)";

        var list = new List<AudioOutputDeviceItem>
        {
            new("", defaultLabel, false)
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in endpoints)
            {
                string name = dev.FriendlyName;
                bool isInterface = name.Contains("UMC", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("Behringer", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("USB Audio CODEC", StringComparison.OrdinalIgnoreCase);

                string displayName = isInterface ? $"★ {name}" : name;
                list.Add(new AudioOutputDeviceItem(dev.ID, displayName, isInterface));
            }
        }
        catch
        {
        }

        return list;
    }

    public void StartOrRestart()
    {
        lock (_lock)
        {
            StopInternal();
            SyncProfileParameters();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? targetDevice = null;

                if (!string.IsNullOrWhiteSpace(_settings.SelectedDeviceId))
                {
                    try
                    {
                        targetDevice = enumerator.GetDevice(_settings.SelectedDeviceId);
                        if (targetDevice.State != DeviceState.Active)
                        {
                            targetDevice = null;
                        }
                    }
                    catch
                    {
                        targetDevice = null;
                    }
                }

                targetDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _activeDevice = targetDevice;
                CurrentDeviceName = targetDevice.FriendlyName;

                int sampleRate = targetDevice.AudioClient.MixFormat.SampleRate;
                int channels = Math.Max(1, Math.Min(2, targetDevice.AudioClient.MixFormat.Channels));

                _signalProvider = new KeepAliveSignalProvider(sampleRate, channels)
                {
                    IsEnabled = _settings.IsEnabled,
                    SignalType = _settings.SignalType,
                    FrequencyHz = _settings.FrequencyHz,
                    VolumePercent = _settings.VolumePercent,
                    PulseMode = false
                };

                _wasapiOut = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 80);
                _wasapiOut.Init(_signalProvider);
                _wasapiOut.Play();
            }
            catch
            {
                CurrentDeviceName = "Reconectando salida...";
            }
        }

        StateChanged?.Invoke();
    }

    public void ApplySettingsLive()
    {
        lock (_lock)
        {
            SyncProfileParameters();

            if (_signalProvider != null)
            {
                _signalProvider.IsEnabled = _settings.IsEnabled;
                _signalProvider.SignalType = _settings.SignalType;
                _signalProvider.FrequencyHz = _settings.FrequencyHz;
                _signalProvider.VolumePercent = _settings.VolumePercent;
                _signalProvider.SuppressedByRealAudio = false;
                if (!_settings.IdleSleepEnabled)
                {
                    _signalProvider.SuppressedByIdleSleep = false;
                }
            }
        }
        StateChanged?.Invoke();
    }

    public void SetTestTone(bool active)
    {
        lock (_lock)
        {
            if (_signalProvider != null)
            {
                _signalProvider.IsTestToneActive = active;
            }
        }
        StateChanged?.Invoke();
    }

    private static double GetUserInputIdleSeconds()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (GetLastInputInfo(ref lii))
            {
                uint elapsedTicks = unchecked((uint)Environment.TickCount - lii.dwTime);
                return elapsedTicks / 1000.0;
            }
        }
        catch
        {
        }
        return 0;
    }

    public void TickMonitor()
    {
        lock (_lock)
        {
            if (_wasapiOut == null || _activeDevice == null || _signalProvider == null)
            {
                StartOrRestart();
                return;
            }

            if (_wasapiOut.PlaybackState != PlaybackState.Playing)
            {
                StartOrRestart();
                return;
            }

            try
            {
                float masterPeak = _activeDevice.AudioMeterInformation.MasterPeakValue;
                CurrentExternalAudioPeak = masterPeak;

                float ownAmplitude = (float)(_settings.VolumePercent / 100.0);
                float threshold = Math.Max(0.025f, ownAmplitude * 2.2f);
                bool isRealAudioPlaying = masterPeak > threshold && !_signalProvider.IsTestToneActive;

                if (isRealAudioPlaying)
                {
                    _lastRealAudioTime = DateTime.UtcNow;
                }

                // Mantenemos el piloto Anti-Gate siempre continuo (sin pausarlo cuando termina un sonido)
                // para que la puerta de ruido (Noise-Gate) del AUX de la Hisense HS2100 jamás se cierre.
                _signalProvider.SuppressedByRealAudio = false;

                double userIdleSeconds = GetUserInputIdleSeconds();
                double audioIdleSeconds = (DateTime.UtcNow - _lastRealAudioTime).TotalSeconds;
                double effectiveIdleSeconds = Math.Min(userIdleSeconds, audioIdleSeconds);
                CurrentIdleMinutes = effectiveIdleSeconds / 60.0;

                if (_settings.IdleSleepEnabled && _settings.IsEnabled)
                {
                    double maxIdleSeconds = Math.Max(1, _settings.IdleSleepMinutes) * 60.0;
                    bool shouldSleep = effectiveIdleSeconds >= maxIdleSeconds;

                    if (_wasIdleSleeping && !shouldSleep)
                    {
                        _wakeBoostUntil = DateTime.UtcNow.AddMilliseconds(800);
                    }

                    _wasIdleSleeping = shouldSleep;
                    _signalProvider.SuppressedByIdleSleep = shouldSleep;
                }
                else
                {
                    _wasIdleSleeping = false;
                    _signalProvider.SuppressedByIdleSleep = false;
                }

                _signalProvider.IsWakeBoostActive = DateTime.UtcNow < _wakeBoostUntil;
            }
            catch
            {
                StartOrRestart();
            }
        }
    }

    private void StopInternal()
    {
        try
        {
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
        }
        catch { }
        _wasapiOut = null;

        try
        {
            _activeDevice?.Dispose();
        }
        catch { }
        _activeDevice = null;
        _signalProvider = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }
}
