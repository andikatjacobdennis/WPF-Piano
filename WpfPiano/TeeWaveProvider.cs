using NAudio.Wave;

namespace WpfSynthPiano
{
    public class TeeWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider source;
        private readonly BufferedWaveProvider tap;

        public TeeWaveProvider(IWaveProvider source, BufferedWaveProvider tap)
        {
            this.source = source;
            this.tap = tap;
            this.WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = source.Read(buffer, offset, count);
            // Copy to oscilloscope buffer
            tap.AddSamples(buffer, offset, bytesRead);
            return bytesRead;
        }
    }

}