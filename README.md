# 🎹 WPF Piano

**WPF Piano** is a virtual synthesizer and piano application built with **C#** and **WPF (.NET 9+)**. It offers real-time audio synthesis, MIDI support, waveform selection, note recording/playback, and a live oscilloscope. Perfect for hobbyists, music learners, and developers exploring audio DSP or modern desktop UI.

![screenshot](screenshot.png)

---

## ✨ Features

* ✅ **Interactive Piano UI** with mouse/touch support
* 🔊 **Waveform Synthesizer**: Sine, Square, Triangle, Sawtooth
* 🎚️ **Per-Waveform Volume Control**
* 🎵 **MIDI In & Out Support**
* 🧠 **Real-Time Oscilloscope**
* 📼 **Note Recording & Playback**
* 🎛️ **Octave Range Selector**
* 🎹 Show/Hide **Note Labels**
* 📁 **Load and Play MIDI Songs** from `/songs` folder
* 🎨 Stylish dark-themed UI

---

## 🚀 Getting Started

### Requirements

* Windows OS
* [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
* Visual Studio 2022 or newer (with WPF and .NET desktop workloads)

### How to Run

```bash
git clone https://github.com/andikatjacobdennis/WPF-Piano.git
cd WPF-Piano
```

1. Open the `.sln` file in **Visual Studio 2022+**.
2. Ensure the target framework is `.NET 9` and WPF is enabled in `.csproj`.
3. Build and run the project using **F5** or **Ctrl+F5**.

---

## 🎛️ Controls Overview

| Control              | Description                                 |
| -------------------- | ------------------------------------------- |
| **Instrument**       | (Placeholder) Instrument presets            |
| **Wave Type**        | Choose waveform shape for synthesis         |
| **Wave Volume**      | Adjust volume per waveform                  |
| **From/Till Octave** | Customize the keyboard range                |
| **MIDI In/Out**      | Select MIDI devices                         |
| **Show Note Labels** | Display note names on keys                  |
| **Record / Play**    | Record and playback your own note sequences |
| **Song List**        | Load & play `.mid` files from `/songs`      |

---

## 🎵 Usage Tips

* Click or drag across keys to play melodies.
* Use waveform dropdown + volume slider to design your sound.
* Connect a MIDI controller via **MIDI In** for live playing.
* Use **MIDI Out** to route playback to external synths or DAWs.
* Press **Start Recording**, play, then **Play Recording** to replay.
* Drop MIDI files into the `/songs` folder to play them in-app.

---

## 🔧 To Do / Ideas

* 🎚️ Add **master volume control**
* 🎧 Limit **polyphony** for performance
* 📈 Improve oscilloscope performance
* 🎼 Export recorded sequences to **custom-named MIDI files**
* 📱 Improve touch support for tablets
* 🖱️ Add keyboard shortcuts for notes or actions

---

## 🤝 Contributing

Pull requests are welcome! Fork the project, create a branch, and open a PR with your changes or ideas. Bug fixes, refactors, and feature enhancements are all appreciated.

---

## 📄 License

MIT License
© [Andikat Jacob Dennis](https://github.com/andikatjacobdennis)

---

## 🙌 Acknowledgments

* Built with love for music and modern UI development
* Powered by [NAudio](https://github.com/naudio/NAudio) and [DryWetMIDI](https://github.com/melanchall/drywetmidi)
* Inspired by real-world digital pianos and virtual synths
