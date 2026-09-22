using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>设置窗口：与主界面同一套档案终端风格。</summary>
    internal class SettingsForm : Form
    {
        // 设计尺寸：DesignW/DesignH 是 100% 缩放下的设计像素，实际布局一律经 Theme.S 换算。
        // 本窗与卸载窗共用同一套版式元素，改这里的宽度要同步检查两个窗口的对齐。
        private const int DesignW = 664;
        private const int DesignH = 912;   // 810 + 102：新增「二级界面过渡动画」三档一行 + 说明行
        private const int PadX = 34;
        // 标题区高度，同时是可拖动区域的下界（见 OnMouseDown）
        private const int HeaderH = 76;

        // 直接引用调用方传进来的配置对象：Save() 是就地改它，调用方随后自己写盘
        private readonly AppConfig _cfg;
        // 用于判断"端口改了但服务正在跑"；可为 null（此时不做该提示）
        private readonly DshServer _server;

        // 工作目录输入框；Save 时会 Trim 并去掉首尾引号（用户常从资源管理器复制带引号的路径）
        private TextBox _dir;
        private NumericUpDown _port;
        private RadioButton _modeNpx;
        private RadioButton _modeDirect;
        private CheckBox _verify;
        private CheckBox _versionMode;
        private TextBox _version;
        private ComboBox _registry;
        private CheckBox _autoOpen;
        private CheckBox _edge;
        private CheckBox _tray;
        private CheckBox _autostart;
        private CheckBox _boot;
        private CheckBox _transition;
        private RadioButton _motionFull;
        private RadioButton _motionBrief;
        private RadioButton _motionOff;
        private Label _motionHint;
        private Label _shortcutHint;
        // 安装位置只读展示：绿色免安装时显示"（绿色免安装）+ 当前目录"，否则显示配置里的安装目录
        private Label _installDirLabel;

        /// <summary>
        /// 「更改安装位置」的待生效值：
        ///   null = 没改；"" = 绿色免安装；其它 = 新安装目录。
        /// 真正的搬迁在设置窗保存并关闭之后由 LauncherContext 执行 —— 那里才方便重启启动器。
        /// </summary>
        public string PendingInstallDir = null;

        /// <summary>建设置窗：先搭界面再回填当前配置。</summary>
        /// <param name="cfg">要编辑的配置对象（就地修改，不在本窗写盘）。</param>
        /// <param name="server">DSH 服务句柄，仅用于判断端口改动是否需要提示；可为 null。</param>
        public SettingsForm(AppConfig cfg, DshServer server)
        {
            _cfg = cfg;
            _server = server;
            // 必须在 BuildUi 之前建：BuildUi 里的 AddSection 要把章节锚点登记进来
            _reveal = new WindowReveal(this, WindowReveal.Level, false);
            BuildUi();
            LoadValues();
        }

        // 入场动画/版式控制器。声明放在构造函数之后，但**必须在 BuildUi 之前完成赋值** ——
        // BuildUi 里的 AddSection 要把章节锚点登记进它，晚一步章节就会整批丢失。
        private readonly WindowReveal _reveal;

        /// <summary>登记一个章节锚点：标题由 OnPaint 按导轨进度自绘，不再用子控件 Label。</summary>
        private void AddSection(string index, string cn, string en, int y)
        {
            if (_reveal != null) _reveal.AddSection(y, index, cn, en);
        }

        /// <summary>首次显示时开始播入场动画。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reveal != null) _reveal.BeginEnter();
        }

        /// <summary>关窗前先播退场动画（InterceptClose 会先取消本次关闭）。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reveal != null && _reveal.InterceptClose(e)) return;   // 先播 200ms 退场再真关
            base.OnFormClosing(e);
        }

        /// <summary>释放版式控制器。WindowReveal 持有本窗体引用，不释放会留下悬挂的动画注册。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _reveal != null) _reveal.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>搭出整窗控件树：工作目录 → 端口 → 启动方式 → 动效档位 → 安装位置 → 底部按钮。</summary>
        /// <remarks>
        /// 章节标题不在这里建：它们由 AddSection 登记锚点、OnPaint 按导轨进度自绘，
        /// 所以 y 只是"版式进度"，每加一节都要继续往下推。
        /// </remarks>
        private void BuildUi()
        {
            SuspendLayout();
            Text = "设置";
            Icon = Res.AppIcon(32);
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.None;   // 与主界面一致的档案终端卡片式窗口
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            // 小屏兜底：本窗设计高 912（150% 缩放下 1368px）。屏幕装不下时**不裁内容**，
            // 改成可滚动 —— 否则「保存 / 取消」会跑到屏幕外面，设置就改不成了。
            int designH = DesignH;
            try
            {
                int fit = (int)Math.Floor(Screen.PrimaryScreen.WorkingArea.Height / (double)(Theme.Scale <= 0f ? 1f : Theme.Scale)) - 24;
                if (fit > 420 && fit < designH) { designH = fit; AutoScroll = true; }
            }
            catch { }
            ClientSize = new Size(Theme.S(DesignW), Theme.S(designH));
            DoubleBuffered = true;

            int y = 96;

            // 章节标题**不再用子控件 Label**：它们要跟着导轨逐笔显形，所以交给 OnPaint 自绘。
            // 位置与排版不变，只是从"控件"变成"版式"（AddSection 登记锚点，UiPaint 按进度画）。
            AddSection("SECT. 01", "工作目录", "WORKSPACE", y);
            AddHint("DSH 以该目录作为工作区", PadX + 344, y + 5);
            y += 22;
            _dir = new TextBox();
            _dir.Location = new Point(Theme.S(PadX), Theme.S(y));
            _dir.Size = new Size(Theme.S(438), Theme.S(28));
            _dir.BorderStyle = BorderStyle.FixedSingle;
            _dir.BackColor = Theme.PanelHi;
            _dir.ForeColor = Theme.Ink;
            _dir.Font = Theme.FontMono;
            Controls.Add(_dir);

            FlatButton browse = new FlatButton();
            browse.Text = "浏览…";
            browse.Location = new Point(Theme.S(PadX + 450), Theme.S(y - 5));
            browse.Size = new Size(Theme.S(140), Theme.S(38));
            browse.BackColor = Theme.Bg;
            browse.Click += delegate(object s, EventArgs e) { BrowseFolder(); };
            Controls.Add(browse);

            y += 54;
            AddSection("SECT. 02", "端口", "PORT", y);
            y += 22;
            _port = new NumericUpDown();
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Location = new Point(Theme.S(PadX), Theme.S(y));
            _port.Size = new Size(Theme.S(120), Theme.S(28));
            _port.BorderStyle = BorderStyle.FixedSingle;
            _port.BackColor = Theme.PanelHi;
            _port.ForeColor = Theme.Ink;
            _port.Font = Theme.FontMono;
            Controls.Add(_port);
            AddHint("默认 3080；被占用时可改成 3081 等", PadX + 140, y + 5);

            y += 52;
            AddSection("SECT. 03", "启动方式", "LAUNCH MODE", y);
            y += 24;
            // ⚠️ 每组单选必须有自己的容器：WinForms 的 RadioButton 是按**父容器**分组的 ——
            //    全都挂在窗体上就会变成"一大组互斥"，选了动效档位就把启动方式取消掉。
            Panel modeGroup = AddRadioGroup(PadX + 6, y, 596, 52);
            _modeNpx = AddRadio(modeGroup, "npm 方式：每次经 npm 检查版本（可核验下载源完整性，推荐）", 0, 0);
            _modeDirect = AddRadio(modeGroup, "极速模式：直接启动本地已缓存的 DSH（最快、离线可用）", 0, 28);
            y += 52;

            y += 40;
            _verify = AddCheck("启动前核验下载源完整性（防止被投毒，需要联网）", PadX + 6, y);
            y += 28;
            _versionMode = AddCheck("部署时自动使用官方最新可安装版（取消勾选则固定用下面的版本号）", PadX + 6, y);
            y += 36;
            AddLabel("固定版本", PadX + 26, y + 7);
            _version = new TextBox();
            _version.Location = new Point(Theme.S(PadX + 96), Theme.S(y));
            _version.Size = new Size(Theme.S(140), Theme.S(26));
            _version.BorderStyle = BorderStyle.FixedSingle;
            _version.BackColor = Theme.PanelHi;
            _version.ForeColor = Theme.Ink;
            _version.Font = Theme.FontMono;
            Controls.Add(_version);
            AddLabel("首选源", PadX + 254, y + 7);
            _registry = new ComboBox();
            _registry.DropDownStyle = ComboBoxStyle.DropDownList;
            _registry.Location = new Point(Theme.S(PadX + 314), Theme.S(y));
            _registry.Size = new Size(Theme.S(276), Theme.S(26));
            _registry.BackColor = Theme.PanelHi;
            _registry.ForeColor = Theme.Ink;
            _registry.Font = Theme.FontMono;
            _registry.Items.Add("https://registry.npmmirror.com");
            _registry.Items.Add("https://registry.npmjs.org");
            Controls.Add(_registry);

            y += 46;
            _autoOpen = AddCheck("服务就绪后自动打开界面（浏览器）", PadX + 6, y);
            y += 28;
            _edge = AddCheck("用 Edge 应用窗口打开（没有地址栏，像独立软件）", PadX + 6, y);
            y += 28;
            _tray = AddCheck("关闭窗口时最小化到托盘（服务继续在后台运行）", PadX + 6, y);
            y += 28;
            _autostart = AddCheck("开机自动启动（后台静默，不弹出窗口）", PadX + 6, y);
            y += 28;
            _boot = AddCheck("打开界面时播放开启动画（螺旋 DNA 授权环，点击可跳过）", PadX + 6, y);
            y += 28;
            _transition = AddCheck("服务启动成功后播放接入过渡动画，再打开 Web 界面", PadX + 6, y);
            y += 34;

            // 二级界面过渡动画：三档（对应原库的 reduced-motion 硬分支，不是"把动画调快"）
            AddSection("SECT. 04", "二级界面过渡动画", "WINDOW TRANSITION", y);
            y += 24;
            Panel motionGroup = AddRadioGroup(PadX + 6, y, 330, 24);   // 自己的容器 = 自己的一组
            _motionFull = AddMotionRadio(motionGroup, "完整", 0, 110);
            _motionBrief = AddMotionRadio(motionGroup, "精简", 116, 96);
            _motionOff = AddMotionRadio(motionGroup, "关闭", 218, 96);
            y += 26;
            _motionHint = new Label();
            _motionHint.Font = Theme.FontSmall;
            _motionHint.ForeColor = Theme.Sub;
            _motionHint.BackColor = Theme.Bg;
            _motionHint.AutoSize = false;
            _motionHint.TextAlign = ContentAlignment.MiddleLeft;
            _motionHint.Location = new Point(Theme.S(PadX + 6), Theme.S(y));
            _motionHint.Size = new Size(Theme.S(560), Theme.S(18));
            Controls.Add(_motionHint);

            y += 40;
            AddSection("SECT. 05", "安装位置", "INSTALL LOCATION", y);
            AddHint("程序本体放在哪里；空 = 绿色免安装", PadX + 344, y + 5);
            y += 22;
            _installDirLabel = new Label();
            _installDirLabel.Font = Theme.FontMonoSmall;
            _installDirLabel.ForeColor = Theme.Ink;
            _installDirLabel.BackColor = Theme.PanelHi;
            _installDirLabel.AutoSize = false;
            _installDirLabel.TextAlign = ContentAlignment.MiddleLeft;
            _installDirLabel.BorderStyle = BorderStyle.FixedSingle;
            _installDirLabel.Location = new Point(Theme.S(PadX), Theme.S(y));
            _installDirLabel.Size = new Size(Theme.S(438), Theme.S(28));
            Controls.Add(_installDirLabel);

            FlatButton changeDir = new FlatButton();
            changeDir.Text = "更改…";
            changeDir.Location = new Point(Theme.S(PadX + 450), Theme.S(y - 5));
            changeDir.Size = new Size(Theme.S(140), Theme.S(38));
            changeDir.BackColor = Theme.Bg;
            changeDir.Click += delegate(object s, EventArgs e) { ChangeInstallDir(); };
            Controls.Add(changeDir);

            y += 54;
            _shortcutHint = new Label();
            _shortcutHint.Text = "创建 / 修复快捷方式（桌面 + 开始菜单 + 开机自启）→";
            _shortcutHint.Font = Theme.FontSmall;
            _shortcutHint.ForeColor = Theme.Amber;
            _shortcutHint.BackColor = Theme.Bg;
            _shortcutHint.AutoSize = false;
            _shortcutHint.TextAlign = ContentAlignment.MiddleLeft;
            _shortcutHint.Cursor = Cursors.Hand;
            _shortcutHint.Location = new Point(Theme.S(PadX + 6), Theme.S(y));
            _shortcutHint.Size = new Size(Theme.S(430), Theme.S(24));
            _shortcutHint.Click += delegate(object s, EventArgs e) { RepairShortcuts(); };
            Controls.Add(_shortcutHint);

            // 卸载入口（用 Abort 这个 DialogResult 表示"不是保存，而是要去卸载"）
            Label uninstall = new Label();
            uninstall.Text = "卸载 DSH / 启动器…";
            uninstall.Font = Theme.FontSmall;
            uninstall.ForeColor = Theme.SignalAlert;
            uninstall.BackColor = Theme.Bg;
            uninstall.AutoSize = false;
            uninstall.TextAlign = ContentAlignment.MiddleRight;
            uninstall.Cursor = Cursors.Hand;
            uninstall.Location = new Point(Theme.S(PadX + 474), Theme.S(y));
            uninstall.Size = new Size(Theme.S(160), Theme.S(24));
            uninstall.Click += delegate(object s, EventArgs e)
            {
                DialogResult = DialogResult.Abort;
                Close();
            };
            Controls.Add(uninstall);

            int btnY = DesignH - 66;
            FlatButton save = new FlatButton();
            save.Text = "保存";
            save.Primary = true;
            save.Location = new Point(Theme.S(DesignW - PadX - 120), Theme.S(btnY));
            save.Size = new Size(Theme.S(120), Theme.S(44));
            save.BackColor = Theme.Bg;
            save.Click += delegate(object s, EventArgs e) { Save(); };
            Controls.Add(save);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Location = new Point(Theme.S(DesignW - PadX - 236), Theme.S(btnY));
            cancel.Size = new Size(Theme.S(104), Theme.S(44));
            cancel.BackColor = Theme.Bg;
            cancel.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            FlatButton reset = new FlatButton();
            reset.Text = "恢复默认";
            reset.Location = new Point(Theme.S(PadX), Theme.S(btnY));
            reset.Size = new Size(Theme.S(120), Theme.S(44));
            reset.BackColor = Theme.Bg;
            reset.Click += delegate(object s, EventArgs e) { ResetDefaults(); };
            Controls.Add(reset);

            ResumeLayout(false);

            // 无边框窗口的关闭按钮（右上角细线 ✕）
            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - PadX - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);
            close.BringToFront();
        }

        /// <summary>无边框窗口补一个系统投影。</summary>
        /// <summary>无边框窗口补一个系统投影。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        /// <summary>无边框窗口的拖动与"点击跳过入场"。</summary>
        /// <remarks>只有左键落在标题区（HeaderH 以内）才拖动，免得在输入控件上误拖整窗。</remarks>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }   // 入场期间点一下就跳过
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        /// <summary>键盘拦截：入场期间按任意键跳过动画；Esc 等同取消并关窗。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return true; }
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>加一个 AutoSize 的粗体标签（用于「固定版本」「首选源」这类行内小标题）。</summary>
        private void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontUiBold;
            l.ForeColor = Theme.InkSoft;
            l.BackColor = Theme.Bg;
            l.AutoSize = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            Controls.Add(l);
        }

        /// <summary>加一个 AutoSize 的灰色说明标签（跟在章节标题右侧，解释该节的取值规则）。</summary>
        private void AddHint(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontSmall;
            l.ForeColor = Theme.Sub;
            l.BackColor = Theme.Bg;
            l.AutoSize = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            Controls.Add(l);
        }

        /// <summary>
        /// 建一个"单选分组"容器：**本窗里每一组单选都必须有自己的父容器**。
        /// WinForms 的 RadioButton 是按父容器分组的 —— 全都挂在窗体上就会变成一大组互斥
        /// （实测踩到：选了「动效档位」会把「启动方式」取消掉，用户看到的就是"启动方式选不了"）。
        /// 容器只覆盖它那一组控件的范围，用窗体底色，避免盖住别的控件。
        /// </summary>
        private Panel AddRadioGroup(int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.Location = new Point(Theme.S(x), Theme.S(y));
            p.Size = new Size(Theme.S(w), Theme.S(h));
            p.BackColor = Theme.Bg;
            Controls.Add(p);
            return p;
        }

        /// <summary>往指定容器里加一个单选按钮（宽高固定，位置按设计像素给）。</summary>
        /// <param name="parent">父容器。**必须传本组自己的容器**，否则会同名成一大组互斥，见 AddRadioGroup。</param>
        /// <param name="text">按钮文字。</param>
        /// <param name="x">容器内 X（设计像素）。</param>
        /// <param name="y">容器内 Y（设计像素）。</param>
        /// <returns>新建的单选按钮。</returns>
        private RadioButton AddRadio(Control parent, string text, int x, int y)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Font = Theme.FontUi;
            r.ForeColor = Theme.Ink;
            r.BackColor = Theme.Bg;
            r.UseVisualStyleBackColor = false;
            r.FlatStyle = FlatStyle.Standard;
            r.AutoSize = false;
            r.Location = new Point(Theme.S(x), Theme.S(y));
            r.Size = new Size(Theme.S(590), Theme.S(24));
            parent.Controls.Add(r);
            return r;
        }

        /// <summary>动效档位的单选（一行三个，比三行单选省地方）。同样必须挂在自己的容器里。</summary>
        /// <summary>动效档位的单选（一行三个，比三行单选省地方）。同样必须挂在自己的容器里。</summary>
        /// <param name="parent">本组专属容器。</param>
        /// <param name="text">档位名（完整 / 精简 / 关闭）。</param>
        /// <param name="x">容器内 X（设计像素）。</param>
        /// <param name="w">宽度（设计像素）。</param>
        /// <returns>新建的单选按钮；勾选变化会即时刷新下方说明文字。</returns>
        private RadioButton AddMotionRadio(Control parent, string text, int x, int w)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Font = Theme.FontUi;
            r.ForeColor = Theme.Ink;
            r.BackColor = Theme.Bg;
            r.UseVisualStyleBackColor = false;
            r.FlatStyle = FlatStyle.Standard;
            r.AutoSize = false;
            r.Location = new Point(Theme.S(x), 0);
            r.Size = new Size(Theme.S(w), Theme.S(24));
            r.CheckedChanged += delegate(object s, EventArgs e) { if (r.Checked) SyncMotionHint(); };
            parent.Controls.Add(r);
            return r;
        }

        /// <summary>当前选中的档位。</summary>
        private MotionLevel SelectedMotion()
        {
            if (_motionOff != null && _motionOff.Checked) return MotionLevel.Off;
            if (_motionBrief != null && _motionBrief.Checked) return MotionLevel.Brief;
            return MotionLevel.Full;
        }

        /// <summary>把当前档位对应的说明文字填进 _motionHint（切换档位时即时更新）。</summary>
        private void SyncMotionHint()
        {
            if (_motionHint == null) return;
            switch (SelectedMotion())
            {
                case MotionLevel.Off:
                    _motionHint.Text = "关闭：瞬时到位，不做任何过渡（等同原库的「减少动态效果」硬分支）";
                    break;
                case MotionLevel.Brief:
                    _motionHint.Text = "精简：只保留整窗淡入淡出，跳过四角括号与章节导轨（约 0.15 秒）";
                    break;
                default:
                    _motionHint.Text = "完整：淡入 + 四角括号 + 高亮条 + 章节导轨逐笔就位（约 0.6 秒，点击可跳过）";
                    break;
            }
        }

        /// <summary>加一个复选框（统一样式与行宽）。</summary>
        /// <param name="text">选项文字。</param>
        /// <param name="x">窗体 X（设计像素）。</param>
        /// <param name="y">窗体 Y（设计像素）。</param>
        /// <returns>新建的复选框。</returns>
        private CheckBox AddCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = Theme.FontUi;
            c.ForeColor = Theme.Ink;
            c.BackColor = Theme.Bg;
            c.UseVisualStyleBackColor = false;
            c.FlatStyle = FlatStyle.Standard;
            c.AutoSize = false;
            c.Location = new Point(Theme.S(x), Theme.S(y));
            c.Size = new Size(Theme.S(590), Theme.S(24));
            Controls.Add(c);
            return c;
        }

        /// <summary>把配置回填到各控件（窗口刚建好时调一次）。</summary>
        /// <remarks>
        /// 所有字段都做"偏保守"的解读：启动方式非 direct 一律按 npm 方式显示，版本模式非 pinned 一律按最新版显示，
        /// 这样配置文件里出现未知值时不会出现"两个选项都没选中"的空档。
        /// </remarks>
        private void LoadValues()
        {
            _dir.Text = _cfg.Workspace;
            _port.Value = Math.Min(65535, Math.Max(1, _cfg.Port));
            _modeNpx.Checked = _cfg.LaunchMode != "direct";
            _modeDirect.Checked = _cfg.LaunchMode == "direct";
            _verify.Checked = _cfg.VerifyIntegrity;
            _versionMode.Checked = _cfg.VersionMode != "pinned";
            _version.Text = _cfg.PinnedVersion;
            int idx = _registry.Items.IndexOf(_cfg.Registry);
            _registry.SelectedIndex = idx >= 0 ? idx : 0;
            _autoOpen.Checked = _cfg.AutoOpenBrowser;
            _edge.Checked = _cfg.EdgeAppMode;
            _tray.Checked = _cfg.CloseToTray;
            _autostart.Checked = _cfg.AutoStart;
            _boot.Checked = _cfg.BootAnimation;
            _transition.Checked = _cfg.TransitionAnimation;
            MotionLevel lv = UiMotion.ParseLevel(_cfg.Motion);
            _motionFull.Checked = lv == MotionLevel.Full;
            _motionBrief.Checked = lv == MotionLevel.Brief;
            _motionOff.Checked = lv == MotionLevel.Off;
            SyncMotionHint();
            _installDirLabel.Text = _cfg.IsPortable
                ? "（绿色免安装）" + AppPaths.InstallDir
                : _cfg.InstallDir;
        }

        /// <summary>把所有控件恢复到 AppConfig 的默认值（只改界面，保存后才会写盘）。</summary>
        /// <remarks>
        /// 与 LoadValues 的差别不只是取值来源：这里刻意把几个"机器相关"的开关归零 ——
        /// 工作目录、安装位置、开机自启都不属于"默认值"，重置不该顺手改掉用户的部署现状。
        /// </remarks>
        private void ResetDefaults()
        {
            AppConfig def = new AppConfig();
            _port.Value = def.Port;
            _modeNpx.Checked = true;
            _modeDirect.Checked = false;
            _verify.Checked = def.VerifyIntegrity;
            _versionMode.Checked = true;
            _version.Text = def.PinnedVersion;
            _registry.SelectedIndex = 0;
            _autoOpen.Checked = def.AutoOpenBrowser;
            _edge.Checked = false;
            _tray.Checked = def.CloseToTray;
            _autostart.Checked = false;
            _boot.Checked = def.BootAnimation;
            _transition.Checked = def.TransitionAnimation;
            _motionFull.Checked = UiMotion.ParseLevel(def.Motion) == MotionLevel.Full;
            _motionBrief.Checked = UiMotion.ParseLevel(def.Motion) == MotionLevel.Brief;
            _motionOff.Checked = UiMotion.ParseLevel(def.Motion) == MotionLevel.Off;
            SyncMotionHint();
        }

        /// <summary>用系统文件夹对话框挑工作目录；取消则保持原值不动。</summary>
        private void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 DSH 的工作目录";
                dlg.ShowNewFolderButton = true;
                try { if (Directory.Exists(_dir.Text)) dlg.SelectedPath = _dir.Text; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) _dir.Text = dlg.SelectedPath;
            }
        }

        /// <summary>校验输入、把控件值写回 _cfg，然后以 DialogResult.OK 关窗。</summary>
        /// <remarks>
        /// 这里**只改内存中的配置对象**，真正落盘由调用方（LauncherContext）在对话框返回后执行；
        /// 安装位置的搬迁同理，本窗只把待生效值放进 PendingInstallDir。
        /// 因此本方法可以中途 return 而不留半写状态。
        /// </remarks>
        private void Save()
        {
            // 去掉首尾空白与引号：用户从资源管理器「复制路径」拿到的字符串是带引号的
            string dir = _dir.Text.Trim().Trim('"');
            if (!Directory.Exists(dir))
            {
                ConfirmDialog.Info(this, "CONFIG / 目录无效", "工作目录不存在", dir, null,
                    "请选择一个已存在的目录，或先创建它。");
                return;
            }
            // 服务在跑时改端口不会立刻生效（监听已经绑好），只提示、不阻止保存
            bool portChanged = ((int)_port.Value) != _cfg.Port;
            if (portChanged && _server != null && _server.Owned &&
                (_server.Status == ServerStatus.Running || _server.Status == ServerStatus.Starting))
            {
                ConfirmDialog.Info(this, "CONFIG / 提示", "端口改动将在下次启动时生效",
                    "服务当前正在运行，新的端口会在下次启动服务时使用。",
                    new string[] { "新端口=" + ((int)_port.Value) }, null);
            }

            _cfg.Workspace = dir;
            _cfg.Port = (int)_port.Value;
            _cfg.LaunchMode = _modeDirect.Checked ? "direct" : "npx";
            _cfg.VerifyIntegrity = _verify.Checked;
            _cfg.VersionMode = _versionMode.Checked ? "latest" : "pinned";
            _cfg.PinnedVersion = _version.Text.Trim();
            // 没选中任何项时保留原值，不写入空串，免得把可用源配置清坏
            _cfg.Registry = _registry.SelectedItem == null ? _cfg.Registry : _registry.SelectedItem.ToString();
            _cfg.AutoOpenBrowser = _autoOpen.Checked;
            _cfg.EdgeAppMode = _edge.Checked;
            _cfg.CloseToTray = _tray.Checked;
            _cfg.BootAnimation = _boot.Checked;
            _cfg.TransitionAnimation = _transition.Checked;
            _cfg.Motion = UiMotion.LevelKey(SelectedMotion());   // 二级界面过渡动画档位

            // 与上面各字段不同，开机自启有副作用（要动启动文件夹），必须立刻落到磁盘上，
            // 不能等调用方保存配置时再处理。
            // 条件里的 || wantAutostart 是"勾着就重建"：快捷方式可能被清理过，重建一次才算真正生效。
            bool wantAutostart = _autostart.Checked;
            if (wantAutostart != _cfg.AutoStart || wantAutostart) ApplyAutoStart(wantAutostart);
            _cfg.AutoStart = wantAutostart;

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>按勾选状态建或删开机自启项，并把旧方案留下的自启快捷方式退休掉。</summary>
        /// <param name="enable">true = 建（先退休旧的再建新的）；false = 删掉启动文件夹里那一条。</param>
        /// <remarks>
        /// 建失败会弹提示而不是静默失败 —— 用户以为自启开了、实际没开，是最难排查的那类问题。
        /// 退休旧自启项是历史包袱：早期方案用的是另一条快捷方式，两条并存会导致开机启动两次。
        /// 整个过程包在 try 里：设置窗不该因为快捷方式建不出来而打不开或关不掉。
        /// </remarks>
        private void ApplyAutoStart(bool enable)
        {
            try
            {
                if (enable)
                {
                    string legacy = Shortcuts.RetireLegacyAutoStart(AppPaths.ExePath);
                    bool ok = Shortcuts.CreateAutoStartLink(AppPaths.ExePath, AppPaths.InstallDir);
                    if (!ok)
                        ConfirmDialog.Info(this, "CONFIG / 自启失败", "写入开机自启快捷方式失败",
                            "无法在启动文件夹里创建快捷方式。",
                            new string[] { "目标=" + Shortcuts.AutoStartLinkPath },
                            "请检查启动目录的写入权限。");
                    else if (!string.IsNullOrEmpty(legacy))
                        ConfirmDialog.Info(this, "CONFIG / 自启项已处理", "旧自启项已处理",
                            "开机将由本启动器静默启动服务，不再出现黑窗口。",
                            new string[] { "说明=" + legacy }, null);
                }
                else
                {
                    // 以配置为准：关掉就把启动文件夹里那条清掉，不留孤儿
                    // （旧自启项也一并退休，否则下次开机会从旧路径再拉起一个实例）
                    Shortcuts.Delete(Shortcuts.AutoStartLinkPath);
                    Shortcuts.RetireLegacyAutoStart(AppPaths.ExePath);
                }
            }
            catch { }
        }

        /// <summary>
        /// 一次修好全部三处：桌面 + 开始菜单 + 开机自启（自启按当前勾选状态建或删）。
        /// 老版本只修前两处，启动文件夹里那条就一直烂着 —— 这次补上。
        /// </summary>
        private void RepairShortcuts()
        {
            bool wantAuto = _autostart.Checked;
            bool[] app = Shortcuts.CreateAppLinks(AppPaths.ExePath, AppPaths.InstallDir);

            bool autoOk = true;
            string autoNote;
            if (wantAuto)
            {
                autoOk = Shortcuts.CreateAutoStartLink(AppPaths.ExePath, AppPaths.InstallDir);
                autoNote = autoOk ? "OK" : "失败";
            }
            else
            {
                Shortcuts.Delete(Shortcuts.AutoStartLinkPath);
                autoNote = "（按当前设置保持关闭，已清理）";
            }

            string legacy = Shortcuts.RetireLegacyAutoStart(AppPaths.ExePath);

            bool allOk = app[0] && app[1] && autoOk;
            List<string> fields = new List<string>();
            fields.Add("桌面=" + Shortcuts.DesktopLinkPath + (app[0] ? "  OK" : "  失败"));
            fields.Add("开始菜单=" + Shortcuts.StartMenuLinkPath + (app[1] ? "  OK" : "  失败"));
            fields.Add("开机自启=" + autoNote);
            if (!string.IsNullOrEmpty(legacy)) fields.Add("旧自启项=" + legacy);

            ConfirmDialog.Info(this, "SHORTCUT / 快捷方式", "快捷方式已处理",
                allOk ? "桌面、开始菜单与开机自启（如已勾选）均已就绪。" : "部分快捷方式创建失败，详见下方。",
                fields.ToArray(),
                allOk ? null : "请检查目标目录的写入权限。");
        }

        /// <summary>
        /// 「更改…」：重新选安装位置。这里只记录待生效值，真正的搬迁在设置窗保存之后
        /// 由 LauncherContext 执行 —— 那一步可能会重启启动器，不能还压着一个模态窗。
        /// </summary>
        private void ChangeInstallDir()
        {
            ConfirmDialog.Choice c = InstallLocation.Ask(this);
            if (c == ConfirmDialog.Choice.Cancel) return;      // 取消 = 不改

            if (c == ConfirmDialog.Choice.Alt)                 // 绿色免安装
            {
                PendingInstallDir = "";
                _installDirLabel.Text = "（绿色免安装）" + AppPaths.InstallDir;
                ConfirmDialog.Info(this, "INSTALL / 安装位置", "已选择绿色免安装",
                    "保存后生效：启动器不再指向任何安装目录，就在当前所在目录运行。",
                    new string[] { "当前位置=" + AppPaths.InstallDir }, null);
                return;
            }

            string target = c == ConfirmDialog.Choice.Confirm
                ? AppConfig.SuggestedInstallDir()
                : InstallLocation.PickFolder(this, _cfg.InstallDir.Length > 0 ? _cfg.InstallDir : AppPaths.InstallDir);
            if (string.IsNullOrEmpty(target)) return;

            PendingInstallDir = target;
            _installDirLabel.Text = target;
            ConfirmDialog.Info(this, "INSTALL / 安装位置", "保存后生效",
                "点「保存」后启动器会把程序本体安置到该位置、重建快捷方式，然后以新位置重启自己。",
                new string[]
                {
                    "新安装位置=" + target,
                    "当前程序=" + AppPaths.ExePath
                },
                "正在运行的 DSH 服务不受影响。");
        }

        /// <summary>整窗自绘：头部色带 + 刻度尺 + 标志标题 + 四角括号 + 章节导轨。</summary>
        /// <remarks>
        /// 顺序即层次：纸 → 网格 → 头部色带 → 刻度尺 → 标志与标题（压在高亮条上）→ 四角括号 → 章节导轨。
        /// 输入控件由框架画在本方法之上，这里只负责"版式"。
        /// </remarks>
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int band = Theme.S(3);
            int right = w - Theme.S(PadX);

            // ---- 版式就位（《莱茵生命 PPT 模板》那套元素，按 0..600ms 逐笔画出）----
            // 顺序即层次：纸 → 网格 → 头部色带 → 刻度尺 → 标志与标题（压在高亮条上）→ 四角括号 → 章节导轨
            Theme.Fill(g, ClientRectangle, Theme.Bg);
            if (_reveal != null) UiPaint.Grid(g, ClientRectangle, _reveal.GridP, Theme.S(44), 16);

            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            // 刻度尺：逐根 11ms 错峰点亮（27 根 ≈ 300ms，与整段入场同量级）
            int step = Theme.S(22);
            int tickLeft = Theme.S(PadX);
            for (int x = tickLeft, i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                double tp = _reveal != null ? _reveal.Tick(i, 11, 300) : 1.0;
                if (tp <= 0.02) continue;
                int th = tall ? 8 : 4;
                Theme.VRule(g, x, h - 1 - Theme.S((int)Math.Round(th * tp)), h - 1,
                            Theme.Mix(Theme.Panel, tall ? Theme.Line : Theme.LineSoft, tp));
            }

            Image logo = Res.Logo();
            int box = Theme.S(32);
            double markP = _reveal != null ? _reveal.TitleP : 1.0;
            double barP = _reveal != null ? _reveal.BarP : 1.0;
            if (logo != null && markP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(20), box, box));
            }

            // 标题：背后一条浅色高亮条先刷到位，文字再自左向右擦入
            //（模板里 RHINE LAB.LLC. 那条就是"高亮条 + 右端小圈"）
            int titleX = Theme.S(PadX + 44);
            int titleY = band + Theme.S(23);
            int barW = Theme.S(102);
            UiPaint.HighlightBar(g, new Rectangle(titleX - Theme.S(6), band + Theme.S(17), barW, Theme.S(22)), barP, true);
            double titleP = _reveal != null ? _reveal.TitleP : 1.0;
            if (titleP > 0.01)
                Theme.DrawTracked(g, "SETTINGS", Theme.FontTechBold, titleX, titleY, Theme.Ink, Theme.SF(2.2f),
                                  titleX + (int)Math.Round(barW * titleP));   // 手动截断擦入（GDI 文字不认 SetClip）
            if (markP > 0.02)
                Theme.DrawTracked(g, "配置", Theme.FontSmall, Theme.S(PadX + 46), band + Theme.S(42),
                                  Theme.Mix(Theme.Panel, Theme.Sub, markP), Theme.SF(1.2f));

            Image mark = Res.Mark();
            int markH = Theme.S(18);
            int markW = (int)Math.Round(markH * 2.2);
            if (mark != null && markP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(right - markW, Theme.S(34), markW, markH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                right - markW - Theme.S(10), Theme.S(37), Theme.Mix(Theme.Panel, Theme.Sub, markP), Theme.SF(1.6f));

            g.TranslateTransform(0, AutoScrollPosition.Y);   // 自绘内容跟随滚动（子控件会移，自绘不会 —— 框架不做这个平移）
            DrawSections(g);
            g.TranslateTransform(0, -AutoScrollPosition.Y);

            // 四角括号压在最上层：它框住的是整张卡片
            if (_reveal != null && _reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)),
                                 Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }

        /// <summary>
        /// 章节导轨 + 每节的「chip + 中文标题 + 灰色英文小字」。
        /// 这就是模板里那套 "leader line + dot + chip + headline + sub"：圆点明确指向它标的是哪一节。
        /// </summary>
        private void DrawSections(Graphics g)
        {
            if (_reveal == null || !_reveal.WantsLayout || _reveal.Sections.Count == 0) return;

            // 导轨：从第一节划到最后一节，圆点落在每节的文字基线上
            int railX = Theme.S(14);
            int railTop = Theme.S(_reveal.Sections[0].Y) + Theme.S(7);
            int railBottom = Theme.S(_reveal.Sections[_reveal.Sections.Count - 1].Y) + Theme.S(7);
            UiPaint.SectionRail(g, railX, railTop, railBottom, _reveal.RailP,
                                _reveal.SectionYs(), _reveal.SectionPhases());

            for (int i = 0; i < _reveal.Sections.Count; i++)
            {
                WindowReveal.Section s = _reveal.Sections[i];
                double p = _reveal.SectionP(i);
                if (p <= 0.01) continue;

                int x = Theme.S(PadX);
                int y = Theme.S(s.Y);
                int chipW = UiPaint.Chip(g, x, y, s.Index, p);

                int tx = x + chipW + Theme.S(10);
                Theme.DrawTracked(g, s.Cn, Theme.FontUiBold, tx, y + Theme.S(3),
                                  Theme.Mix(Theme.Bg, Theme.Ink, p), Theme.SF(0.6f));
                Size cn = Theme.MeasureTracked(g, s.Cn, Theme.FontUiBold, Theme.SF(0.6f));
                if (s.En.Length > 0)
                    Theme.DrawTracked(g, s.En, Theme.FontMonoSmall, tx + cn.Width + Theme.S(12), y + Theme.S(4),
                                      Theme.Mix(Theme.Bg, Theme.Sub, p), Theme.SF(1.6f));
            }
        }
    }
}
