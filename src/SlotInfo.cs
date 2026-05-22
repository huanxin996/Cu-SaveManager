using System.IO;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 给 UI 用的槽位视图模型：物理路径 + 主存档 FileInfo + sidecar 元数据。
    /// Hash 走懒加载，避免每帧扫描列表都做磁盘 MD5。
    /// </summary>
    internal sealed class SlotInfo
    {
        internal FileInfo File { get; }
        internal SlotSidecar Sidecar { get; }
        internal string DateFolder { get; }
        internal string RunIdFolder { get; }

        private string _hash;
        private bool _hashComputed;

        internal SlotInfo(FileInfo file, SlotSidecar sidecar, string dateFolder, string runIdFolder)
        {
            File = file;
            Sidecar = sidecar;
            DateFolder = dateFolder;
            RunIdFolder = runIdFolder;
        }

        internal string DisplayName
            => string.IsNullOrEmpty(Sidecar.Nickname) ? Path.GetFileNameWithoutExtension(File.Name) : Sidecar.Nickname;

        internal string FullSlotPath => File.FullName;

        internal string Hash
        {
            get
            {
                if (!_hashComputed)
                {
                    _hash = SaveStore.ComputeFileHash(FullSlotPath);
                    _hashComputed = true;
                }
                return _hash;
            }
        }
    }
}
