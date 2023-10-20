using FFmpeg.AutoGen;

namespace FFmpegTest.Player;

public unsafe interface IPlayer
{
    public int GetStreamIndex();
    public void Enqueue(AVPacket* packet);
    public bool Init(AVFormatContext* formatContext);
    public void Play();
    public void NoMorePackets();
    public void Pause();
}