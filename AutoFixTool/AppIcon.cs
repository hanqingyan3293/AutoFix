using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>
    /// 应用图标。
    ///
    /// 说明：项目文件中的 ApplicationIcon 只写入 exe 的 Win32 资源（影响资源管理器里显示的图标），
    /// WinForms 窗体默认使用 .NET 自带的通用图标，必须显式设置 Form.Icon，
    /// 标题栏与任务栏才会显示本程序的图标。
    /// </summary>
    internal static class AppIcon
    {
        private static Icon _cached;
        private static bool _loaded;

        /// <summary>应用图标（多尺寸 ICO，含 16/24/32/48/64/128/256）。加载失败返回 null。</summary>
        internal static Icon Value
        {
            get
            {
                if (_loaded)
                {
                    return _cached;
                }
                _loaded = true;
                _cached = Load();
                return _cached;
            }
        }

        private static Icon Load()
        {
            // 1) 优先从内嵌资源加载：保留多尺寸，系统可按 DPI 选择合适的一帧
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using (System.IO.Stream s = asm.GetManifestResourceStream(name))
                    {
                        if (s != null)
                        {
                            return new Icon(s);
                        }
                    }
                }
            }
            catch { }

            // 2) 回退：从 exe 自身提取（只有 32x32，但聊胜于无）
            try
            {
                return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            return null;
        }

        /// <summary>给窗体设置应用图标；加载失败则保持默认，不抛异常。</summary>
        internal static void Apply(Form form)
        {
            if (form == null)
            {
                return;
            }
            try
            {
                Icon icon = Value;
                if (icon != null)
                {
                    form.Icon = icon;
                }
            }
            catch { }
        }
    }
}
