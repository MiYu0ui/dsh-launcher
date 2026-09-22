using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>内嵌资源（图标与鲸鱼 logo），资源名由构建脚本 /resource:...,名称 指定。</summary>
    internal static class Res
    {
        private static Icon _icon16;
        private static Icon _icon32;
        private static Image _logo;
        private static Image _mark;

        /// <summary>打开一个内嵌资源流。</summary>
        /// <returns>资源流；资源不存在或读取失败时返回 null，调用方必须判空。</returns>
        /// <remarks>
        /// 资源名由构建脚本的 <c>/resource:文件,名称</c> 指定，所以这里拿到的是**名称**而不是路径。
        /// 取流期间的异常被吞掉：缺资源只该是"少一张图"，不该让程序起不来。
        /// </remarks>
        private static Stream Open(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                return asm.GetManifestResourceStream(name);
            }
            catch { return null; }
        }

        /// <summary>取程序图标。</summary>
        /// <param name="size">期望的边长（像素）。小于等于 16 走小图标缓存，大于 16 走大图标缓存。</param>
        /// <returns>图标；资源缺失或构造失败时退回系统默认应用图标。</returns>
        /// <remarks>
        /// 按"小 / 大"两档各缓存一份，避免每次窗口 / 托盘取图标都新建 <see cref="Icon"/>
        /// （每次构造都会占用一份 GDI 句柄，调用点还不少）。
        /// 缓存的是**对象本身**，调用方不要把它 Dispose 掉。
        /// </remarks>
        public static Icon AppIcon(int size)
        {
            try
            {
                if (size <= 16 && _icon16 != null) return _icon16;
                if (size > 16 && _icon32 != null) return _icon32;
                using (Stream s = Open("app.ico"))
                {
                    if (s == null) return SystemIcons.Application;
                    Icon icon = new Icon(s, new Size(size, size));
                    if (size <= 16) _icon16 = icon; else _icon32 = icon;
                    return icon;
                }
            }
            catch { return SystemIcons.Application; }
        }

        /// <summary>取内嵌的鲸鱼 logo 图片。</summary>
        /// <returns>图片；资源缺失或解码失败时返回 null（调用方一律按"可能为空"处理）。</returns>
        /// <remarks>
        /// 这里只在**成功**时缓存。失败不缓存，所以缺图时每次调用都会重试一次 ——
        /// 拿它当每帧都要用的贴图之前请先想清楚这一点。
        /// </remarks>
        public static Image Logo()
        {
            if (_logo != null) return _logo;
            try
            {
                using (Stream s = Open("logo.png"))
                {
                    if (s == null) return null;
                    _logo = Image.FromStream(s);
                }
            }
            catch { _logo = null; }
            return _logo;
        }

        /// <summary>取莱茵生命标志图片（路径取自 RhineLabUI 的 src/brand.ts）。</summary>
        /// <returns>图片；资源缺失或解码失败时返回 null。</returns>
        /// <remarks>
        /// 与 <see cref="Logo"/> 不同：这里的缓存把"失败"也记下来（失败一次后不再重试）。
        /// </remarks>
        public static Image Mark()
        {
            if (_mark != null) return _mark;
            try
            {
                using (Stream s = Open("mark.png"))
                {
                    if (s == null) return null;
                    _mark = Image.FromStream(s);
                }
            }
            catch { _mark = null; }
            return _mark;
        }
    }
}
