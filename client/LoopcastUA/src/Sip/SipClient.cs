using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LoopcastUA.Config;
using LoopcastUA.Infrastructure;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace LoopcastUA.Sip
{
    internal sealed class SipClient : IDisposable
    {
        private const int OpusPayloadType = 111;
        private const int RingTimeoutMs = 30000;

        private AppConfig _config;
        private SIPTransport _transport;
        private SIPUserAgent _ua;
        private RTPSession _rtpSession;

        private volatile bool _disposed;
        private volatile bool _connecting;
        private int _currentDelayMs;
        private Timer _reconnectTimer;

        public RtpSender RtpSender { get; } = new RtpSender();

        public event EventHandler CallConnected;
        public event EventHandler CallDisconnected;

        public void Start(AppConfig config)
        {
            _config = config;
            _currentDelayMs = config.Reconnect.InitialDelayMs;
            _transport = new SIPTransport();
            ScheduleConnect(0);
        }

        public void Stop()
        {
            _disposed = true;
            _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            try { _ua?.Hangup(); } catch { }
            CleanupSession();
            _transport?.Shutdown();
            _transport?.Dispose();
        }

        private void ScheduleConnect(int delayMs)
        {
            _reconnectTimer?.Dispose();
#pragma warning disable CS4014
            _reconnectTimer = new Timer(__ => { Task.Run(ConnectAsync); }, null, delayMs, Timeout.Infinite);
#pragma warning restore CS4014
        }

        private async Task ConnectAsync()
        {
            if (_disposed || _connecting) return;
            _connecting = true;

            try
            {
                CleanupSession();

                _rtpSession = BuildRtpSession();
                RtpSender.Attach(_rtpSession);

                _ua = new SIPUserAgent(_transport, null, false, null);
                _ua.OnCallHungup += OnCallHungup;

                string dest = $"sip:{_config.Sip.ConferenceRoom}@{_config.Sip.Server}:{_config.Sip.Port}";
                Logger.Info($"Connecting to {dest} as {_config.Sip.Username}");

                bool answered = await _ua.Call(dest, _config.Sip.Username, _config.Sip.Password, _rtpSession, RingTimeoutMs);

                if (answered)
                {
                    await _rtpSession.Start();
                    RtpSender.SetConnected();
                    _currentDelayMs = _config.Reconnect.InitialDelayMs;
                    Logger.Info("SIP call established");
                    CallConnected?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Logger.Warn("SIP call not answered, will retry");
                    ScheduleReconnect();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SIP connect failed: {ex.Message}");
                ScheduleReconnect();
            }
            finally
            {
                _connecting = false;
            }
        }

        private RTPSession BuildRtpSession()
        {
            var session = new RTPSession(false, false, false, null, _config.Sip.LocalRtpPort, null);

            var opusFormat = new SDPAudioVideoMediaFormat(
                SDPMediaTypesEnum.audio,
                OpusPayloadType,
                "opus",
                48000,
                2,
                "minptime=20;useinbandfec=0");

            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { opusFormat },
                MediaStreamStatusEnum.SendOnly);

            session.addTrack(track);
            return session;
        }

        private void OnCallHungup(SIPDialogue dialogue)
        {
            if (_disposed) return;
            Logger.Warn("Call hung up by remote");
            CallDisconnected?.Invoke(this, EventArgs.Empty);
            CleanupSession();
            ScheduleReconnect();
        }

        private void ScheduleReconnect()
        {
            if (_disposed) return;
            Logger.Info($"Reconnecting in {_currentDelayMs}ms");
            ScheduleConnect(_currentDelayMs);
            _currentDelayMs = (int)Math.Min(
                _currentDelayMs * _config.Reconnect.BackoffMultiplier,
                _config.Reconnect.MaxDelayMs);
        }

        private void CleanupSession()
        {
            RtpSender.Detach();
            try { _rtpSession?.Close("cleanup"); } catch { }
            _rtpSession = null;
        }

        public void Dispose()
        {
            Stop();
            _reconnectTimer?.Dispose();
            RtpSender.Dispose();
        }
    }
}
