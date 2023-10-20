using FFmpeg.AutoGen;
using FFmpegTest.Helper;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegTest.Player;

public unsafe class AudioPlayer : IPlayer, IDisposable
{
    private AVCodec* _avCodec;
    private AVStream* _stream;
    private AVCodecContext* _codecContext;
    private SwrContext* _audioConvertContext;

    //输出的采样格式 16bit PCM
    private const AVSampleFormat OutSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
    private const int OutSampleRate = 44100;
    private AVChannelLayout _outChannelLayout;

    //缓冲区指针

    private readonly ConcurrentQueue<IntPtr> _packetsQueue = new();

    private WaveOut _waveOut;
    private BufferedWaveProvider _bufferedWaveProvider;
    // 播放同步时钟
    private DateTime _startTime;
    // 流播放时钟
    private TimeSpan _streamLastTimeSpan;
    public event EventHandler<TimeSpan> OnStartPlaying;
    public PlayState CurrentState { get; private set; } = PlayState.NoPlay;

    private bool _isNoMorePacket;

    public int GetStreamIndex()
    {
        return _stream->index;
    }

    public void Enqueue(AVPacket* packet)
    {
        _packetsQueue.Enqueue((IntPtr)packet);
    }

    public bool Init(AVFormatContext* formatContext)
    {
        AVCodec* audioCodec = _avCodec;
        int audioStreamIndex =
            ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audioCodec, 0);
        if (audioStreamIndex < 0)
        {
            return false;
        }

        _stream = formatContext->streams[audioStreamIndex];
        _codecContext = ffmpeg.avcodec_alloc_context3(audioCodec);

        int openResult = ffmpeg.avcodec_parameters_to_context(_codecContext, _stream->codecpar);
        if (openResult < 0)
        {
            return false;
        }

        openResult = ffmpeg.avcodec_open2(_codecContext, audioCodec, null);
        if (openResult != 0)
        {
            return false;
        }

        return InitConverter();
    }

    public void Play()
    {
        if (CurrentState == PlayState.Paused)
        {
            CurrentState = PlayState.Playing;
            _waveOut.Resume();
            return;
        }

        _isNoMorePacket = false;
        if (_waveOut == null)
        {
            _waveOut = new();
            _bufferedWaveProvider = new(new(OutSampleRate, 2))
            {
                BufferLength = 1024 * 1024 * 10
            };
            _waveOut.Init(_bufferedWaveProvider);
        }

        new Thread(Decode).Start();
    }

    private void Decode()
    {
        while (!_isNoMorePacket)
        {
            while (_packetsQueue.TryDequeue(out IntPtr pktPtr))
            {
                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* pkt = (AVPacket*)pktPtr;

                send:
                int readResult = ffmpeg.avcodec_send_packet(_codecContext, pkt);
                // 读完了
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                // 缓冲区满了，等下再放
                if (readResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    Thread.Sleep(100);
                    goto send;
                }

                // 其它错误
                if (readResult != 0)
                {
                    Debug.WriteLine(FFmpegHelper.GetError(readResult));
                    break;
                }

                while (true)
                {
                    ffmpeg.av_frame_unref(frame);
                    readResult = ffmpeg.avcodec_receive_frame(_codecContext, frame);
                    if (readResult != 0)
                    {
                        break;
                    }

                    int outChannels = frame->ch_layout.nb_channels;
                    int outSamples = frame->nb_samples * OutSampleRate / frame->sample_rate + 256;
                    ulong outSize =
                        (ulong)ffmpeg.av_samples_get_buffer_size(null, outChannels, outSamples, OutSampleFormat, 0);

                    byte* convertOut = (byte*)ffmpeg.av_malloc(outSize);
                    int indeedOutSamplesNumber = ffmpeg.swr_convert(_audioConvertContext, &convertOut,
                        outSamples, frame->extended_data, frame->nb_samples);
                    if (indeedOutSamplesNumber < 0)
                    {
                        IntPtr errorPtr = Marshal.AllocHGlobal(5000);
                        ffmpeg.av_strerror(indeedOutSamplesNumber, (byte*)errorPtr.ToPointer(), 5000);
                        string error = Marshal.PtrToStringAnsi(errorPtr);
                        Debug.WriteLine(error);
                        Marshal.FreeHGlobal(errorPtr);
                        return;
                    }

                    ulong indeedOutSize =
                        (ulong)ffmpeg.av_samples_get_buffer_size(null, outChannels, indeedOutSamplesNumber,
                            OutSampleFormat, 1);

                    byte[] bytes = new byte[indeedOutSize];
                    Marshal.Copy((IntPtr)convertOut, bytes, 0, (int)indeedOutSize);
                    ffmpeg.av_free(convertOut);
                    ffmpeg.av_packet_free(&pkt);

                    while (_bufferedWaveProvider.BufferedBytes + (int)indeedOutSize >
                           _bufferedWaveProvider.BufferLength)
                    {
                        Thread.Sleep(1000);
                    }

                    while (CurrentState == PlayState.Paused)
                    {
                        Thread.Sleep(100);
                    }

                    _bufferedWaveProvider.AddSamples(bytes, 0, bytes.Length);

                    if (_waveOut.PlaybackState != PlaybackState.Playing)
                    {
                        _waveOut.Play();
                        _startTime = DateTime.Now;
                        OnStartPlaying?.Invoke(null, _streamLastTimeSpan);
                    }
                }
            }

            Thread.Sleep(10);
        }
    }

    public void Pause()
    {
        if (CurrentState == PlayState.Playing)
        {
            _waveOut.Pause();
            CurrentState = PlayState.Paused;
            _streamLastTimeSpan += DateTime.Now - _startTime;
        }
    }

    public void NoMorePackets()
    {
        _isNoMorePacket = true;
    }

    private bool InitConverter()
    {
        _audioConvertContext = ffmpeg.swr_alloc();
        if (_codecContext == null)
        {
            return false;
        }

        SwrContext* audioConvertContext = _audioConvertContext;

        AVSampleFormat inSampleFormat = _codecContext->sample_fmt;
        int inSampleRate = _codecContext->sample_rate;
        AVChannelLayout inChannelLayout = _codecContext->ch_layout;
        _outChannelLayout = _codecContext->ch_layout;
        AVChannelLayout outChannelLayout = _outChannelLayout;
        int result = ffmpeg.swr_alloc_set_opts2(&audioConvertContext, &outChannelLayout, OutSampleFormat, OutSampleRate,
            &inChannelLayout, inSampleFormat, inSampleRate, 0, null);
        if (_codecContext == null || result != 0)
        {
            return false;
        }

        if (ffmpeg.swr_init(_audioConvertContext) < 0)
        {
            ffmpeg.swr_free(&audioConvertContext);
            return false;
        }

        return true;
    }

    public int GetUnsolvedItemsNumber() => _packetsQueue.Count;

    public void Dispose()
    {
        AVCodecContext* tmpContext = _codecContext;
        ffmpeg.avcodec_free_context(&tmpContext);
        _codecContext = null;

        _avCodec = null;
        _stream = null;
        _audioConvertContext = null;
    }
}