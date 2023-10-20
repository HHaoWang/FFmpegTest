using Windows.Graphics.DirectX;
using Microsoft.UI.Xaml;
using FFmpegTest.Helper;
using FFmpegTest.Player;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace FFmpegTest;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        //画布绘制
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
            return;
        }

        if (_mediaPlayer is { CurrentState: PlayState.Paused })
        {
            _mediaPlayer.Play();
            return;
        }

        if (string.IsNullOrWhiteSpace(TextBox.Text))
        {
            return;
        }

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.UnSubscribeFrameUpdateEvent(OnFrameUpdated);
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        _mediaPlayer = new();
        bool result = _mediaPlayer.Open(TextBox.Text);
        if (!result)
        {
            TextBlock.Text = "打开失败";
        }

        _mediaPlayer.SubscribeFrameUpdateEvent(OnFrameUpdated);
        _mediaPlayer.Play();
    }

    private void OnFrameUpdated(object sender, byte[] e)
    {
        VideoPlayer player = (VideoPlayer)sender;
        _bitmap = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), e, player.FrameWidth, player.FrameHeight,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
        Canvas.Invalidate();
    }
}