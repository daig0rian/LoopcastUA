using System;
using SIPSorcery.Net;

namespace LoopcastUA.Sip
{
    internal sealed class RtpSender : IDisposable
    {
        private const uint FrameDuration = 960; // 20ms at 48kHz

        private RTPSession _session;
        private volatile bool _connected;

        public void Attach(RTPSession session)
        {
            _session = session;
            _connected = false;
        }

        public void SetConnected()
        {
            _connected = true;
        }

        public void Detach()
        {
            _connected = false;
            _session = null;
        }

        public void Send(ArraySegment<byte> encodedAudio)
        {
            if (!_connected) return;
            var session = _session;
            if (session == null) return;

            var bytes = new byte[encodedAudio.Count];
            Buffer.BlockCopy(encodedAudio.Array, encodedAudio.Offset, bytes, 0, encodedAudio.Count);
            try { session.SendAudio(FrameDuration, bytes); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            Detach();
        }
    }
}
