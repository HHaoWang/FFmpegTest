using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using NAudio.Wave;

#pragma warning disable CS0169
#pragma warning disable CS0649

namespace FFmpegTest.Player;

public unsafe class MediaPlayerBack : IDisposable
{
    private IPlayer _audioPlayer;
    private IPlayer _videoPlayer;

    private AVFormatContext* _formatContext;
    private AVCodec* _videoCodec;
    private AVStream* _videoStream;
    private AVCodecContext* _videoCodecContext;
    private AVPacket* _pkt;
    private SwsContext* _videoConvertContext;
    private AVFrame* _frame;

    #region Aduio

    private AVCodec* _audioCodec;
    private AVCodecContext* _audioCodecContext;
    private SwrContext* _audioConvertContext;
    private AVFrame* _audioFrame;
    private AVPacket* _audioPkt;
    private AVStream* _audioStream;

    //输出的采样格式 16bit PCM
    private const AVSampleFormat OutSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
    private const int OutSampleRate = 44100;

    private AVChannelLayout _outChannelLayout;

    //缓冲区指针
    private IntPtr _audioBuffer;

    #endregion

    //帧，数据指针
    private IntPtr _frameBufferPtr;
    private byte_ptrArray4 _targetData;
    private int_array4 _targetLineSize;

    //视频时长
    public TimeSpan Duration { get; protected set; }

    //编解码器名字
    public string CodecName { get; protected set; }
    public string CodecId { get; protected set; }

    //比特率
    public int BitRate { get; protected set; }

    //帧率
    public double FrameRate { get; protected set; }

    //图像的高和款
    public int FrameWidth { get; protected set; }
    public int FrameHeight { get; protected set; }

    //一帧显示时长
    public TimeSpan FrameDuration { get; private set; }

    public bool IsPlaying { get; private set; } = false;

    public event EventHandler<byte[]> OnReadFrame;
    private WaveOut _waveOut;
    private BufferedWaveProvider _bufferedWaveProvider;

    public bool Open(string videoPath)
    {
        _formatContext = ffmpeg.avformat_alloc_context();
        AVFormatContext* formatContext = _formatContext;
        int openResult = ffmpeg.avformat_open_input(&formatContext, videoPath, null, null);
        if (openResult != 0)
        {
            return false;
        }

        openResult = ffmpeg.avformat_find_stream_info(formatContext, null);
        if (openResult != 0)
        {
            return false;
        }

        AVCodec* videoCodec = _videoCodec;
        int videoStreamIndex =
            ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &videoCodec, 0);
        if (videoStreamIndex < 0)
        {
            return false;
        }

        _videoStream = formatContext->streams[videoStreamIndex];

        AVCodec* audioCodec = _audioCodec;
        int audioStreamIndex =
            ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audioCodec, 0);
        if (audioStreamIndex < 0)
        {
            return false;
        }

        _audioStream = formatContext->streams[audioStreamIndex];

        _videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
        openResult =
            ffmpeg.avcodec_parameters_to_context(_videoCodecContext, _videoStream->codecpar);
        if (openResult < 0)
        {
            return false;
        }

        openResult = ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null);
        if (openResult != 0)
        {
            return false;
        }

        _audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
        openResult = ffmpeg.avcodec_parameters_to_context(_audioCodecContext, _audioStream->codecpar);
        if (openResult < 0)
        {
            return false;
        }

        openResult = ffmpeg.avcodec_open2(_audioCodecContext, audioCodec, null);
        if (openResult != 0)
        {
            return false;
        }

        //视频时长等视频信息
        //Duration = TimeSpan.FromMilliseconds(videoStream->duration / ffmpeg.av_q2d(videoStream->time_base));
        Duration = TimeSpan.FromMilliseconds(_formatContext->duration / 1000);
        CodecId = _videoStream->codecpar->codec_id.ToString();
        CodecName = ffmpeg.avcodec_get_name(_videoStream->codecpar->codec_id);
        BitRate = (int)_videoStream->codecpar->bit_rate;
        FrameRate = ffmpeg.av_q2d(_videoStream->r_frame_rate);
        FrameWidth = _videoStream->codecpar->width;
        FrameHeight = _videoStream->codecpar->height;
        FrameDuration = TimeSpan.FromMilliseconds(1000 / FrameRate);

        //初始化转换器，将图片从源格式 转换成 BGR0 （8:8:8）格式
        bool result = InitVideoConvert(FrameWidth, FrameHeight, _videoCodecContext->pix_fmt, FrameWidth, FrameHeight,
            AVPixelFormat.AV_PIX_FMT_BGR0);
        if (!result)
        {
            return false;
        }

        if (!InitAudioConvert())
        {
            return false;
        }

        IsPlaying = false;
        return true;
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
        _videoConvertContext = ffmpeg.sws_getContext(sourceWidth, sourceHeight, sourceFormat, targetWidth, targetHeight,
            targetFormat, ffmpeg.SWS_FAST_BILINEAR, null, null, null);
        if (_videoConvertContext == null)
        {
            Debug.WriteLine("创建转换器失败");
            return false;
        }

        //获取转换后图像的 缓冲区大小
        int bufferSize = ffmpeg.av_image_get_buffer_size(targetFormat, targetWidth, targetHeight, 1);
        //创建一个指针
        _frameBufferPtr = Marshal.AllocHGlobal(bufferSize);
        _targetData = new byte_ptrArray4();
        _targetLineSize = new int_array4();
        ffmpeg.av_image_fill_arrays(ref _targetData, ref _targetLineSize, (byte*)_frameBufferPtr, targetFormat,
            targetWidth, targetHeight, 1);
        return true;
    }

    private byte[] VideoFrameConvertBytes(AVFrame* sourceFrame)
    {
        // 利用转换器将yuv 图像数据转换成指定的格式数据
        ffmpeg.sws_scale(_videoConvertContext, sourceFrame->data, sourceFrame->linesize, 0, sourceFrame->height,
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

    private bool InitAudioConvert()
    {
        _audioConvertContext = ffmpeg.swr_alloc();
        if (_audioCodecContext == null)
        {
            return false;
        }

        SwrContext* audioConvertContext = _audioConvertContext;

        AVSampleFormat inSampleFormat = _audioCodecContext->sample_fmt;
        int inSampleRate = _audioCodecContext->sample_rate;
        AVChannelLayout inChannelLayout = _audioCodecContext->ch_layout;
        _outChannelLayout = _audioCodecContext->ch_layout;
        AVChannelLayout outChannelLayout = _outChannelLayout;
        int result = ffmpeg.swr_alloc_set_opts2(&audioConvertContext, &outChannelLayout, OutSampleFormat, OutSampleRate,
            &inChannelLayout, inSampleFormat, inSampleRate, 0, null);
        if (_audioCodecContext == null || result != 0)
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

    public void Play()
    {
        _frame = ffmpeg.av_frame_alloc();
        _pkt = ffmpeg.av_packet_alloc();
        Task.Run(() =>
        {
            while (true)
            {
                int readResult;
                do
                {
                    if (_pkt != null)
                    {
                        ffmpeg.av_packet_unref(_pkt);
                    }

                    readResult = ffmpeg.av_read_frame(_formatContext, _pkt);
                    if (readResult != 0)
                    {
                        IsPlaying = false;
                        break;
                    }

                    if (_pkt->stream_index != _videoStream->index)
                    {
                        continue;
                    }

                    readResult = ffmpeg.avcodec_send_packet(_videoCodecContext, _pkt);
                    if (readResult != 0)
                    {
                        IsPlaying = false;
                        break;
                    }

                    ffmpeg.av_frame_unref(_frame);
                    readResult = ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame);
                } while (readResult == ffmpeg.EAGAIN);

                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                if (_pkt->stream_index != _videoStream->index)
                {
                    continue;
                }

                OnReadFrame?.Invoke(this, VideoFrameConvertBytes(_frame));
            }
        });
    }

    public void PlayAudio()
    {
        if (_waveOut == null)
        {
            _waveOut = new();
            _bufferedWaveProvider = new(new(OutSampleRate, 2))
            {
                BufferLength = 1024 * 1024 * 10
            };
            _waveOut.Init(_bufferedWaveProvider);
            _waveOut.Play();
        }

        _frame = ffmpeg.av_frame_alloc();
        _pkt = ffmpeg.av_packet_alloc();
        _audioFrame = ffmpeg.av_frame_alloc();
        Task.Run(() =>
        {
            while (true)
            {
                if (_pkt != null)
                {
                    ffmpeg.av_packet_unref(_pkt);
                }

                int readResult = ffmpeg.av_read_frame(_formatContext, _pkt);
                if (readResult != 0)
                {
                    IsPlaying = false;
                    break;
                }

                if (_pkt->stream_index != _audioStream->index)
                {
                    continue;
                }

                readResult = ffmpeg.avcodec_send_packet(_audioCodecContext, _pkt);
                if (readResult != 0)
                {
                    IsPlaying = false;
                    break;
                }


                while (true)
                {
                    ffmpeg.av_frame_unref(_frame);
                    readResult = ffmpeg.avcodec_receive_frame(_audioCodecContext, _frame);
                    if (readResult != 0)
                    {
                        break;
                    }

                    int outChannels = _frame->ch_layout.nb_channels;
                    int outSamples = _frame->nb_samples * OutSampleRate / _frame->sample_rate + 256;
                    ulong outSize =
                        (ulong)ffmpeg.av_samples_get_buffer_size(null, outChannels, outSamples, OutSampleFormat, 0);

                    byte* convertOut = (byte*)ffmpeg.av_malloc(outSize);
                    int indeedOutSamplesNumber = ffmpeg.swr_convert(_audioConvertContext, &convertOut,
                        outSamples, _frame->extended_data, _frame->nb_samples);
                    if (indeedOutSamplesNumber < 0)
                    {
                        IntPtr errorPtr = Marshal.AllocHGlobal(5000);
                        ffmpeg.av_strerror(indeedOutSamplesNumber, (byte*)errorPtr.ToPointer(), 5000);
                        string error = Marshal.PtrToStringAnsi(errorPtr);
                        Marshal.FreeHGlobal(errorPtr);
                        return;
                    }

                    ulong indeedOutSize =
                        (ulong)ffmpeg.av_samples_get_buffer_size(null, outChannels, indeedOutSamplesNumber,
                            OutSampleFormat, 1);

                    byte[] bytes = new byte[indeedOutSize];
                    Marshal.Copy((IntPtr)convertOut, bytes, 0, (int)indeedOutSize);
                    ffmpeg.av_free(convertOut);

                    while (_bufferedWaveProvider.BufferedBytes + (int)indeedOutSize >
                           _bufferedWaveProvider.BufferLength)
                    {
                        Thread.Sleep(1000);
                    }

                    _bufferedWaveProvider.AddSamples(bytes, 0, bytes.Length);
                }
            }
        });
    }

    public void Dispose()
    {
        AVFormatContext* format = _formatContext;
        ffmpeg.avformat_free_context(_formatContext);
        AVCodecContext* codecContext = _videoCodecContext;
        ffmpeg.avcodec_free_context(&codecContext);
    }
}