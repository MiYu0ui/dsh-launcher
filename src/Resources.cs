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

        private static Stream Open(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                return asm.GetManifestResourceStream(name);
            }
            catch { return null; }
        }

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

        /// <summary>莱茵生命官方标志（路径取自 RhineLabUI 的 src/brand.ts）。</summary>
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
