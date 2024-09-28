using System.IO;

namespace BDInfo.IO;

public interface IDirectoryInfo
{
    string Name { get; }
    string FullName { get; }
    IDirectoryInfo Parent { get; }
    IFileInfo[] GetFiles();
    IFileInfo[] GetFiles(string searchPattern);
    IFileInfo[] GetFiles(string searchPattern, SearchOption searchOption);
    string GetVolumeLabel();
    IDirectoryInfo[] GetDirectories();
}
