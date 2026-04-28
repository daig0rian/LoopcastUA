using System;

namespace LoopcastUA.Audio
{
    internal interface ILoopbackCapturer : IDisposable
    {
        event EventHandler<float[]> StereoFrameReady;
        void Start();
        void Stop();
    }
}
