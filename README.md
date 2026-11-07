<div align="center">

# ⚡ AudioAwake

### **Stop Soundbar & Studio Monitor Auto-Standby Audio Cut-Offs on Windows 10 / 11**
*Evita que tu barra de sonido o monitores de estudio se apaguen en silencio y corten el primer segundo de audio.*

[![Release](https://img.shields.io/badge/Release-v1.0.0-0ea5e9?style=for-the-badge)](../../releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-2563eb?style=for-the-badge)](#)
[![CPU Usage](https://img.shields.io/badge/CPU%20Usage-%3C%200.05%25-10b981?style=for-the-badge)](#)
[![Author](https://img.shields.io/badge/By-AstoStudio-6366f1?style=for-the-badge)](#)

<br/>

![AudioAwake Banner EN](ImagenesTienda/3_Banner_Tienda_1920x1080_EN.png)

<br/>

### [⬇️ **Download AudioAwake v1.0.0 (Portable Windows x64 ZIP)**](../../releases/latest)

</div>

---

## 🇺🇸 English Overview

### **The Problem**
Many modern soundbars (**Hisense HS2100/AX, Samsung, JBL Bar, Creative Stage, Sony, LG, Bose**) and active studio monitors (**KRK Rokit, Edifier, PreSonus, Yamaha**) feature an aggressive hardware **Auto-Standby** and **AUX Noise-Gate** sensor.

Whenever your PC is silent for a few seconds, the speaker shuts down its internal Class-D amplifier. When a new sound plays (a YouTube video, game footsteps, or notification), the speaker takes **0.5 to 1.5 seconds** to wake up—cutting off the beginning of your audio.

### **The Solution: AudioAwake**
**AudioAwake** generates a real-time, mathematically synthesized **Hybrid Anti-Gate Pilot (`38 Hz Sub-Carrier + 19.2 kHz Ultrasonic Pilot + Micro-Dither`)** that passes through the speaker's internal high-pass filter to keep the amplifier stage permanently engaged—while remaining **100% inaudible to the human ear**.

### ✨ Key Features
- 🎯 **Brand & Connection Presets:** Pre-calibrated profiles for *Hisense, Samsung, LG, Sony, JBL, Creative, Bose, Studio Monitors (KRK/Edifier)*, and *Optical SPDIF / Bluetooth* outputs.
- 🎚️ **5-Level Anti-Gate Power Dial (`Lv.1` to `Lv.5`):** Fine-tune the keep-alive signal strength to match your USB audio interface knob (*Behringer U-Phoria UMC22, Focusrite Scarlett*, etc.).
- 🌙 **Smart Eco-Sleep + Instant Mouse Wake-Boost:** Allows your soundbar to rest and save electricity when you leave your PC for `15 / 30 / 60 / 120 minutes`, and wakes it up the exact millisecond you move your mouse again.
- 🔴 **1-Click System Tray Toggle & Custom Global Hotkey:** Left-click the tray icon once to pause protection (turns **Red**) or resume (turns **Blue**), or trigger it from any game with your custom shortcut (`Ctrl + Alt + A` by default).
- 🎬 **OBS & Screen-Recording Safe:** Zero audible interference in recordings or streams, using `< 0.05%` CPU and zero disk reads.

<div align="center">
  <img src="ImagenesTienda/2_Screenshot_App_English.png" width="760" alt="AudioAwake UI English" />
</div>

---

## 🇪🇸 Descripción en Español

<div align="center">
  <img src="ImagenesTienda/4_Banner_Tienda_1920x1080_ES.png" alt="AudioAwake Banner ES" />
</div>

### **¿Qué soluciona AudioAwake?**
Cuando conectas una barra de sonido (como **Hisense HS2100, Samsung, JBL, Sony, LG, Creative**) o monitores de estudio (**KRK Rokit, Edifier, PreSonus**) a tu PC o interfaz de audio (**Behringer UMC22, Focusrite**, etc.) por cable **AUX, Óptico o Bluetooth**, la puerta de ruido (*Noise-Gate*) y el ahorro de energía apagan el amplificador tras unos segundos de silencio. Al reproducir un nuevo audio, se pierde el primer medio segundo mientras la barra "despierta".

**AudioAwake** mantiene el amplificador interno siempre activo sin emitir ningún ruido audible.

### ✨ Características Principales
- 🎯 **Perfiles por Marca y Conexión:** Ajustes listos para *Hisense, Samsung, LG, Sony, JBL, Monitores de Estudio* y *Cable Óptico (SPDIF)*.
- 🎚️ **Dial Anti-Gate de 5 Niveles (`Lv.1` a `Lv.5`):** Supera la puerta de ruido rápida de entradas AUX analógicas e interfaces USB.
- 🌙 **Modo Ahorro Inteligente (`Eco Sleep`) + `Wake-Boost`:** Deja descansar tu barra si te alejas de la PC por `15–120 min` y la despierta en el milisegundo en que vuelves a mover el mouse.
- 🔴 **Control en 1 Clic + Tecla Rápida Global:** Haz 1 clic en el ícono junto al reloj para apagar (se pone en **Rojo**) o activar, o configura tu propia tecla rápida.
- 🌐 **Bilingüe Instantáneo:** Cambia entre Español e Inglés con un clic.

<div align="center">
  <img src="ImagenesTienda/1_Screenshot_App_Espanol.png" width="760" alt="AudioAwake UI Español" />
</div>

---

## 🚀 Quick Start / Instalación Rápida

1. Go to [**Releases**](../../releases/latest) and download **`AudioAwake_v1.0.0_Windows_x64.zip`**.
2. Extract the folder anywhere on your PC and run **`AudioAwake.exe`** *(self-contained — no extra `.NET` installation required)*.
3. Select your **Output Device (1)**, your **Speaker Brand (2)**, and choose **`Lv.2 (AUX+)`** or **`Lv.3`** on the right dial.
4. Enable **Auto-Start** and click **Minimize to System Tray**.

---

<div align="center">
  Developed with ❤️ by <b>AstoStudio</b>
</div>
