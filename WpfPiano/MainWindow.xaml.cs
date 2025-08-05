using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using InputDevice = Melanchall.DryWetMidi.Multimedia.InputDevice;
using Path = System.IO.Path;
using Window = System.Windows.Window;

namespace WpfSynthPiano
{
    public partial class MainWindow : Window
    {
        // Audio Engine
        private WaveOutEvent waveOut;
        private MixingSampleProvider mixer;
        private WaveFormat waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        private List<SignalGenerator> activeSignals = new List<SignalGenerator>();
        private Dictionary<double, Button> currentlyPlayingNotes = new Dictionary<double, Button>();
        private BufferedWaveProvider waveProvider;
        private float volume = 0.05f;

        // Oscilloscope
        private DispatcherTimer scopeTimer;
        private const int ScopeWidth = 600;
        private const int ScopeHeight = 150;
        private const int ScopePointCount = 400;
        private float[] scopeBuffer = new float[ScopePointCount];
        private Polyline scopeLine;

        // MIDI
        private OutputDevice midiOutDevice;
        private InputDevice midiInDevice;
        private bool midiEnabled = false;
        private string[] midiOutDevices;
        private string[] midiInDevices;

        // Piano UI
        private Dictionary<Button, string> keyLabels = new Dictionary<Button, string>();
        private Dictionary<Button, int> keyMidiNotes = new Dictionary<Button, int>();
        private bool isMouseDown = false;
        private Button lastPlayedKey = null;

        // Note configuration
        private readonly string[] whiteNotes = { "C", "D", "E", "F", "G", "A", "B" };
        private readonly string[] blackNotes = { "C#", "D#", "", "F#", "G#", "A#", "" };
        private int startOctave = 3;
        private int endOctave = 5;

        // Recording
        private List<TimedEvent> recordedEvents = new List<TimedEvent>();
        private bool isRecording = false;
        private DateTime recordingStartTime;
        private Playback currentPlayback;
        private CancellationTokenSource playbackCts;

        // Maps a frequency to its active SignalGenerator
        private Dictionary<double, SignalGenerator> activeNoteGenerators = new();

        // Song playback
        private DispatcherTimer songProgressTimer;
        private TimeSpan songDuration;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAudioEngine();
            InitializeOscilloscope();
            InitializeMidiDevices();
            InitializeInstruments();
            InitializeWaveTypes();
            InitializeOctaveControls();
            LoadSongsFromFolder();
            DrawKeyboard(startOctave, endOctave);

            PianoCanvas.PreviewMouseUp += (s, e) =>
            {
                if (isMouseDown)
                {
                    isMouseDown = false;
                    Mouse.Capture(null);
                    if (lastPlayedKey != null)
                    {
                        StopNote((double)lastPlayedKey.Tag, lastPlayedKey);
                        lastPlayedKey = null;
                    }
                }
            };

            PianoCanvas.PreviewMouseMove += PianoCanvas_PreviewMouseMove;

            songProgressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            songProgressTimer.Tick += SongProgressTimer_Tick;

        }

        private void PianoCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!isMouseDown) return;

            // Get the element under the cursor
            Point position = e.GetPosition(PianoCanvas);
            HitTestResult hit = VisualTreeHelper.HitTest(PianoCanvas, position);

            if (hit != null)
            {
                // Try to find a Button in the visual tree
                DependencyObject current = hit.VisualHit;

                while (current != null && current is not Button)
                {
                    current = VisualTreeHelper.GetParent(current);
                }

                if (current is Button button && button.Tag is double frequency)
                {
                    if (lastPlayedKey != null && lastPlayedKey != button)
                    {
                        StopNote((double)lastPlayedKey.Tag, lastPlayedKey);
                        lastPlayedKey = null; // Clear before playing the new
                    }

                    if (!currentlyPlayingNotes.ContainsKey(frequency))
                    {
                        PlayNote(frequency, button);
                    }
                }
            }
        }



        private void InitializeAudioEngine()
        {
            mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
            waveOut = new WaveOutEvent();

            waveProvider = new BufferedWaveProvider(waveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(100)
            };

            var sampleToWaveProvider = new SampleToWaveProvider(mixer);
            var tee = new TeeWaveProvider(sampleToWaveProvider, waveProvider);

            waveOut.Init(tee);
            waveOut.Play();
        }

        private void InitializeOscilloscope()
        {
            OscilloscopeCanvas.Children.Clear();
            scopeLine = new Polyline
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 1.5
            };
            OscilloscopeCanvas.Children.Add(scopeLine);

            scopeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
            scopeTimer.Tick += UpdateOscilloscope;
            scopeTimer.Start();
        }

        private void UpdateOscilloscope(object sender, EventArgs e)
        {
            if (waveProvider.BufferedBytes > 0)
            {
                byte[] buffer = new byte[ScopePointCount * 4];
                int bytesRead = waveProvider.Read(buffer, 0, buffer.Length);

                for (int i = 0; i < bytesRead / 4; i++)
                {
                    scopeBuffer[i] = BitConverter.ToSingle(buffer, i * 4);
                }

                scopeLine.Points.Clear();
                double xScale = OscilloscopeCanvas.ActualWidth / (double)ScopePointCount;

                for (int i = 0; i < ScopePointCount && i < bytesRead / 4; i++)
                {
                    double x = i * xScale;
                    double y = OscilloscopeCanvas.ActualHeight / 2 - scopeBuffer[i] * (OscilloscopeCanvas.ActualHeight / 2 * 0.8);
                    scopeLine.Points.Add(new Point(x, y));
                }
            }
        }

        private void InitializeMidiDevices()
        {
            try
            {
                midiOutDevices = Enumerable.Range(0, OutputDevice.GetDevicesCount())
                    .Select(i => OutputDevice.GetByIndex(i).Name)
                    .ToArray();

                midiInDevices = Enumerable.Range(0, InputDevice.GetDevicesCount())
                    .Select(i => InputDevice.GetByIndex(i).Name)
                    .ToArray();

                MidiOutComboBox.ItemsSource = midiOutDevices;
                MidiInComboBox.ItemsSource = midiInDevices;

                if (midiOutDevices.Length > 0)
                {
                    MidiOutComboBox.SelectedIndex = 0;
                    midiOutDevice = OutputDevice.GetByIndex(0);
                    midiEnabled = true;
                }

                if (midiInDevices.Length > 0)
                {
                    MidiInComboBox.SelectedIndex = 0;
                    midiInDevice = InputDevice.GetByIndex(0);
                    midiInDevice.EventReceived += OnMidiEventReceived;
                    midiInDevice.StartEventsListening();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MIDI initialization error: {ex.Message}");
            }
        }

        private void InitializeInstruments()
        {
            InstrumentComboBox.ItemsSource = Enum.GetNames(typeof(Instruments));
            InstrumentComboBox.SelectedIndex = 0;
        }

        private void InitializeWaveTypes()
        {
            WaveTypeComboBox.ItemsSource = Enum.GetValues(typeof(WaveTypes));
            WaveTypeComboBox.SelectedIndex = 0;
        }

        private void InitializeOctaveControls()
        {
            for (int i = 1; i <= 7; i++)
            {
                FromOctaveComboBox.Items.Add(i);
                TillOctaveComboBox.Items.Add(i);
            }
            FromOctaveComboBox.SelectedItem = startOctave;
            TillOctaveComboBox.SelectedItem = endOctave;
        }

        private void DrawKeyboard(int startOctave, int endOctave)
        {
            PianoCanvas.Children.Clear();
            keyLabels.Clear();
            keyMidiNotes.Clear();

            int whiteKeyWidth = 40;
            int whiteKeyHeight = 250;
            int blackKeyWidth = 25;
            int blackKeyHeight = 150;
            int xOffset = 0;

            // White keys
            for (int octave = startOctave; octave <= endOctave; octave++)
            {
                for (int i = 0; i < 7; i++)
                {
                    string note = whiteNotes[i] + octave;
                    double frequency = GetFrequency(note);
                    int midiNote = GetMidiNote(note);

                    var key = new Button
                    {
                        Width = whiteKeyWidth,
                        Height = whiteKeyHeight,
                        Background = Brushes.White,
                        BorderBrush = Brushes.Black,
                        Tag = frequency,
                        Content = ShowLabelsCheckbox.IsChecked == true ? note : null,
                        FontSize = 10
                    };

                    key.PreviewMouseDown += Key_PreviewMouseDown;
                    key.PreviewMouseUp += Key_PreviewMouseUp;
                    key.MouseEnter += Key_MouseEnter;
                    key.MouseLeave += Key_MouseLeave;

                    keyLabels[key] = note;
                    keyMidiNotes[key] = midiNote;

                    Canvas.SetLeft(key, xOffset);
                    Canvas.SetTop(key, 0);
                    PianoCanvas.Children.Add(key);

                    xOffset += whiteKeyWidth;
                }
            }

            // Black keys
            xOffset = 0;
            for (int octave = startOctave; octave <= endOctave; octave++)
            {
                for (int i = 0; i < 7; i++)
                {
                    if (blackNotes[i] == "") continue;

                    string note = blackNotes[i] + octave;
                    double frequency = GetFrequency(note);
                    int midiNote = GetMidiNote(note);

                    var blackKey = new Button
                    {
                        Width = blackKeyWidth,
                        Height = blackKeyHeight,
                        Background = Brushes.Black,
                        Foreground = Brushes.White,
                        BorderBrush = Brushes.Gray,
                        Tag = frequency,
                        Content = ShowLabelsCheckbox.IsChecked == true ? note : null,
                        FontSize = 10
                    };

                    blackKey.PreviewMouseDown += Key_PreviewMouseDown;
                    blackKey.PreviewMouseUp += Key_PreviewMouseUp;
                    blackKey.MouseEnter += Key_MouseEnter;
                    blackKey.MouseLeave += Key_MouseLeave;

                    keyLabels[blackKey] = note;
                    keyMidiNotes[blackKey] = midiNote;

                    double left = (i + (octave - startOctave) * 7) * whiteKeyWidth;
                    left += whiteKeyWidth - (blackKeyWidth / 2);

                    if (i == 2 || i == 6) continue;

                    Canvas.SetLeft(blackKey, left);
                    Canvas.SetTop(blackKey, 0);
                    Panel.SetZIndex(blackKey, 1);
                    PianoCanvas.Children.Add(blackKey);
                }
            }
        }

        private int GetMidiNote(string note)
        {
            string noteName = note.Substring(0, note.Length - 1);
            int octave = int.Parse(note.Substring(note.Length - 1, 1));

            Dictionary<string, int> semitoneOffsets = new Dictionary<string, int>
            {
                {"C", 0}, {"C#", 1}, {"D", 2}, {"D#", 3}, {"E", 4},
                {"F", 5}, {"F#", 6}, {"G", 7}, {"G#", 8}, {"A", 9},
                {"A#", 10}, {"B", 11}
            };

            return (octave + 1) * 12 + semitoneOffsets[noteName];
        }

        private double GetFrequency(string note)
        {
            string noteName = note.Substring(0, note.Length - 1);
            int octave = int.Parse(note.Substring(note.Length - 1, 1));

            Dictionary<string, int> semitoneOffsets = new Dictionary<string, int>
            {
                {"C", 0}, {"C#", 1}, {"D", 2}, {"D#", 3}, {"E", 4},
                {"F", 5}, {"F#", 6}, {"G", 7}, {"G#", 8}, {"A", 9},
                {"A#", 10}, {"B", 11}
            };

            int noteNumber = (octave + 1) * 12 + semitoneOffsets[noteName];
            return 440.0 * Math.Pow(2, (noteNumber - 69) / 12.0);
        }

        private SignalGeneratorType GetCurrentWaveType()
        {
            if (WaveTypeComboBox.SelectedItem is WaveTypes selectedWave)
            {
                return selectedWave switch
                {
                    WaveTypes.Sin => SignalGeneratorType.Sin,
                    WaveTypes.Square => SignalGeneratorType.Square,
                    WaveTypes.SawTooth => SignalGeneratorType.SawTooth,
                    WaveTypes.Triangle => SignalGeneratorType.Triangle,
                    _ => SignalGeneratorType.Sin
                };
            }
            return SignalGeneratorType.Sin;
        }

        private void PlayNote(double frequency, Button key)
        {
            if (currentlyPlayingNotes.ContainsKey(frequency)) return;

            var signal = new SignalGenerator(waveFormat.SampleRate, waveFormat.Channels)
            {
                Gain = volume,
                Frequency = frequency,
                Type = GetCurrentWaveType()
            };

            mixer.AddMixerInput(signal);
            currentlyPlayingNotes[frequency] = key;
            activeNoteGenerators[frequency] = signal;

            if (midiEnabled && midiOutDevice != null && keyMidiNotes.ContainsKey(key))
            {
                var noteOnEvent = new NoteOnEvent((SevenBitNumber)keyMidiNotes[key], (SevenBitNumber)127);
                midiOutDevice.SendEvent(noteOnEvent);
            }

            if (key.Background == Brushes.White)
                key.Background = new SolidColorBrush(Color.FromRgb(210, 210, 210));
            else if (key.Background == Brushes.Black)
                key.Background = new SolidColorBrush(Color.FromRgb(80, 80, 80));

            lastPlayedKey = key;

            if (isRecording && keyMidiNotes.ContainsKey(key))
            {
                long deltaTime = (long)(DateTime.Now - recordingStartTime).TotalMilliseconds;
                recordedEvents.Add(new TimedEvent(
                    new NoteOnEvent((SevenBitNumber)keyMidiNotes[key], (SevenBitNumber)127),
                    deltaTime));
            }
        }

        private void StopNote(double frequency, Button key)
        {
            // Only stop if the note is actively playing
            if (!currentlyPlayingNotes.ContainsKey(frequency) || !activeNoteGenerators.ContainsKey(frequency))
                return;

            // Remove signal from mixer
            var signal = activeNoteGenerators[frequency];
            mixer.RemoveMixerInput(signal);

            // Clean up tracking
            activeNoteGenerators.Remove(frequency);
            currentlyPlayingNotes.Remove(frequency);

            // Send MIDI NoteOff
            if (midiEnabled && midiOutDevice != null && keyMidiNotes.ContainsKey(key))
            {
                var noteOffEvent = new NoteOffEvent((SevenBitNumber)keyMidiNotes[key], (SevenBitNumber)0);
                midiOutDevice.SendEvent(noteOffEvent);
            }

            // Restore original color
            if (IsBlackKey(frequency))
                key.Background = Brushes.Black;
            else
                key.Background = Brushes.White;

            // Record NoteOff if recording
            if (isRecording && keyMidiNotes.ContainsKey(key))
            {
                long deltaTime = (long)(DateTime.Now - recordingStartTime).TotalMilliseconds;
                recordedEvents.Add(new TimedEvent(
                    new NoteOffEvent((SevenBitNumber)keyMidiNotes[key], (SevenBitNumber)0),
                    deltaTime));
            }
        }
        private bool IsBlackKey(double frequency)
        {
            int midiNote = FrequencyToMidiNote(frequency);
            int noteInOctave = midiNote % 12;

            // These positions in the octave are black keys
            return noteInOctave == 1 || noteInOctave == 3 || noteInOctave == 6 ||
                   noteInOctave == 8 || noteInOctave == 10;
        }


        private int FrequencyToMidiNote(double frequency)
        {
            return (int)Math.Round(69 + 12 * Math.Log(frequency / 440.0, 2));
        }




        private void StopAllNotes()
        {
            foreach (var signal in activeSignals.ToList())
            {
                mixer.RemoveMixerInput(signal);
                activeSignals.Remove(signal);
            }
            currentlyPlayingNotes.Clear();

            if (midiEnabled && midiOutDevice != null)
            {
                foreach (var midiNote in keyMidiNotes.Values)
                {
                    var noteOffEvent = new NoteOffEvent((SevenBitNumber)midiNote, (SevenBitNumber)0);
                    midiOutDevice.SendEvent(noteOffEvent);
                }
            }

            // Reset all key colors
            foreach (var key in keyLabels.Keys)
            {
                if (key.Background == Brushes.White || key.Background.ToString() == "#FFD2D2D2")
                    key.Background = Brushes.White;
                else if (key.Background == Brushes.Black || key.Background.ToString() == "#FF505050")
                    key.Background = Brushes.Black;
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeValueLabel != null)
            {
                volume = (float)VolumeSlider.Value;
                VolumeValueLabel.Content = $"{volume * 100:0}%";
            }
        }

        private void Key_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            isMouseDown = true;
            var button = (Button)sender;

            if (button.Tag is double frequency)
            {
                PlayNote(frequency, button);
                Mouse.Capture(PianoCanvas); // Add this line to capture mouse
            }
        }


        private void Key_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            isMouseDown = false;
            Mouse.Capture(null); // Release capture

            var button = (Button)sender;

            if (button.Tag is double frequency)
            {
                StopNote(frequency, button);
            }

            lastPlayedKey = null;
        }

        private void Key_MouseEnter(object sender, MouseEventArgs e)
        {
            if (isMouseDown && sender is Button button && button.Tag is double frequency)
            {
                if (lastPlayedKey != null && lastPlayedKey != button)
                {
                    StopNote((double)lastPlayedKey.Tag, lastPlayedKey);
                }

                PlayNote(frequency, button);
            }
        }





        private void Key_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isMouseDown && sender is Button button && button.Tag is double frequency)
            {
                StopNote(frequency, button);
            }
        }

        private void OnMidiEventReceived(object sender, MidiEventReceivedEventArgs e)
        {
            var midiDevice = (MidiDevice)sender;
            Debug.WriteLine($"Event received from '{midiDevice.Name}' at {DateTime.Now}: {e.Event}");

            if (e.Event.EventType == MidiEventType.NoteOn)
            {
                var noteOnEvent = (NoteOnEvent)e.Event;
                Dispatcher.Invoke(() => HandleMidiNoteOn(noteOnEvent));
            }
            else if (e.Event.EventType == MidiEventType.NoteOff)
            {
                var noteOffEvent = (NoteOffEvent)e.Event;
                Dispatcher.Invoke(() => HandleMidiNoteOff(noteOffEvent));
            }
        }

        private void HandleMidiNoteOn(NoteOnEvent noteEvent)
        {
            foreach (var key in keyMidiNotes)
            {
                if (key.Value == noteEvent.NoteNumber)
                {
                    PlayNote((double)key.Key.Tag, key.Key);
                    break;
                }
            }
        }

        private void HandleMidiNoteOff(NoteOffEvent noteEvent)
        {
            foreach (var key in keyMidiNotes)
            {
                if (key.Value == noteEvent.NoteNumber)
                {
                    StopNote((double)key.Key.Tag, key.Key);
                    break;
                }
            }
        }

        private void ToggleLabels(object sender, RoutedEventArgs e)
        {
            bool show = ShowLabelsCheckbox.IsChecked == true;
            foreach (var kvp in keyLabels)
            {
                kvp.Key.Content = show ? kvp.Value : null;
            }
        }

        private void InstrumentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (midiEnabled && midiOutDevice != null && InstrumentComboBox.SelectedIndex >= 0)
            {
                var programChangeEvent = new ProgramChangeEvent((SevenBitNumber)InstrumentComboBox.SelectedIndex);
                midiOutDevice.SendEvent(programChangeEvent);
            }
        }

        private void WaveTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var signal in activeSignals.ToList())
            {
                signal.Type = GetCurrentWaveType();
            }
        }

        private void MidiOutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MidiOutComboBox.SelectedIndex >= 0)
            {
                midiOutDevice?.Dispose();
                midiOutDevice = OutputDevice.GetByIndex(MidiOutComboBox.SelectedIndex);
                midiEnabled = true;
            }
        }

        private void MidiInComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MidiInComboBox.SelectedIndex >= 0)
            {
                midiInDevice?.Dispose();
                midiInDevice = InputDevice.GetByIndex(MidiInComboBox.SelectedIndex);
                midiInDevice.EventReceived += OnMidiEventReceived;
                midiInDevice.StartEventsListening();
            }
        }

        private void OctaveRangeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FromOctaveComboBox.SelectedItem != null && TillOctaveComboBox.SelectedItem != null)
            {
                int newStart = (int)FromOctaveComboBox.SelectedItem;
                int newEnd = (int)TillOctaveComboBox.SelectedItem;

                if (newStart > newEnd)
                {
                    MessageBox.Show("Start octave cannot be higher than end octave");
                    FromOctaveComboBox.SelectedItem = startOctave;
                    TillOctaveComboBox.SelectedItem = endOctave;
                    return;
                }

                startOctave = newStart;
                endOctave = newEnd;
                DrawKeyboard(startOctave, endOctave);
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            isRecording = !isRecording;

            if (isRecording)
            {
                recordedEvents.Clear();
                recordingStartTime = DateTime.Now;
                RecordButton.Content = "Stop Recording";
                PlayButton.IsEnabled = false;
            }
            else
            {
                RecordButton.Content = "Start Recording";
                SaveRecordingToMidiFile();
                PlayButton.IsEnabled = true;
            }
        }

        private void SaveRecordingToMidiFile()
        {
            if (recordedEvents.Count == 0) return;

            try
            {
                recordedEvents.Sort((a, b) => a.Time.CompareTo(b.Time));

                var trackChunk = new TrackChunk();
                trackChunk.Events.Add(new SetTempoEvent(500000)); // 120 BPM

                const double msToTicks = 96.0 / 500.0; // 96 ticks per quarter note at 120 BPM
                long previousTime = 0;

                foreach (var timedEvent in recordedEvents)
                {
                    long deltaTimeMs = timedEvent.Time - previousTime;
                    long deltaTimeTicks = (long)(deltaTimeMs * msToTicks);

                    var midiEvent = (MidiEvent)timedEvent.Event.Clone();
                    midiEvent.DeltaTime = deltaTimeTicks;
                    trackChunk.Events.Add(midiEvent);
                    previousTime = timedEvent.Time;
                }

                trackChunk.Events.Add(new NormalSysExEvent(new byte[] { 0x2F, 0x00 }));

                var midiFile = new MidiFile(trackChunk)
                {
                    TimeDivision = new TicksPerQuarterNoteTimeDivision(96)
                };

                midiFile.Write("Recording.mid", overwriteFile: true);
                MessageBox.Show("MIDI recording saved to 'Recording.mid'.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save MIDI: {ex.Message}");
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (currentPlayback != null && currentPlayback.IsRunning)
                {
                    await StopPlaybackAsync();
                    PlayButton.Content = "Play";
                    return;
                }

                if (!System.IO.File.Exists("Recording.mid"))
                {
                    MessageBox.Show("No recording found to play.");
                    return;
                }

                PlayButton.Content = "Loading...";
                PlayButton.IsEnabled = false;
                playbackCts = new CancellationTokenSource();

                await Task.Run(async () =>
                {
                    try
                    {
                        var midiFile = MidiFile.Read("Recording.mid");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            currentPlayback = midiFile.GetPlayback(midiOutDevice);
                            currentPlayback.Speed = 1.0;
                            currentPlayback.TrackNotes = true;

                            currentPlayback.Finished += (s, args) => Dispatcher.InvokeAsync(() => CleanUpPlayback());
                            currentPlayback.NotesPlaybackStarted += OnNotesPlaybackStarted;
                            currentPlayback.NotesPlaybackFinished += OnNotesPlaybackFinished;

                            PlayButton.Content = "Stop";
                            PlayButton.IsEnabled = true;
                            currentPlayback.Start();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // Playback was cancelled
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"Failed to play MIDI: {ex.Message}");
                            CleanUpPlayback();
                        });
                    }
                }, playbackCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Playback error: {ex.Message}");
                CleanUpPlayback();
            }
        }

        private async Task StopPlaybackAsync()
        {
            if (currentPlayback != null)
            {
                if (currentPlayback.IsRunning)
                {
                    currentPlayback.Stop();
                    await Task.Delay(50);
                }

                currentPlayback.Dispose();
                currentPlayback = null;
            }

            playbackCts?.Cancel();
            StopAllNotes();
        }

        private void CleanUpPlayback()
        {
            _ = StopPlaybackAsync();
            PlayButton.Content = "Play";
            PlayButton.IsEnabled = true;
        }

        private void OnNotesPlaybackStarted(object sender, NotesEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var note in args.Notes)
                {
                    foreach (var key in keyMidiNotes)
                    {
                        if (key.Value == note.NoteNumber)
                        {
                            key.Key.Background = key.Key.Background == Brushes.White
                                ? new SolidColorBrush(Color.FromRgb(210, 210, 210))
                                : new SolidColorBrush(Color.FromRgb(80, 80, 80));
                            break;
                        }
                    }
                }
            });
        }

        private void OnNotesPlaybackFinished(object sender, NotesEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var note in args.Notes)
                {
                    foreach (var key in keyMidiNotes)
                    {
                        if (key.Value == note.NoteNumber)
                        {
                            key.Key.Background = key.Key.Background.ToString() == "#FFD2D2D2" || key.Key.Background == Brushes.White
                                ? Brushes.White
                                : Brushes.Black;
                            break;
                        }
                    }
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            playbackCts?.Cancel();
            _ = StopPlaybackAsync();

            scopeTimer?.Stop();
            waveOut?.Dispose();
            midiOutDevice?.Dispose();
            midiInDevice?.Dispose();

            base.OnClosed(e);
        }

        private void LoadSongsFromFolder()
        {
            try
            {
                string songsFolder = "songs";
                if (!Directory.Exists(songsFolder))
                {
                    Directory.CreateDirectory(songsFolder);
                }

                var midiFiles = Directory.GetFiles(songsFolder, "*.mid")
                                         .Select(Path.GetFileName)
                                         .ToList();

                SongsListView.ItemsSource = midiFiles;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load songs: {ex.Message}");
            }
        }

        private async void PlaySongButton_Click(object sender, RoutedEventArgs e)
        {
            if (SongsListView.SelectedItem is not string selectedSong)
            {
                MessageBox.Show("Please select a song to play.");
                return;
            }

            try
            {
                if (currentPlayback != null && currentPlayback.IsRunning)
                {
                    await StopPlaybackAsync();
                    PlaySongButton.Content = "Play Song";
                    return;
                }

                string path = Path.Combine("songs", selectedSong);

                if (!File.Exists(path))
                {
                    MessageBox.Show($"File not found: {path}");
                    return;
                }

                PlaySongButton.Content = "Loading...";
                PlaySongButton.IsEnabled = false;
                playbackCts = new CancellationTokenSource();

                await Task.Run(() =>
                {
                    try
                    {
                        var midiFile = MidiFile.Read(path);

                        Dispatcher.Invoke(() =>
                        {
                            currentPlayback = midiFile.GetPlayback(midiOutDevice);
                            currentPlayback.Speed = 1.0;
                            currentPlayback.TrackNotes = true;

                            currentPlayback.Finished += (s, args) => Dispatcher.Invoke(() => CleanUpSongPlayback());
                            currentPlayback.NotesPlaybackStarted += OnNotesPlaybackStarted;
                            currentPlayback.NotesPlaybackFinished += OnNotesPlaybackFinished;

                            PlaySongButton.Content = "Stop Song";
                            PlaySongButton.IsEnabled = true;
                            currentPlayback.Start();

                            songDuration = currentPlayback.GetDuration<MetricTimeSpan>();
                            SongProgressBar.Value = 0;
                            SongProgressBar.Maximum = songDuration.TotalSeconds;
                            SongTimeText.Text = $"00:00 / {songDuration:mm\\:ss}";

                            songProgressTimer.Start();
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to play song: {ex.Message}");
                            CleanUpSongPlayback();
                        });
                    }
                }, playbackCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing song: {ex.Message}");
                CleanUpSongPlayback();
            }
        }

        private void SongProgressTimer_Tick(object sender, EventArgs e)
        {
            if (currentPlayback == null || !currentPlayback.IsRunning)
                return;

            var currentTime = currentPlayback.GetCurrentTime<MetricTimeSpan>();
            var elapsed = TimeSpan.FromMilliseconds(currentTime.TotalMilliseconds);

            SongProgressBar.Value = Math.Min(elapsed.TotalSeconds, songDuration.TotalSeconds);
            SongTimeText.Text = $"{elapsed:mm\\:ss} / {songDuration:mm\\:ss}";
        }


        private void CleanUpSongPlayback()
        {
            _ = StopPlaybackAsync();
            PlaySongButton.Content = "Play Song";
            PlaySongButton.IsEnabled = true;
            songProgressTimer.Stop();
            SongProgressBar.Value = 0;
            SongTimeText.Text = $"00:00 / 00:00";
        }
    }

    public enum WaveTypes
    {
        Sin,
        Square,
        SawTooth,
        Triangle,
        White,
        Pink,
        Sweep
    }

    // Instruments
    public enum Instruments
    {
        AcousticGrandPiano = 0,
        BrightAcousticPiano = 1,
        ElectricGrandPiano = 2,
        HonkyTonkPiano = 3,
        ElectricPiano1 = 4,
        ElectricPiano2 = 5,
        Harpsichord = 6,
        Clavinet = 7,

        Celesta = 8,
        Glockenspiel = 9,
        MusicBox = 10,
        Vibraphone = 11,
        Marimba = 12,
        Xylophone = 13,
        TubularBells = 14,
        Dulcimer = 15,

        DrawbarOrgan = 16,
        PercussiveOrgan = 17,
        RockOrgan = 18,
        ChurchOrgan = 19,
        ReedOrgan = 20,
        Accordion = 21,
        Harmonica = 22,
        TangoAccordion = 23,

        AcousticGuitarNylon = 24,
        AcousticGuitarSteel = 25,
        ElectricGuitarJazz = 26,
        ElectricGuitarClean = 27,
        ElectricGuitarMuted = 28,
        OverdrivenGuitar = 29,
        DistortionGuitar = 30,
        GuitarHarmonics = 31,

        AcousticBass = 32,
        ElectricBassFinger = 33,
        ElectricBassPick = 34,
        FretlessBass = 35,
        SlapBass1 = 36,
        SlapBass2 = 37,
        SynthBass1 = 38,
        SynthBass2 = 39,

        Violin = 40,
        Viola = 41,
        Cello = 42,
        Contrabass = 43,
        TremoloStrings = 44,
        PizzicatoStrings = 45,
        OrchestralHarp = 46,
        Timpani = 47,

        StringEnsemble1 = 48,
        StringEnsemble2 = 49,
        SynthStrings1 = 50,
        SynthStrings2 = 51,
        ChoirAahs = 52,
        VoiceOohs = 53,
        SynthVoice = 54,
        OrchestraHit = 55,

        Trumpet = 56,
        Trombone = 57,
        Tuba = 58,
        MutedTrumpet = 59,
        FrenchHorn = 60,
        BrassSection = 61,
        SynthBrass1 = 62,
        SynthBrass2 = 63,

        SopranoSax = 64,
        AltoSax = 65,
        TenorSax = 66,
        BaritoneSax = 67,
        Oboe = 68,
        EnglishHorn = 69,
        Bassoon = 70,
        Clarinet = 71,

        Piccolo = 72,
        Flute = 73,
        Recorder = 74,
        PanFlute = 75,
        BlownBottle = 76,
        Shakuhachi = 77,
        Whistle = 78,
        Ocarina = 79,

        Lead1Square = 80,
        Lead2Sawtooth = 81,
        Lead3Calliope = 82,
        Lead4Chiff = 83,
        Lead5Charang = 84,
        Lead6Voice = 85,
        Lead7Fifths = 86,
        Lead8BassLead = 87,

        Pad1NewAge = 88,
        Pad2Warm = 89,
        Pad3Polysynth = 90,
        Pad4Choir = 91,
        Pad5Bowed = 92,
        Pad6Metallic = 93,
        Pad7Halo = 94,
        Pad8Sweep = 95,

        FX1Rain = 96,
        FX2Soundtrack = 97,
        FX3Crystal = 98,
        FX4Atmosphere = 99,
        FX5Brightness = 100,
        FX6Goblins = 101,
        FX7Echoes = 102,
        FX8SciFi = 103,

        Sitar = 104,
        Banjo = 105,
        Shamisen = 106,
        Koto = 107,
        Kalimba = 108,
        Bagpipe = 109,
        Fiddle = 110,
        Shanai = 111,

        TinkleBell = 112,
        Agogo = 113,
        SteelDrums = 114,
        Woodblock = 115,
        TaikoDrum = 116,
        MelodicTom = 117,
        SynthDrum = 118,
        ReverseCymbal = 119,

        GuitarFretNoise = 120,
        BreathNoise = 121,
        Seashore = 122,
        BirdTweet = 123,
        TelephoneRing = 124,
        Helicopter = 125,
        Applause = 126,
        Gunshot = 127
    }
}