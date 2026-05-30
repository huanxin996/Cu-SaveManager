using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人存档（KrokMP）位置定位与打包。KrokMP 把存档放在 persistentDataPath/mp_save/ 下：
    /// 每个玩家一个子目录 &lt;persistentId&gt;/save.sv，外加头文件 mp_rules.json。
    /// 单机存档则是 persistentDataPath/save.sv 单文件。
    /// 槽位仍是单个 .sv 文件——多人时其内容为 mp_save 目录的 zip 包。
    /// </summary>
    internal static class MpSaveLocator
    {
        internal const string MpFolderName = "mp_save";
        internal const string MpRulesFile = "mp_rules.json";

        internal static string MpSaveDir => Path.Combine(Application.persistentDataPath, MpFolderName);

        internal static string MpRulesPath => Path.Combine(MpSaveDir, MpRulesFile);

        internal static string VanillaSavePath => Path.Combine(Application.persistentDataPath, "save.sv");

        /// <summary>多人存档目录是否有效：含 mp_rules.json 即视为一份多人存档。</summary>
        internal static bool HasMpSave() => File.Exists(MpRulesPath);

        /// <summary>当前是否应按多人存档处理：KrokMP 多人模式已启用即按多人（与是否开房联机无关）。
        /// 不依赖 mp_save 文件存在——KrokMP 加载后会删除 mp_save，游戏内它不存在是常态。</summary>
        internal static bool IsMultiplayerSaveActive()
            => MultiplayerBridge.IsMultiplayerEnabled();

        /// <summary>把整个 mp_save 目录打包成 zip 写到 destZipPath。目录不存在则抛异常。</summary>
        internal static void PackMpSaveTo(string destZipPath)
        {
            if (!HasMpSave()) throw new FileNotFoundException("没有可打包的多人存档 mp_save");
            if (File.Exists(destZipPath)) File.Delete(destZipPath);
            ZipFile.CreateFromDirectory(MpSaveDir, destZipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        /// <summary>用 zip 包覆盖还原 mp_save 目录（先清空再解包）。</summary>
        internal static void RestoreMpSaveFrom(string srcZipPath)
        {
            if (!File.Exists(srcZipPath)) throw new FileNotFoundException("槽位文件不存在", srcZipPath);
            if (Directory.Exists(MpSaveDir)) Directory.Delete(MpSaveDir, recursive: true);
            Directory.CreateDirectory(MpSaveDir);
            ZipFile.ExtractToDirectory(srcZipPath, MpSaveDir);
        }
    }
}
