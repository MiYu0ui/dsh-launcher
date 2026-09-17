using System;
using System.Collections.Generic;

namespace DshLauncher
{
    /// <summary>详情页里的一个内容块。三级界面按块顺序排版。</summary>
    internal class HelpBlock
    {
        public enum Kind
        {
            Para,     // 段落
            Steps,    // 编号步骤
            Kv,       // 键=值（等宽两列）
            Note,     // 提示（灰）
            Warn,     // 警告（红）
            Cmd,      // 命令行（等宽底衬）
            Sub       // 小节标题
        }

        public Kind Type = Kind.Para;
        public string Title = "";
        public List<string> Lines = new List<string>();

        public static HelpBlock P(string text)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Para; b.Lines.Add(text); return b;
        }

        public static HelpBlock Sub(string title)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Sub; b.Title = title; return b;
        }

        public static HelpBlock Steps(params string[] steps)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Steps;
            b.Lines.AddRange(steps); return b;
        }

        public static HelpBlock Kv(string title, params string[] pairs)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Kv; b.Title = title;
            b.Lines.AddRange(pairs); return b;
        }

        public static HelpBlock Note(string title, params string[] lines)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Note; b.Title = title;
            b.Lines.AddRange(lines); return b;
        }

        public static HelpBlock Warn(string title, params string[] lines)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Warn; b.Title = title;
            b.Lines.AddRange(lines); return b;
        }

        public static HelpBlock Cmd(params string[] lines)
        {
            HelpBlock b = new HelpBlock(); b.Type = Kind.Cmd;
            b.Lines.AddRange(lines); return b;
        }
    }

    /// <summary>一个帮助主题：二级界面列它，三级界面展开它。</summary>
    internal class HelpTopic
    {
        public string Index = "";     // "SECT. 01"
        public string Title = "";     // 中文标题
        public string En = "";        // 英文小字
        public string Group = "";     // 左列表的分组
        public string Summary = "";   // 一句话摘要
        public List<string> KeyPoints = new List<string>();     // 概览里的要点
        public List<HelpBlock> Blocks = new List<HelpBlock>();  // 三级界面的正文
    }

    /// <summary>
    /// 帮助内容。**纯数据**：加主题、加小节都不用碰界面代码。
    /// 目标是覆盖启动器现有的全部功能，所以每个主题都写到"照着能做"的程度。
    /// </summary>
    internal static partial class HelpTopics
    {
        private static List<HelpTopic> _all;

        public static List<HelpTopic> All
        {
            get { if (_all == null) _all = Build(); return _all; }
        }

        public static List<string> Groups()
        {
            List<string> gs = new List<string>();
            foreach (HelpTopic t in All)
                if (!gs.Contains(t.Group)) gs.Add(t.Group);
            return gs;
        }

        internal static HelpTopic T(string index, string group, string title, string en, string summary,
                                    string[] keys, params HelpBlock[] blocks)
        {
            HelpTopic t = new HelpTopic();
            t.Index = index; t.Group = group; t.Title = title; t.En = en; t.Summary = summary;
            t.KeyPoints.AddRange(keys);
            t.Blocks.AddRange(blocks);
            return t;
        }

        private static List<HelpTopic> Build()
        {
            List<HelpTopic> list = new List<HelpTopic>();

            list.Add(T("SECT. 01", "入门", "快速上手", "QUICK START",
                "四个按钮就能把 DSH 跑起来：一键部署 → 启动 DSH → 打开界面；不想用了点停止。",
                new string[]
                {
                    "启动 DSH=没部署过就自动部署，部署过就直接拉起服务",
                    "打开界面=在浏览器里打开 Web UI（会先播一段接入过渡）",
                    "停止=结束本地服务，浏览器里那个页面随即失效",
                    "首次运行会先问你装到哪里，不会替你静默决定"
                },
                HelpBlock.P("主界面中间是状态面板（显示服务当前处在哪个阶段），下面是按钮行：启动 DSH / 打开界面 / 停止 / 检查更新 / 卸载…。右上角依次是 ? 帮助 与 ⚙ 设置。"),
                HelpBlock.Steps(
                    "第一次用：点「启动 DSH」。启动器先做环境体检，缺东西就自动进入部署（7 步，见下一条）。",
                    "部署完成后自动启动服务；服务就绪时播一段接入过渡，然后打开浏览器。",
                    "以后每次：点「启动 DSH」，几秒后点「打开界面」。",
                    "不想让它继续跑：点「停止」，或在托盘图标上右键选停止。"
                ),
                HelpBlock.Note("托盘常驻", "关掉窗口默认只是最小化到托盘，服务继续在后台跑。要彻底退出：托盘右键→退出，或在设置里取消「关闭窗口时最小化到托盘」。"),
                HelpBlock.Kv("按钮的可点条件",
                    "启动 DSH=始终可点（主操作，深色实心，四角刻度缓慢呼吸）",
                    "打开界面=服务运行中才可点",
                    "停止=危险色，服务运行中才可点")
            ));

            list.Add(T("SECT. 02", "入门", "一键部署的 7 个步骤", "AUTO DEPLOYMENT",
                "从零到能跑：系统门 → Node → npm → 版本 → 下载 → 快捷方式 → 核验，每步都显示在清单上。",
                new string[]
                {
                    "清单每行右侧有状态标记：· 待执行 / > 进行中 / OK 完成 / !! 失败 / -- 跳过",
                    "网络取数优先交给 node（OpenSSL 栈），失败才退回 .NET",
                    "Node 有三级下载渠道，全失败会打开 nodejs.org 让你手动装"
                },
                HelpBlock.P("体检发现缺运行环境时，启动器会先播一段「进入部署」过渡卡片，然后把面板切成部署清单，逐条执行下面 7 步。"),
                HelpBlock.Kv("七个步骤",
                    "1 检查系统兼容性=Windows 10/11 64 位（用 RtlGetVersion 读真实版本，不依赖进程清单）",
                    "2 准备 Node.js=已就绪则跳过；否则按渠道安装",
                    "3 准备 npm / npx=随 Node 一起确认可用",
                    "4 解析目标版本=按版本策略算出要装哪个版本（见「版本策略」）",
                    "5 下载并安装 DSH=装进 npx 缓存目录，实时显示已下载体积",
                    "6 创建快捷方式=桌面 / 开始菜单指向启动器（可在设置里关）",
                    "7 核验完整性=比对下载源给出的摘要值（可在设置里关闭）"),
                HelpBlock.Note("部署会不会动我的系统？", "只做三件事：往 npx 缓存里装 DSH、必要时装 Node、创建快捷方式。不改系统 PATH，不写注册表启动项（自启用的是启动文件夹快捷方式）。"),
                HelpBlock.Warn("网络相关", "第 5 步可能走 Schannel（.NET）或 node（OpenSSL），两者在本机的可用性不一定相同。某一步反复失败时先看「排障」一节。"),
                HelpBlock.Cmd("想让已装好的机器也走一遍部署向导（用于演示）：",
                    "set DSH_LAUNCHER_FORCE_DEPLOY=1",
                    "\"DSH Launcher.exe\"")
            ));

            list.Add(T("SECT. 03", "入门", "启动方式：npx 与极速模式", "LAUNCH MODE",
                "npx 方式每次经 npm 检查版本、可核验完整性；极速模式直接启动本地已缓存的 DSH，最快且离线可用。",
                new string[]
                {
                    "npx 方式：可核验下载源完整性（推荐）",
                    "极速模式：直接启动本地缓存入口，最快、离线可用",
                    "没手动选过时：找到缓存入口就用极速模式，否则用 npx"
                },
                HelpBlock.P("设置里「启动方式」二选一。两者跑的是同一份 DSH，区别只在启动前做不做 npm 检查。"),
                HelpBlock.Kv("怎么选",
                    "想要最新版 / 在意被投毒=npx 方式",
                    "想快 / 经常离线=极速模式",
                    "不确定=保持默认（启动器按有没有本地缓存自动决定）"),
                HelpBlock.Note("完整性核验", "只有 npx 方式会走核验。勾了「启动前核验下载源完整性」但用极速模式时，核验会被跳过（清单上标 --）。")
            ));

            list.Add(T("SECT. 04", "入门", "版本策略：latest 与 pinned", "VERSION POLICY",
                "默认每次解析官方最新可安装版；也可以钉死一个版本号，做可复现的部署。",
                new string[]
                {
                    "latest（默认）：GitHub immutable Release 优先，回退 npm 全量自选最大版",
                    "pinned：固定用你填的版本号 + 完整性值",
                    "为什么不直接信 npm 的 latest 标签：它可能给出比本机更旧的版本"
                },
                HelpBlock.P("设置里「部署时自动使用官方最新可安装版」勾上就是 latest；取消勾选就能填固定版本（pinned）。"),
                HelpBlock.Kv("解析顺序（latest）",
                    "①=官方 GitHub Release 的 immutable 发布列表",
                    "②=npm registry 的 versions 全量，自己按 SemVer 挑最大（含预发布比较）",
                    "③=都拿不到时才退回 pinnedversion"),
                HelpBlock.Warn("关于 npm 的 dist-tag", "`npm view @deepseek-ai/dsh version` 返回的是 dist-tag latest，实测它可能比本机在跑的版本还旧（0.1.5-rc.1 < 0.1.5-rc.2）。照它走会静默降级，所以启动器不看这个字段。"),
                HelpBlock.Kv("相关配置项（config.ini）",
                    "versionmode=latest|pinned",
                    "pinnedversion=例如 0.1.5-rc.2",
                    "pinnedintegrity=该版本的完整性摘要（可留空）")
            ));

            list.Add(T("SECT. 05", "入门", "安装位置：装到哪、怎么搬", "INSTALL LOCATION",
                "首次运行三选一；之后可以在设置里搬迁，搬迁会复制本体、重建快捷方式并重启自己。",
                new string[]
                {
                    "首次三选一：默认位置 / 自定义… / 绿色免安装",
                    "✕ 或 Esc = 还没决定，下次再问（绝不静默采用默认值）",
                    "搬迁后：旧目录由新实例在下次启动时清理"
                },
                HelpBlock.P("启动器把「程序本体放哪」交给你决定。绿色免安装的意思是：它就在自己所在的目录里跑，不复制到任何地方。"),
                HelpBlock.Steps(
                    "设置 →「安装位置」→ 点「更改…」。",
                    "在三个选项里选一个（默认位置 / 自定义… / 绿色免安装）。",
                    "点「保存」。启动器复制本体、重建三处快捷方式，然后以 --after-install 重启自己。",
                    "正在运行的 DSH 服务不受影响；旧目录在下次启动时被清理。"
                ),
                HelpBlock.Kv("相关配置项",
                    "installdir=安装目录；留空 = 绿色免安装",
                    "installasked=1 表示已经问过你",
                    "cleanupdir=待清理的旧目录（一般不用手改）"),
                HelpBlock.Note("运行期数据放哪？", "日志与备份固定放在 %LOCALAPPDATA%\\DSH Launcher\\{logs,backup}，不放在 exe 目录 —— 绿色包不会夹带日志，挪 exe 也不丢日志。")
            ));

            list.Add(T("SECT. 06", "界面", "界面与动效", "MOTION & LOOK",
                "开启动画、接入过渡、二级界面过渡（三档可调）、按钮动效 —— 都能在设置里关掉或调档。",
                new string[]
                {
                    "二级界面过渡动画三档：完整（600ms）/ 精简（150ms）/ 关闭",
                    "完整档是「版式逐笔就位」：四角括号、标签块、章节导轨、极淡网格",
                    "任何过渡都能点击或按键跳过"
                },
                HelpBlock.Sub("三段主流程动画"),
                HelpBlock.Kv("",
                    "开启动画=螺旋 DNA 授权环（设置里可关，或启动加 --no-boot）",
                    "接入过渡=服务就绪 → 铺满窗口的过渡卡片 → 再打开浏览器",
                    "二级界面过渡=设置 / 卸载 / 确认框 / 打字确认框打开时的入场"),
                HelpBlock.Sub("二级界面过渡的三个档位"),
                HelpBlock.Kv("",
                    "完整=淡入 + 四角括号 + 浅色高亮条 + 标题擦入 + 章节导轨（600ms）",
                    "精简=只保留整窗淡入淡出（150ms）",
                    "关闭=瞬时到位，不做任何过渡"),
                HelpBlock.P("完整档的元素取自《莱茵生命》PPT 模板：四角括号（1px L 形）、黑色标签块 + 白色大写字距小字（SECT. 0N）、浅色高亮条 + 右端小圈、左侧章节导轨 + 圆点 + 短引线、极淡方格（间距 44px、alpha 16）。核心思路来自 PowerPoint 的「平滑」转场——元素位移就位，不遮挡内容，所以入场期间文字一直可读。"),
                HelpBlock.Note("改动什么时候生效？", "档位改动从下一个二级界面开始生效（已经开着的窗口不重播）。"),
                HelpBlock.Sub("按钮动效"),
                HelpBlock.Kv("",
                    "悬停=一道彗尾横扫按钮面（1.9 秒一轮）+ 四角刻度浮现",
                    "主按钮=四角刻度 3.4 秒缓慢呼吸",
                    "按下=边框内收 1px")
            ));

            list.Add(T("SECT. 07", "界面", "状态面板与授权环", "STATUS PANEL",
                "中间那块面板告诉你服务此刻处在哪个阶段；右侧暗屏里是授权环加载动画。",
                new string[]
                {
                    "状态：未启动 / 正在接入 / 运行中 / 异常 / 外部实例",
                    "「外部实例」=端口上已经有一个 DSH 在跑，不是本启动器拉起的",
                    "弧长是平滑插值的，状态切换不会「跳」一下"
                },
                HelpBlock.Kv("状态含义",
                    "未启动=服务没在跑",
                    "正在接入=进程已起，还在等端口就绪",
                    "运行中=探测到 DSH 在响应",
                    "异常=进程退出或探测失败（面板下方给出结论）",
                    "外部实例=端口被别的 DSH 占用，启动器不会去抢"),
                HelpBlock.Note("日志去哪了", "界面上不显示滚动日志（那是旧版黑窗的体验）。完整日志写在 %LOCALAPPDATA%\\DSH Launcher\\logs\\launcher-YYYYMMDD.log。")
            ));

            list.AddRange(BuildMore());
            return list;
        }
    }
}
