using System;
using NAudio.Wave;

namespace SoundbarKeepAlive;

/// <summary>
/// Generador matemático en tiempo real con tecnología Híbrida Anti-Gate AUX:
/// Combina portadora sub-grave (38-42 Hz) + Piloto Ultrasónico (19.2 kHz) + Micro-Dither
/// para superar el filtro paso-alto interno de barras como la Hisense HS2100 y mantener
/// el amplificador Clase-D permanentemente abierto sin "micro-activaciones" al iniciar un sonido.
/// </summary>
public class KeepAliveSignalProvider : ISampleProvider
{
    private readonly WaveFormat _waveFormat;
    private readonly Random _random = new();

    private double _phaseSub;
    private double _phaseUltra;
    private double _testPhase;
    private double _currentGain;

    private double _b0, _b1, _b2, _b3, _b4, _b5, _b6;

    public volatile bool IsEnabled = true;
    public volatile bool IsTestToneActive = false;
    public volatile bool SuppressedByRealAudio = false;
    public volatile bool SuppressedByIdleSleep = false;
    public volatile bool IsWakeBoostActive = false;

    public KeepAliveSignalType SignalType { get; set; } = KeepAliveSignalType.DualSubTone;
    public double FrequencyHz { get; set; } = 38.0;
    public double VolumePercent { get; set; } = 0.85;
    public bool PulseMode { get; set; } = false;
    public int PulseIntervalSeconds { get; set; } = 15;

    private long _totalSamplesProcessed;
    public float LastOutputPeak { get; private set; }
    public bool IsCurrentlyEmitting { get; private set; }

    public WaveFormat WaveFormat => _waveFormat;

    public KeepAliveSignalProvider(int sampleRate = 48000, int channels = 2)
    {
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int sampleRate = _waveFormat.SampleRate;
        int channels = _waveFormat.Channels;
        int frames = count / channels;

        double subFreq = Math.Clamp(FrequencyHz, 18.0, 90.0);
        // Piloto ultrasónico en 19,200 Hz (pasa el filtro paso-alto del AUX del soundbar pero es inaudible)
        double ultraFreq = Math.Min(19200.0, sampleRate * 0.42);
        double targetAmplitude = Math.Clamp(VolumePercent / 100.0, 0.0001, 0.12);

        if (IsWakeBoostActive)
        {
            targetAmplitude = Math.Min(0.05, targetAmplitude * 2.5);
        }

        double phaseStepSub = 2.0 * Math.PI * subFreq / sampleRate;
        double phaseStepUltra = 2.0 * Math.PI * ultraFreq / sampleRate;
        double testPhaseStep = 2.0 * Math.PI * 440.0 / sampleRate;

        bool shouldEmit = IsTestToneActive || (IsEnabled && !SuppressedByIdleSleep && !SuppressedByRealAudio);
        IsCurrentlyEmitting = shouldEmit;

        double desiredGain = 0.0;
        if (IsTestToneActive)
        {
            desiredGain = 0.08;
        }
        else if (shouldEmit)
        {
            desiredGain = targetAmplitude;
        }

        double smoothing = 1.0 - Math.Exp(-1.0 / (sampleRate * 0.015));
        float maxSample = 0f;

        KeepAliveSignalType sigType = SignalType;

        for (int f = 0; f < frames; f++)
        {
            _currentGain += (desiredGain - _currentGain) * smoothing;

            float sampleValue = 0f;

            if (_currentGain > 1e-7)
            {
                if (IsTestToneActive)
                {
                    sampleValue = (float)(Math.Sin(_testPhase) * _currentGain);
                    _testPhase += testPhaseStep;
                    if (_testPhase >= 2.0 * Math.PI) _testPhase -= 2.0 * Math.PI;
                }
                else
                {
                    switch (sigType)
                    {
                        case KeepAliveSignalType.SineWave:
                            // Sub-grave + toque ultrasónico (75% sub + 25% 19.2kHz)
                            double sSub = Math.Sin(_phaseSub) * 0.75;
                            double sUlt = Math.Sin(_phaseUltra) * 0.25;
                            sampleValue = (float)((sSub + sUlt) * _currentGain);
                            break;

                        case KeepAliveSignalType.DualSubTone:
                            // Modo Híbrido Anti-Gate AUX: 55% Sub (38Hz) + 35% Ultrasónico (19.2kHz) + 10% Ruido Rosa Dither
                            double whiteD = _random.NextDouble() * 2.0 - 1.0;
                            _b0 = 0.99886 * _b0 + whiteD * 0.0555179;
                            double dither = (_b0 + whiteD * 0.2) * 0.08;
                            double hSub = Math.Sin(_phaseSub) * 0.55;
                            double hUlt = Math.Sin(_phaseUltra) * 0.35;
                            sampleValue = (float)((hSub + hUlt + dither) * _currentGain);
                            break;

                        case KeepAliveSignalType.PinkNoise:
                            double white = _random.NextDouble() * 2.0 - 1.0;
                            _b0 = 0.99886 * _b0 + white * 0.0555179;
                            _b1 = 0.99332 * _b1 + white * 0.0750759;
                            _b2 = 0.96900 * _b2 + white * 0.1538520;
                            _b3 = 0.86650 * _b3 + white * 0.3104856;
                            _b4 = 0.55000 * _b4 + white * 0.5329522;
                            _b5 = -0.7616 * _b5 - white * 0.0168980;
                            double pink = (_b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362) * 0.04;
                            _b6 = white * 0.115926;
                            sampleValue = (float)(pink * _currentGain);
                            break;
                    }
                }
            }

            _phaseSub += phaseStepSub;
            if (_phaseSub >= 2.0 * Math.PI) _phaseSub -= 2.0 * Math.PI;

            _phaseUltra += phaseStepUltra;
            if (_phaseUltra >= 2.0 * Math.PI) _phaseUltra -= 2.0 * Math.PI;

            float abs = Math.Abs(sampleValue);
            if (abs > maxSample) maxSample = abs;

            int baseIdx = offset + f * channels;
            for (int c = 0; c < channels; c++)
            {
                buffer[baseIdx + c] = sampleValue;
            }
        }

        _totalSamplesProcessed += frames;
        LastOutputPeak = maxSample;
        return count;
    }
}
