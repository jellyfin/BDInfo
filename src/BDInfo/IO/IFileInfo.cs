using System.IO;

namespace BDInfo.IO
{
    public interface IFileInfo
    {
        string Name { get; }
        string FullName { get; }
        string Extension { get; }
        long Length { get; }
        bool IsDir { get; }
        Stream OpenRead();
        StreamReader OpenText();
    }
}
