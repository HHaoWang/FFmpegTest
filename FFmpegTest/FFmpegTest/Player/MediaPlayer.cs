using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

#pragma warning disable CS0169
#pragma warning disable CS0649

namespace FFmpegTest.Player;

public unsafe class MediaPlayer : IDisposable
{
    private AudioPlayer _audioPlayer;
    private VideoPlayer _videoPlayer;
    public event EventHandler OnCompletePlaying;
    public event EventHandler OnElapsedTimeChanged;

    public TimeSpan? Duration { get; private set; } = null;
    public TimeSpan ElapsedTime => _audioPlayer.ElapsedTime;

    public double PicDelay
    {
        get => _videoPlayer.Delay;
        set => _videoPlayer.Delay = value;
    }

    public PlayState CurrentState { get; private set; } = PlayState.NoPlay;

    private AVFormatContext* _formatContext;
    private readonly Dictionary<int, IPlayer> _players = new();

    public bool Open(string videoPath)
    {
        _players.Clear();
        _videoPlayer ??= new();
        _audioPlayer ??= new();
        _videoPlayer.OnCompletePlaying += OnComplete;
        _audioPlayer.OnCompletePlaying += OnComplete;
        _audioPlayer.OnElapsedTimeChanged += OnElapsedTimeChanged;

        _formatContext = ffmpeg.avformat_alloc_context();
        AVFormatContext* formatContext = _formatContext;
        int openResult = ffmpeg.avformat_open_input(&formatContext, videoPath, null, null);
        if (openResult != 0)
        {
            return false;
        }

        openResult = ffmpeg.avformat_find_stream_info(formatContext, null);
        if (openResult != 0 || !_videoPlayer.Init(formatContext) || !_audioPlayer.Init(formatContext))
        {
            return false;
        }

        Duration = TimeSpan.FromSeconds(_formatContext->duration / ffmpeg.AV_TIME_BASE);
        _players[_videoPlayer.GetStreamIndex()] = _videoPlayer;
        _players[_audioPlayer.GetStreamIndex()] = _audioPlayer;

        _audioPlayer.OnStartPlaying += (_, e) => { _videoPlayer.SetStopWatch(e); };

        return true;
    }

    public void Play()
    {
        _videoPlayer.Play();
        _audioPlayer.Play();
        if (CurrentState == PlayState.Paused)
        {
            CurrentState = PlayState.Playing;
            return;
        }

        CurrentState = PlayState.Playing;
        Task.Run(() =>
        {
            while (CurrentState != PlayState.NoPlay)
            {
                AVPacket* pkt = ffmpeg.av_packet_alloc();

                int readResult = ffmpeg.av_read_frame(_formatContext, pkt);
                if (readResult != 0)
                {
                    break;
                }

                if (_players.TryGetValue(pkt->stream_index, out IPlayer player))
                {
                    player.Enqueue(pkt);
                }

                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                while (CurrentState == PlayState.Paused)
                {
                    Thread.Sleep(100);
                }

                if (CurrentState == PlayState.NoPlay)
                {
                    break;
                }
            }

            _audioPlayer.NoMorePackets();
            _videoPlayer.NoMorePackets();
        });
    }

    public void Pause()
    {
        CurrentState = PlayState.Paused;
        _audioPlayer.Pause();
        _videoPlayer.Pause();
    }

    public void SubscribeFrameUpdateEvent(EventHandler<byte[]> handler)
    {
        _videoPlayer.OnReadFrame += handler;
    }

    public void UnSubscribeFrameUpdateEvent(EventHandler<byte[]> handler)
    {
        _videoPlayer.OnReadFrame -= handler;
    }

    private void OnComplete(object sender, EventArgs e)
    {
        if (_audioPlayer.CurrentState == PlayState.NoPlay)
        {
            _videoPlayer.Stop();
        }

        if (_audioPlayer.CurrentState != PlayState.NoPlay || _videoPlayer.CurrentState != PlayState.NoPlay) return;
        CurrentState = PlayState.NoPlay;
        Duration = null;
        OnCompletePlaying?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _videoPlayer?.Dispose();
        _audioPlayer?.Dispose();
        AVFormatContext* tmpContext = _formatContext;
        ffmpeg.avformat_close_input(&tmpContext);
    }

    public void Stop()
    {
        _audioPlayer.Stop();
        _videoPlayer.Stop();
        Duration = null;
    }
}

public enum PlayState
{
    NoPlay,
    Playing,
    Paused,
}