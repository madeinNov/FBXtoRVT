using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 리본 버튼용 아이콘을 코드로 그려서 만든다.
    /// (별도 이미지 파일 없이, 사각 실선 테두리 + 구분 색/글자로 경계를 명확히)
    /// </summary>
    public static class IconFactory
    {
        /// <summary>
        /// 사각 테두리 + 가운데 글자로 아이콘 이미지를 생성.
        /// </summary>
        /// <param name="size">아이콘 한 변 픽셀 크기 (예: 32, 16)</param>
        /// <param name="glyph">가운데 표시할 짧은 글자</param>
        /// <param name="borderColor">테두리/글자 색</param>
        public static BitmapSource Create(int size, string glyph, Color borderColor)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 바깥 여백을 조금 두고 사각형 영역 계산
                double margin = size * 0.1;
                var rect = new Rect(margin, margin, size - 2 * margin, size - 2 * margin);

                // 사각 실선 테두리 (배경은 흰색으로 채워 경계가 또렷하게)
                double thickness = Math.Max(1.0, size / 16.0);
                var borderBrush = new SolidColorBrush(borderColor);
                var backBrush = new SolidColorBrush(Color.FromArgb(255, 250, 250, 250));
                var pen = new Pen(borderBrush, thickness);

                double corner = size * 0.12; // 살짝 둥근 모서리
                dc.DrawRoundedRectangle(backBrush, pen, rect, corner, corner);

                // 가운데 글자 (한글은 맑은 고딕으로 렌더)
                var typeface = new Typeface(
                    new FontFamily("Malgun Gothic"),
                    FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

                var text = new FormattedText(
                    glyph,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    size * 0.5,
                    borderBrush,
                    1.0);

                // 글자를 정중앙에 배치
                var origin = new Point(
                    (size - text.Width) / 2.0,
                    (size - text.Height) / 2.0);
                dc.DrawText(text, origin);
            }

            // 벡터 그림을 비트맵으로 렌더
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze(); // 다른 스레드(리본)에서 안전하게 쓰도록 고정

            return bitmap;
        }
    }
}
