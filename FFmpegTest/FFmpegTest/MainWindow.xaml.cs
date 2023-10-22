using System;
using Windows.Graphics.DirectX;
using Microsoft.UI.Xaml;
using FFmpegTest.Helper;
using FFmpegTest.Player;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegTest;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        //»­²¼»æÖÆ
        Canvas.Draw += (s, e) =>
        {
            if (_bitmap == null) return;
            Transform2DEffect te = Win2DUtil.CalculateImageCenteredTransform(Canvas.ActualSize, _bitmap.Size);
            te.Source = _bitmap;
            e.DrawingSession.DrawImage(te);
        };
    }

    private MediaPlayer _mediaPlayer;
    private CanvasBitmap _bitmap;

    private void OnClickPlay(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is { CurrentState: PlayState.Playing })
        {
            _mediaPlayer.Pause();
            PlayBtn.Content = "²¥·Å";
            return;
        }

        if (_mediaPlayer is { CurrentState: PlayState.Paused })
        {
            _mediaPlayer.Play();
            PlayBtn.Content = "ÔÝÍ£";
            return;
        }

        if (string.IsNullOrWhiteSpace(TextBox.Text))
        {
            return;
        }

        _mediaPlayer ??= new();
        _mediaPlayer.OnCompletePlaying += OnComplete;
        _mediaPlayer.OnElapsedTimeChanged += OnElapsedTimeChanged;
        bool result = _mediaPlayer.Open(TextBox.Text);
        if (!result)
        {
            TextBlock.Text = "´ò¿ªÊ§°Ü";
        }

        DurationText.Text = _mediaPlayer.Duration?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
        _mediaPlayer.SubscribeFrameUpdateEvent(OnFrameUpdated);
        _mediaPlayer.Play();
        PlayBtn.Content = "ÔÝÍ£";
        StopBtn.IsEnabled = true;
    }

    private void OnElapsedTimeChanged(object sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => { ElapsedTimeText.Text = _mediaPlayer.ElapsedTime.ToString(@"hh\:mm\:ss"); });
    }

    private void OnFrameUpdated(object sender, byte[] e)
    {
        VideoPlayer player = (VideoPlayer)sender;
        _bitmap = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), e, player.FrameWidth, player.FrameHeight,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
        Canvas.Invalidate();
    }

    private void OnComplete(object sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Canvas.Invalidate();
            TextBox.Text = "";
            ElapsedTimeText.Text = "";
            PlayBtn.Content = "²¥·Å";
            StopBtn.IsEnabled = false;
        });
    }

    private void OnDelayChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.PicDelay = args.NewValue;
    }

    private void OnClickStop(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.Stop();
        StopBtn.IsEnabled = false;
        TextBox.Text = "";
        PlayBtn.Content = "²¥·Å";
    }
}