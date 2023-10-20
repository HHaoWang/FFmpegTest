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

    private AVFormatContext* _formatContext;

    private readonly Dictionary<int, IPlayer> _players = new();

    public bool IsPlaying { get; private set; } = false;

    public PlayState CurrentState { get; private set; } = PlayState.NoPlay;

    public bool Open(string videoPath)
    {
        _players.Clear();
        _videoPlayer = new VideoPlayer();
        _audioPlayer = new AudioPlayer();

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

    public void Dispose()
    {
        _videoPlayer?.Dispose();
        _audioPlayer?.Dispose();
        AVFormatContext* tmpContext = _formatContext;
        ffmpeg.avformat_close_input(&tmpContext);
    }
}

public enum PlayState
{
    NoPlay,
    Playing,
    Paused,
}