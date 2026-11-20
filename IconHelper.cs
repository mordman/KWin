using System.Drawing.Drawing2D;

namespace KeyboardLayoutSwitcher;

// Рисует значок в трее с кодом текущей раскладки ("RU", "EN" или "||" на паузе).
internal static class IconHelper
{
    public static Icon CreateTextIcon(string text, bool paused)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            if (paused)
            {
                // Серый фон с двумя полосами "||"
                using var bg = new SolidBrush(Color.FromArgb(90, 90, 90));
                g.FillRectangle(bg, 0, 0, 32, 32);
                using var pen = new Pen(Color.White, 4);
                g.DrawLine(pen, 11, 8, 11, 24);
                g.DrawLine(pen, 21, 8, 21, 24);
            }
            else
            {
                // Цвет зависит от языка: RU — оранжево-красный, EN — синий, прочие — серый
                Color back = text.ToUpperInvariant() switch
                {
                    "RU" => Color.FromArgb(200, 60, 30),
                    "EN" => Color.FromArgb(30, 90, 200),
                    _ => Color.FromArgb(70, 70, 70),
                };
                using var bg = new SolidBrush(back);
                g.FillRectangle(bg, 0, 0, 32, 32);

                // Сам текст: "RU"/"EN" полностью, для прочих — первые 2 символа
                string label = text.Length > 2 ? text[..2] : text;
                using var font = new Font("Segoe UI", label.Length switch { 1 => 16f, 2 => 12f, _ => 12f },
                                          FontStyle.Bold, GraphicsUnit.Pixel);
                var size = g.MeasureString(label, font);
                using var fg = new SolidBrush(Color.White);
                g.DrawString(label, font, fg, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
