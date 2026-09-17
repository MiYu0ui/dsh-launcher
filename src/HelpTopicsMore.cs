using System;
using System.Collections.Generic;

namespace DshLauncher
{
    /// <summary>帮助内容的第二部分（SECT. 08 起）。与 HelpTopics.cs 同一个类，纯数据。</summary>
    internal static partial class HelpTopics
    {
        private static List<HelpTopic> BuildMore()
        {
            List<HelpTopic> list = new List<HelpTopic>();

            list.Add(T("SECT. 08", "日常", "托盘、关机与开机自启", "TRAY & AUTOSTART",
                "关窗口默认只收进托盘；开机自启是静默的（没有任何黑窗口一闪而过）。",
                new string[]
                {
                    "关闭窗口 = 最小化到托盘，服务继续跑",
                    "开机自启用启动文件夹快捷方式，静默启动、不弹窗",
                    "旧 PowerShell 方案的自启项会被自动回收"
                },
                HelpBlock.Kv("托盘右键菜单",
                    "打开界面=与主按钮同效",
                    "启动 / 停止服务=同上（按当前状态禁用其一）",
                    "设置 / 帮助 / 卸载=与主界面同一个人口",
                    "退出=真正退出启动器（服务是否停止由你选）"),
                HelpBlock.Steps(
                    "设置 →「开机自动启动（后台静默，不弹出窗口）」勾上。",
                    "启动器会在启动文件夹创建 DSH Launcher 的快捷方式（参数 --autostart）。",
                    "同时它会检查旧 PowerShell 方案留下的自启项，找到就退役掉（避免开机弹黑窗）。"
                ),
                HelpBlock.Note("为什么看不到黑窗？", "启动器用隐藏窗口方式拉起 node，且自带 --selftest 专门验证「全程不出现任何黑色命令行窗口」。"),
                HelpBlock.Kv("相关配置项", "closetotray=1 关闭到托盘；autostart=1 开机自启")
            ));

            list.Add(T("SECT. 09", "日常", "三处快捷方式与修复", "SHORTCUTS",
                "桌面 / 开始菜单 / 启动文件夹三处都由启动器统一管理，设置里有一键修复。",
                new string[]
                {
                    "三处：桌面、开始菜单、启动文件夹（自启）",
                    "设置里「创建 / 修复快捷方式」会一次修齐",
                    "启动时自动清理指向已经不存在目标的孤儿项"
                },
                HelpBlock.P("快捷方式指向的是**启动器**，不是 node —— 所以双击它不会闪黑窗。"),
                HelpBlock.Kv("什么时候需要修复",
                    "换了安装位置=搬迁流程会自动重建，不用手动",
                    "手动挪过 exe=目标失效，用设置里的「创建 / 修复快捷方式」",
                    "想给自启也修一遍=同一个按钮会一起处理"),
                HelpBlock.Note("不会动别的快捷方式", "只处理文件名与目标都指向本启动器的那三处，其他 .lnk 一律不碰。")
            ));

            list.Add(T("SECT. 10", "日常", "检查更新与自动更新", "UPDATE",
                "点「检查更新」比对版本；有新版本会变成「更新 x.y.z」，点了就改名换新并重启自己。",
                new string[]
                {
                    "更新不动正在运行的 DSH 服务",
                    "流程：旧 exe 改名 .old → 复制新版到位 → 以 --after-update 启动新实例",
                    "新实例会等旧实例交出单实例锁（最多 15 秒）"
                },
                HelpBlock.Steps(
                    "点主按钮行的「检查更新」（无更新时就是这个文案）。",
                    "有更新时按钮变成「更新 x.y.z」并转暖褐，标题栏版本号也会显示 `v旧 › v新`。",
                    "点它 → 确认 → 启动器改名换新并以 --after-update 重启。",
                    "旧文件会留成 .old，下次启动时自动清理。"
                ),
                HelpBlock.Warn("本机已知情况", "仓库当前是 Private，匿名 GitHub API 取不到它的 Release，所以「检查更新」会以 404 收场 —— 这是预期行为，不是故障。"),
                HelpBlock.Kv("更新源", "GitHub Release 资产（exe + 同名 .sha256）；仓库坐标可在构建时用 repo.txt 覆盖")
            ));

            list.Add(T("SECT. 11", "日常", "卸载：五种范围与硬护栏", "UNINSTALL",
                "五个范围从「只删程序包」到「全删」；危险范围必须手打确认词，默认送回收站。",
                new string[]
                {
                    "危险范围打字确认：UNINSTALL DSH / UNINSTALL LAUNCHER / UNINSTALL ALL",
                    "勾了「永久删除」再叠一层 DELETE",
                    "硬护栏：工作区、非 DSH 的 npx 目录、自装 Node、%APPDATA%\\npm 永不删"
                },
                HelpBlock.Kv("五种范围",
                    "① 卸载 DSH（保留用户数据）=只删 DSH 程序包（按内容识别出的那些 npx 目录）",
                    "② 彻底卸载 DSH=① + .dsh（含 .credentials.yaml 与全部会话）+ 壁纸引擎数据 + 插件记忆",
                    "③ 卸载启动器（保留配置与日志）=本体 + 三处快捷方式",
                    "④ 彻底卸载启动器=③ + 配置 + 日志 + 早期安装残留",
                    "⑤ 彻底卸载全部=② + ④"),
                HelpBlock.Steps(
                    "主界面点「卸载…」（托盘右键、设置窗右下角也是同一入口）。",
                    "选范围 → 看清「将删除 / 将保留 / 不会动」三组清单。",
                    "按需勾选：先停服务、备份凭据、一并删快捷方式、壁纸数据、插件记忆、清旧残留、永久删除。",
                    "危险范围会要求你**手打确认词**才放行；永久删除再叠一层 DELETE。",
                    "执行中面板会从清单「变形」成加载动画，实时显示已删除体积与项数；可随时关窗口中断（会再确认一次）。",
                    "完成后报告写到桌面并自动定位，失败项会如实列出。"
                ),
                HelpBlock.Warn("中断的代价", "执行中关窗口只是停止后续删除，**已经删掉的部分不会回来**，机器会停在「卸载了一半」的状态。"),
                HelpBlock.Note("两条贴心设计", "① 执行前可以把 .credentials.yaml 与 config.ini 备份到桌面；② 送回收站前会检查回收站容量（不足会提醒），避免大目录删到一半失败。"),
                HelpBlock.Kv("导出清单", "卸载窗口底部有「导出清单」：只写清单、不做任何删除，方便你留档或核对。")
            ));

            list.Add(T("SECT. 12", "进阶", "命令行参数", "COMMAND LINE",
                "启动器是 GUI 程序，但保留了一组参数用于自启、自检与自动化。",
                new string[]
                {
                    "--autostart / --minimized 供开机自启与后台运行",
                    "--selftest 会真的拉起服务并检查有没有黑窗",
                    "--after-update / --after-install 是更新与搬迁的交接参数"
                },
                HelpBlock.Cmd("全部参数：",
                    "DSH Launcher.exe                 打开启动器界面",
                    "  --autostart, -a                后台静默启动服务（开机自启用）",
                    "  --minimized, -m                只放进托盘，不显示窗口",
                    "  --settings                     打开界面并直接进入设置",
                    "  --no-boot                      跳过开启动画",
                    "  --port 3080                    临时指定端口",
                    "  --dir D:\\项目                   临时指定工作目录（--workspace 同义）",
                    "  --after-update                 更新后重启（先等旧实例交出单实例锁）",
                    "  --after-install                迁移安装位置后重启（同上）",
                    "  --selftest 报告.txt            自检并输出报告",
                    "    --mode direct|npx            自检时指定启动方式",
                    "    --verify                     自检时启用下载源完整性核验",
                    "  --help, -h, /?                 显示这份用法"),
                HelpBlock.Note("单实例", "启动器同一时间只允许一个实例（互斥体 Local\\DshLauncher.SingleInstance）。再次双击只会把已有窗口唤到前面。")
            ));

            list.Add(T("SECT. 13", "进阶", "自检与诊断日志", "SELFTEST & LOGS",
                "--selftest 会把「服务能不能起、有没有黑窗」写成一份报告；日志与诊断导出都在设置里。",
                new string[]
                {
                    "自检真的拉起服务、探测端口、再干净停掉",
                    "报告含 6 项：控制台窗口、服务就绪、黑窗、孙进程、服务输出、停止后端口释放",
                    "诊断导出会脱敏 token"
                },
                HelpBlock.Cmd("跑一次自检：",
                    "「DSH Launcher.exe」 --selftest 「%USERPROFILE%\\Desktop\\自检报告.txt」"),
                HelpBlock.Kv("报告里的 6 项",
                    "[1]=启动前可见的控制台窗口数（期望 0）",
                    "[2]=启动服务并等待端口就绪",
                    "[3]=启动过程中有没有新增黑窗（期望 0）",
                    "[4]=模拟 DSH 调用命令行工具时有没有黑窗",
                    "[5]=服务输出尾部（含 PID 与地址）",
                    "[6]=停止服务并确认端口释放"),
                HelpBlock.Kv("日志与导出",
                    "日志= %LOCALAPPDATA%\\DSH Launcher\\logs\\launcher-YYYYMMDD.log",
                    "备份= %LOCALAPPDATA%\\DSH Launcher\\backup\\",
                    "导出诊断=设置里「导出诊断日志」，打包日志与配置（token 已脱敏）")
            ));

            list.Add(T("SECT. 14", "进阶", "排障：常见问题", "TROUBLESHOOTING",
                "黑窗、端口占用、服务起不来、TLS 抽风、更新 404 —— 逐条给出判断与做法。",
                new string[]
                {
                "任何「看起来像坏了」的现象，先看日志尾部",
                    "端口被占：换端口或先停掉占用的进程",
                    "网络问题分两栈：node（OpenSSL）与 .NET（Schannel）不一定同时可用"
                },
                HelpBlock.Sub("服务起不来"),
                HelpBlock.Steps(
                    "看状态面板的结论与 %LOCALAPPDATA%\\DSH Launcher\\logs 里最后 20 行。",
                    "确认端口没被别的程序占用（设置里可以改端口，改完下次启动服务生效）。",
                "用 --selftest 跑一遍：它会用另一个空闲端口独立验证，排除「你当前配置」的干扰。"
                ),
                HelpBlock.Sub("打开界面是空白 / 一直转"),
                HelpBlock.P("服务在跑但页面打不开时，通常是浏览器缓存了旧地址，或令牌变了。重新点一次「打开界面」会带上新的令牌。"),
                HelpBlock.Sub("下载总是失败"),
                HelpBlock.Kv("",
                    "装 Node 失败=这一路走 .NET/Schannel，受本机 TLS 环境影响",
                    "装 DSH 失败=这一路优先交给 node（OpenSSL）",
                    "判断方法=同一个网址用浏览器能开、用启动器不能开，就是这一栈的问题"),
                HelpBlock.Sub("更新检查 404"),
                HelpBlock.P("仓库是 Private 时，匿名 GitHub API 取不到 Release，必然 404。这不是故障。"),
                HelpBlock.Sub("卸载后还剩东西"),
                HelpBlock.P("看桌面上那份卸载报告：成功与失败逐条列出，被占用而删不掉的会写明文件名。结束相关进程后再跑一次同范围卸载即可。"),
                HelpBlock.Sub("托盘图标不见了"),
                HelpBlock.P("Windows 会把不常用的托盘图标收进「隐藏的图标」折叠区。也可以直接再双击一次快捷方式把窗口唤出来。")
            ));

            list.Add(T("SECT. 15", "进阶", "关于本启动器", "ABOUT",
                "版本、设计来源、许可与它到底做了什么。",
                new string[]
                {
                    "当前版本 v" + BuildInfo.Version,
                    "界面语言来自《莱茵生命》PPT 模板与 RhineLabUI",
                    "不修改系统 PATH、不写注册表启动项、不夹带后台服务"
                },
                HelpBlock.Kv("它做了什么",
                    "图形化拉起 DSH=隐藏窗口启动 node，服务在后台",
                    "环境体检与自动部署=缺什么补什么",
                    "三处快捷方式与自启=统一管理、可一键修复",
                    "更新与卸载=都能自己完成"
                ),
                HelpBlock.Kv("它不做什么",
                    "不改系统 PATH=不污染你的开发环境",
                    "不写注册表启动项=自启走启动文件夹快捷方式",
                    "不常驻服务=退出后不留后台进程",
                    "不收集任何数据=没有联网上报，只有你自己点的更新检查"
                ),
                HelpBlock.Kv("设计来源",
                    "版式=《莱茵生命》PPT 模板（罗德岛的克斯制作）——四角括号、标签块、章节导轨、极淡网格",
                    "动效=RhineLabUI（MIT，仅代码）——两条全局缓动曲线与临界阻尼弹簧",
                    "配色=与上述设计逐值对齐（#eae5e1 / #080a08 / #aaa59a / #9b7247）"),
                HelpBlock.Note("字体", "界面字体链：MiSans → 思源黑体 / Microsoft YaHei UI（中文），Bahnschrift 近似 Novecento wide（英文科技字形），Cascadia Mono（等宽）。")
            ));

            return list;
        }
    }
}
