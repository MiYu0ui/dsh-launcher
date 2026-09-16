using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 部署面板：未部署时顶掉状态面板，用同样式（细线 + 四角刻度 + 宽字距标注）
    /// 展示体检结论与 6 步自动部署清单。
    /// </summary>
    internal class DeployPanel : Panel
    {
        private string _track = "DEPLOY / 自动部署";
        private string _headline = "检测中";
        private string _summary = "";
        private List<DeployStep> _steps = new List<DeployStep>();
        private double _progress;
        private bool _running;
        private Color _signal = Theme.SignalBusy;

        public DeployPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Anim.Track(this, 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        public void SetState(string track, string headline, string summary, Color signal,
                             List<DeployStep> steps, bool running, double progress)
        {
            _track = track;
            _headline = headline == null ? "" : headline;
            _summary = summary == null ? "" : summary;
            _signal = signal;
            _steps = steps == null ? new List<DeployStep>() : steps;
            _running = running;
            _progress = progress;
            Anim.Track(this, running ? 1 : 3);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Fill(g, ClientRectangle, BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.PanelHi);
            Theme.DrawLiveFrame(g, r, Theme.Line, _signal, true, 5.6, 0.35);
            int barAlpha = (int)Theme.Pulse(2.6, 0.35, 140, 255);
            Theme.Fill(g, new Rectangle(r.X + 1, r.Y + 1, Theme.S(3), r.Height - 2),
                       Color.FromArgb(barAlpha, _signal));

            int x = Theme.S(22);
            int right = Width - Theme.S(22);

            Theme.DrawTracked(g, _track, Theme.FontMonoSmall, x, Theme.S(12), Theme.Sub, Theme.SF(1.6f));
            // 进度百分比（部署中）
            if (_running)
                Theme.DrawTrackedRight(g, ((int)Math.Round(_progress * 100)) + "%", Theme.FontMonoSmall,
                    right, Theme.S(12), Theme.Amber, Theme.SF(1.4f));

            TextRenderer.DrawText(g, _headline, Theme.FontStatus,
                new Rectangle(x, Theme.S(28), right - x, Theme.S(26)), _signal,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            Theme.SweepRule(g, x, right, Theme.S(62), Theme.Line, _signal, 8.5, 0.1);

            int y = Theme.S(68);
            // 步骤数会随版本变化（当前 7 步）；按可用高度自适应行高，保证全部显示得下
            int shown = Math.Min(_steps.Count, 8);
            int areaBottom = Height - Theme.S(26);
            int rowH = shown > 0 ? Math.Max(Theme.S(11), (areaBottom - Theme.S(68)) / shown) : Theme.S(16);
            for (int i = 0; i < shown; i++)
            {
                DeployStep s = _steps[i];
                Color titleColor = Theme.Ink;
                Color stateColor = Theme.SignalIdle;
                string stateText = "·";
                switch (s.State)
                {
                    case StepState.Running: titleColor = Theme.Amber; stateColor = Theme.Amber; stateText = ">"; break;
                    case StepState.Done: stateColor = Theme.Green; stateText = "OK"; break;
                    case StepState.Failed: titleColor = Theme.SignalAlert; stateColor = Theme.SignalAlert; stateText = "!!"; break;
                    case StepState.Skipped: stateColor = Theme.SignalIdle; stateText = "--"; break;
                }

                Theme.DrawTracked(g, (i + 1).ToString(), Theme.FontMonoSmall, x, y + Theme.S(2), Theme.Sub, 0f);
                int titleX = x + Theme.S(18);
                int stateW = Theme.S(30);
                int textW = right - titleX - stateW;

                Rectangle titleRect = new Rectangle(titleX, y, textW, rowH);
                TextRenderer.DrawText(g, s.Title, Theme.FontSmall, titleRect, titleColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                string detail = s.Detail;
                if (detail.Length > 0)
                {
                    Size titleSize = TextRenderer.MeasureText(g, s.Title, Theme.FontSmall, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
                    int detailX = titleX + titleSize.Width + Theme.S(8);
                    int detailW = right - stateW - detailX;
                    if (detailW > Theme.S(40))
                    {
                        TextRenderer.DrawText(g, detail, Theme.FontMonoSmall,
                            new Rectangle(detailX, y, detailW, rowH), Theme.Sub,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
                    }
                }

                Theme.DrawTrackedRight(g, stateText, Theme.FontMonoSmall, right, y + Theme.S(2), stateColor, 0f);
                y += rowH;
            }

            // 底部：结论摘要
            if (_summary.Length > 0)
            {
                TextRenderer.DrawText(g, _summary, Theme.FontSmall,
                    new Rectangle(x, Height - Theme.S(20), right - x, Theme.S(16)), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
