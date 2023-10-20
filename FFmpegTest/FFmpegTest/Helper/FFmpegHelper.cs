using FFmpeg.AutoGen;
using System.IO;
using System;
using Windows.Storage;

namespace FFmpegTest.Helper;

public static class FFmpegHelper
{
    public static void RegisterFFmpegBinaries()
    {
        //获取当前软件启动的位置
        string currentFolder = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
        //ffmpeg在项目中放置的位置
        string probe = Path.Combine("Assets", "ffmpeg", "bin", Environment.Is64BitOperatingSystem ? "x64" : "x86");
        while (!string.IsNullOrWhiteSpace(currentFolder))
        {
            string ffmpegBinaryPath = Path.Combine(currentFolder, probe);

            if (Directory.Exists(ffmpegBinaryPath))
            {
                //找到dll放置的目录，并赋值给rootPath;
                ffmpeg.RootPath = ffmpegBinaryPath;
                return;
            }

            currentFolder = Directory.GetParent(currentFolder)?.FullName;
        }
    }
}