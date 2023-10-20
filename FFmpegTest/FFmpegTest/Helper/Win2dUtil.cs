using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;

namespace FFmpegTest.Helper;

public static class Win2DUtil
{
    public static string ZeroPad(this int value, int length)
    {
        return value.ToString().ZeroPad(length);
    }

    public static string ZeroPad(this string value, int length)
    {
        var temp = value;
        while (temp.Length < length)
        {
            temp = "0" + temp;
        }

        return temp;
    }


    public static Transform2DEffect CalculateImageCenteredTransform(double cWidth, double cHeight, double iWidth,
        double iHeight)
    {
        var mat = CalculateImageCenteredMat(cWidth, cHeight, iWidth, iHeight);
        return new Transform2DEffect() { TransformMatrix = mat };
    }

    public static Transform2DEffect CalculateImageCenteredTransform(Windows.Foundation.Size cSize,
        Windows.Foundation.Size iSize)
    {
        return CalculateImageCenteredTransform(cSize.Width, cSize.Height, iSize.Width, iSize.Height);
    }

    public static Transform2DEffect CalculateImageCenteredTransform(Vector2 cSize, Windows.Foundation.Size iSize)
    {
        return CalculateImageCenteredTransform(cSize.X, cSize.Y, iSize.Width, iSize.Height);
    }

    public static Matrix3x2 CalculateImageCenteredMat(Windows.Foundation.Size cSize, Windows.Foundation.Size iSize)
    {
        return CalculateImageCenteredMat(cSize.Width, cSize.Height, iSize.Width, iSize.Height);
    }

    public static Matrix3x2 CalculateImageCenteredMat(double cWidth, double cHeight, double iWidth, double iHeight)
    {
        float f = (float)Math.Min(cWidth / iWidth, cHeight / iHeight);
        float ox = (float)(cWidth - iWidth * f) / 2;
        float oy = (float)(cHeight - iHeight * f) / 2;
        Matrix3x2 matrix3X2 = Matrix3x2.CreateScale(f) * Matrix3x2.CreateTranslation(ox, oy);
        return matrix3X2;
    }
}