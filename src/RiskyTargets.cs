using System;
using System.Collections.Generic;
using System.IO;

namespace AutodeskFix
{
    /// <summary>
    /// 一个「有争议的删除目标」：删除它可能影响非 Autodesk 软件，
    /// 或影响尚未卸载的其他产品。执行前必须由用户逐项决定保留或删除。
    /// </summary>
    internal sealed class RiskyTarget
    {
        public string Path = "";
        public string Title = "";
        public string Reason = "";
        public bool Exists;

        /// <summary>用户选择：true = 保留（默认，安全），false = 删除。</summary>
        public bool Keep = true;
    }

    internal static partial class RepairService
    {
        /// <summary>
        /// 列出深度清理中「有争议」的删除目标。
        /// 判定标准：删除它可能影响其他软件（多厂商共用），或影响其他未卸载的产品。
        /// </summary>
        internal static List<RiskyTarget> GetRiskyTargets()
        {
            var list = new List<RiskyTarget>
            {
                new RiskyTarget
                {
                    Path = @"C:\Program Files\Common Files\Macrovision Shared",
                    Title = "FlexNet Publisher 授权运行时",
                    Reason = "该目录下的 FlexNet 授权运行时被多个厂商共用（Adobe、PTC、Siemens 等）。"
                           + "删除后，其他使用 FlexNet 授权的软件可能无法激活。"
                },
                new RiskyTarget
                {
                    Path = @"C:\Program Files\Common Files\Autodesk Shared",
                    Title = "Autodesk 共享组件",
                    Reason = "被多个 Autodesk 产品共用。若本机仍有未卸载的 Autodesk 产品，删除会影响它们。"
                },
                new RiskyTarget
                {
                    Path = @"C:\Program Files (x86)\Common Files\Autodesk Shared",
                    Title = "Autodesk 共享组件（32 位）",
                    Reason = "同上，供 32 位 Autodesk 产品使用。选择「保留」将同时跳过其中的 AdskLicensing 卸载，保持该目录完整。"
                }
            };

            foreach (RiskyTarget t in list)
            {
                try
                {
                    t.Exists = Directory.Exists(t.Path);
                }
                catch
                {
                    t.Exists = false;
                }
            }

            return list;
        }

        /// <summary>只返回本机实际存在的争议项（用于提示）。</summary>
        internal static List<RiskyTarget> ExistingRiskyTargets()
        {
            var list = new List<RiskyTarget>();
            foreach (RiskyTarget t in GetRiskyTargets())
            {
                if (t.Exists)
                {
                    list.Add(t);
                }
            }
            return list;
        }

        /// <summary>判断某个路径是否被用户选择保留。</summary>
        private static bool IsKeptByUser(string path, List<RiskyTarget> decisions)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // 未提供决策时按安全默认处理：保留所有争议项（而不是删除）。
            if (decisions == null)
            {
                foreach (RiskyTarget t in GetRiskyTargets())
                {
                    if (string.Equals(t.Path.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            foreach (RiskyTarget t in decisions)
            {
                if (t.Keep
                    && string.Equals(t.Path.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
