using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace FFmpegTest.Player;

public unsafe class VideoPlayer : IPlayer, IDisposable
{
    private AVCodec* _avCodec;
    private AVStream* _stream;
    private AVCodecContext* _codecContext;
    private SwsContext* _convertContext;

    //帧，数据指针
    private IntPtr _frameBufferPtr;
    private byte_ptrArray4 _targetData;
    private int_array4 _targetLineSize;

    private readonly ConcurrentQueue<IntPtr> _packetsQueue = new();
    private bool _isNoMorePacket;
    private double _secondsPerPts;
    private Thread _decodeThread;
    private Stopwatch _stopwatch;

    public int FrameWidth;
    public int FrameHeight;
    public event EventHandler<byte[]> OnReadFrame;
    public double Delay { get; set; }
    public PlayState CurrentState { get; private set; } = PlayState.NoPlay;
    public event EventHandler OnCompletePlaying;

    public void SetStopWatch(Stopwatch stopwatch)
    {
        _stopwatch = stopwatch;
    }

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
        AVCodec* videoCodec = _avCodec;
        int videoStreamIndex =
            ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &videoCodec, 0);
        if (videoStreamIndex < 0)
        {
            return false;
        }

        _stream = formatContext->streams[videoStreamIndex];
        _codecContext = ffmpeg.avcodec_alloc_context3(videoCodec);

        int openResult = ffmpeg.avcodec_parameters_to_context(_codecContext, _stream->codecpar);
        if (openResult < 0)
        {
            return false;
        }

        openResult = ffmpeg.avcodec_open2(_codecContext, videoCodec, null);
        if (openResult != 0)
        {
            return false;
        }

        //视频时长等视频信息
        FrameWidth = _stream->codecpar->width;
        FrameHeight = _stream->codecpar->height;

        //初始化转换器，将图片从源格式 转换成 BGR0 （8:8:8）格式
        bool result = InitVideoConvert(FrameWidth, FrameHeight, _codecContext->pix_fmt, FrameWidth, FrameHeight,
            AVPixelFormat.AV_PIX_FMT_BGR0);
        if (!result)
        {
            return false;
        }

        _secondsPerPts = ffmpeg.av_q2d(_stream->time_base);
        return true;
    }

    public void Play()
    {
        if (CurrentState == PlayState.Paused)
        {
            CurrentState = PlayState.Playing;
            return;
        }

        _isNoMorePacket = false;
        CurrentState = PlayState.Playing;
        _decodeThread = new(Decode);
        _decodeThread.Start();
    }

    private void Decode()
    {
        while (!_isNoMorePacket)
        {
            while (_packetsQueue.TryDequeue(out IntPtr pktPtr))
            {
                if (CurrentState == PlayState.NoPlay)
                {
                    goto complete;
                }

                while (CurrentState == PlayState.Paused)
                {
                    Thread.Sleep(100);
                    if (CurrentState == PlayState.NoPlay)
                    {
                        goto complete;
                    }
                }

                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* pkt = (AVPacket*)pktPtr;

                int readResult = ffmpeg.avcodec_send_packet(_codecContext, pkt);
                if (readResult != 0)
                {
                    break;
                }

                ffmpeg.av_frame_unref(frame);
                readResult = ffmpeg.avcodec_receive_frame(_codecContext, frame);
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                while (_stopwatch == null)
                {
                    Thread.Sleep(100);
                }

                double timeDistance = _stopwatch.Elapsed.TotalSeconds - frame->pts * _secondsPerPts + Delay;

                // 放慢了，丢帧加快
                if (timeDistance > 0.5)
                {
                    ffmpeg.av_frame_free(&frame);
                    ffmpeg.av_packet_free(&pkt);
                    continue;
                }
                // 放快了，停一下

                if (timeDistance < 0)
                {
                    Thread.Sleep((int)(-timeDistance * 1500));
                }

                OnReadFrame?.Invoke(this, VideoFrameConvertBytes(frame));
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&pkt);
            }

            Thread.Sleep(10);
        }

        complete:
        OnComplete();
    }

    public void NoMorePackets()
    {
        _isNoMorePacket = true;
    }

    public void Pause()
    {
        CurrentState = PlayState.Paused;
    }

    private void OnComplete()
    {
        Marshal.FreeHGlobal(_frameBufferPtr);
        AVCodecContext* tempCodecContext = _codecContext;
        ffmpeg.avcodec_free_context(&tempCodecContext);
        _codecContext = null;
        _stream = null;
        _avCodec = null;
        _convertContext = null;
        CurrentState = PlayState.NoPlay;
        _packetsQueue.Clear();
        OnCompletePlaying?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        CurrentState = PlayState.NoPlay;
    }

    /// <summary>
    /// 初始化转换器
    /// </summary>
    /// <param name="sourceWidth">源宽度</param>
    /// <param name="sourceHeight">源高度</param>
    /// <param name="sourceFormat">源格式</param>
    /// <param name="targetWidth">目标高度</param>
    /// <param name="targetHeight">目标宽度</param>
    /// <param name="targetFormat">目标格式</param>
    /// <returns></returns>
    private bool InitVideoConvert(int sourceWidth, int sourceHeight, AVPixelFormat sourceFormat, int targetWidth,
        int targetHeight, AVPixelFormat targetFormat)
    {
        //根据输入参数和输出参数初始化转换器
        _convertContext = ffmpeg.sws_getContext(sourceWidth, sourceHeight, sourceFormat, targetWidth, targetHeight,
            targetFormat, ffmpeg.SWS_FAST_BILINEAR, null, null, null);
        if (_convertContext == null)
        {
            return false;
        }

        //获取转换后图像的 缓冲区大小
        int bufferSize = ffmpeg.av_image_get_buffer_size(targetFormat, targetWidth, targetHeight, 1);
        //创建一个指针
        _frameBufferPtr = Marshal.AllocHGlobal(bufferSize);
        _targetData = new();
        _targetLineSize = new();
        ffmpeg.av_image_fill_arrays(ref _targetData, ref _targetLineSize, (byte*)_frameBufferPtr, targetFormat,
            targetWidth, targetHeight, 1);
        return true;
    }

    private byte[] VideoFrameConvertBytes(AVFrame* sourceFrame)
    {
        // 利用转换器将yuv 图像数据转换成指定的格式数据
        ffmpeg.sws_scale(_convertContext, sourceFrame->data, sourceFrame->linesize, 0, sourceFrame->height,
            _targetData,
            _targetLineSize);
        byte_ptrArray8 data = new();
        data.UpdateFrom(_targetData);
        int_array8 lineSize = new();
        lineSize.UpdateFrom(_targetLineSize);
        //创建一个字节数据，将转换后的数据从内存中读取成字节数组
        byte[] bytes = new byte[FrameWidth * FrameHeight * 4];
        Marshal.Copy((IntPtr)data[0], bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        AVCodecContext* tmpContext = _codecContext;
        ffmpeg.avcodec_free_context(&tmpContext);
        _codecContext = null;

        Marshal.FreeHGlobal(_frameBufferPtr);
        _avCodec = null;
        _stream = null;
        _convertContext = null;
    }
}