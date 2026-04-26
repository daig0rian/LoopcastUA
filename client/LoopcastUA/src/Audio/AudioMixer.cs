namespace LoopcastUA.Audio
{
    internal static class AudioMixer
    {
        public static float[] MixStereoToMono(float[] stereo)
        {
            var mono = new float[stereo.Length / 2];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5f;
            return mono;
        }
    }
}
