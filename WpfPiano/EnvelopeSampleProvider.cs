
using System;
using NAudio.Wave;

namespace WpfSynthPiano.Audio
{
    public class EnvelopeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly WaveFormat waveFormat;

        private float currentAmplitude = 0f;
        private bool isFinished = false;

        public float AttackSeconds { get; set; } = 0f;
        public float ReleaseSeconds { get; set; } = 0.5f;
        public float SustainLevel { get; set; } = 0.5f;
        public float HoldSeconds { get; set; } = 0.0f;

        private int sampleRate;
        private int totalSamples;
        private int attackSamples;
        private int holdSamples;
        private int releaseSamples;

        private int samplePosition = 0;

        public EnvelopeSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.waveFormat = source.WaveFormat;
            sampleRate = waveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => waveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (isFinished) return 0;

            float[] sourceBuffer = new float[count];
            int samplesRead = source.Read(sourceBuffer, 0, count);

            attackSamples = (int)(AttackSeconds * sampleRate);
            holdSamples = (int)(HoldSeconds * sampleRate);
            releaseSamples = (int)(ReleaseSeconds * sampleRate);

            totalSamples = attackSamples + holdSamples + releaseSamples;

            for (int n = 0; n < samplesRead; n++)
            {
                float amplitude = SustainLevel;

                if (samplePosition < attackSamples)
                {
                    amplitude = (float)samplePosition / attackSamples * SustainLevel;
                }
                else if (samplePosition < attackSamples + holdSamples)
                {
                    amplitude = SustainLevel;
                }
                else if (samplePosition < totalSamples)
                {
                    float releasePos = samplePosition - attackSamples - holdSamples;
                    amplitude = SustainLevel * (1 - (releasePos / releaseSamples));
                }
                else
                {
                    amplitude = 0f;
                    isFinished = true;
                    break;
                }

                buffer[offset + n] = sourceBuffer[n] * amplitude;
                samplePosition++;
            }

            return samplesRead;
        }
    }
}
