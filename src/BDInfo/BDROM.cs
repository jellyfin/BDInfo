//============================================================================
// BDInfo - Blu-ray Video and Audio Analysis Tool
// Copyright © 2010 Cinema Squid
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//=============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

using BDInfo.IO;

namespace BDInfo;
public class BDROM
{
    public IDirectoryInfo DirectoryRoot;
    public IDirectoryInfo DirectoryBDMV;

    public IDirectoryInfo DirectoryBDJO;
    public IDirectoryInfo DirectoryCLIPINF;
    public IDirectoryInfo DirectoryPLAYLIST;
    public IDirectoryInfo DirectorySNP;
    public IDirectoryInfo DirectorySSIF;
    public IDirectoryInfo DirectorySTREAM;
    public IDirectoryInfo DirectoryMeta;

    public string VolumeLabel;
    public string DiscTitle;
    public ulong Size;
    public bool IsBDPlus;
    public bool IsBDJava;
    public bool IsDBOX;
    public bool IsPSP;
    public bool Is3D;
    public bool Is50Hz;
    public bool IsUHD;

    public Dictionary<string, TSPlaylistFile> PlaylistFiles = new();
    public Dictionary<string, TSStreamClipFile> StreamClipFiles = new();
    public Dictionary<string, TSStreamFile> StreamFiles = new();
    public Dictionary<string, TSInterleavedFile> InterleavedFiles = new();

    private static List<string> _excludeDirs = new() { "ANY!", "AACS", "BDSVM", "ANYVM", "SLYVM" };

    public delegate bool OnStreamClipFileScanError(TSStreamClipFile streamClipFile, Exception ex);

    public event OnStreamClipFileScanError StreamClipFileScanError;

    public delegate bool OnStreamFileScanError(TSStreamFile streamClipFile, Exception ex);

    public event OnStreamFileScanError StreamFileScanError;

    public delegate bool OnPlaylistFileScanError(TSPlaylistFile playlistFile, Exception ex);

    public event OnPlaylistFileScanError PlaylistFileScanError;

    public BDROM(IDirectoryInfo path)
    {
        //
        // Locate BDMV directories.
        //
        DirectoryBDMV = GetDirectoryBDMV(path);

        if (DirectoryBDMV is null)
        {
            throw new Exception("Unable to locate BD structure.");
        }

        DirectoryRoot = DirectoryBDMV.Parent;

        DirectoryBDJO = GetDirectory("BDJO", DirectoryBDMV, 0);
        DirectoryCLIPINF = GetDirectory("CLIPINF", DirectoryBDMV, 0);
        DirectoryPLAYLIST = GetDirectory("PLAYLIST", DirectoryBDMV, 0);
        DirectorySNP = GetDirectory("SNP", DirectoryRoot, 0);
        DirectorySTREAM = GetDirectory("STREAM", DirectoryBDMV, 0);
        DirectorySSIF = GetDirectory("SSIF", DirectorySTREAM, 0);
        DirectoryMeta = GetDirectory("META", DirectoryBDMV, 0);

        if (DirectoryCLIPINF is null || DirectoryPLAYLIST is null)
        {
            throw new Exception("Unable to locate BD structure.");
        }

        VolumeLabel = DirectoryRoot.GetVolumeLabel();
        Size = (ulong)GetDirectorySize(DirectoryRoot);

        var indexFiles = DirectoryBDMV?.GetFiles();
        var indexFile = indexFiles?.FirstOrDefault(t => string.Equals(t.Name, "index.bdmv", StringComparison.OrdinalIgnoreCase));

        if (indexFile is not null)
        {
            using var indexStream = indexFile.OpenRead();
            ReadIndexVersion(indexStream);
        }

        if (null != GetDirectory("BDSVM", DirectoryRoot, 0))
        {
            IsBDPlus = true;
        }
        if (null != GetDirectory("SLYVM", DirectoryRoot, 0))
        {
            IsBDPlus = true;
        }
        if (null != GetDirectory("ANYVM", DirectoryRoot, 0))
        {
            IsBDPlus = true;
        }

        if (DirectoryBDJO?.GetFiles().Length > 0)
        {
            IsBDJava = true;
        }

        if (DirectorySNP is not null &&
            (DirectorySNP.GetFiles("*.mnv").Length > 0 || DirectorySNP.GetFiles("*.MNV").Length > 0))
        {
            IsPSP = true;
        }

        if (DirectorySSIF?.GetFiles().Length > 0)
        {
            Is3D = true;
        }

        var fullName = DirectoryRoot.FullName;
        if (fullName is not null && File.Exists(Path.Combine(fullName, "FilmIndex.xml")))
        {
            IsDBOX = true;
        }

        var metaFiles = DirectoryMeta?.GetFiles("bdmt_eng.xml", SearchOption.AllDirectories);
        if (metaFiles is { Length: > 0 })
        {
            ReadDiscTitle(metaFiles.FirstOrDefault().OpenText());
        }

        //
        // Initialize file lists.
        //
        if (DirectoryPLAYLIST is not null)
        {
            var files = DirectoryPLAYLIST.GetFiles("*.mpls");
            if (files.Length == 0)
            {
                files = DirectoryPLAYLIST.GetFiles("*.MPLS");
            }
            foreach (var file in files)
            {
                PlaylistFiles.Add(file.Name.ToUpper(), new TSPlaylistFile(this, file));
            }
        }

        if (DirectorySTREAM is not null)
        {
            var files = DirectorySTREAM.GetFiles("*.m2ts");
            if (files.Length == 0)
            {
                files = DirectorySTREAM.GetFiles("*.M2TS");
            }
            foreach (var file in files)
            {
                StreamFiles.Add(file.Name.ToUpper(), new TSStreamFile(file));
            }
        }

        if (DirectoryCLIPINF is not null)
        {
            var files = DirectoryCLIPINF.GetFiles("*.clpi");
            if (files.Length == 0)
            {
                files = DirectoryCLIPINF.GetFiles("*.CLPI");
            }
            foreach (var file in files)
            {
                StreamClipFiles.Add(file.Name.ToUpper(), new TSStreamClipFile(file));
            }
        }

        if (DirectorySSIF is not null)
        {
            var files = DirectorySSIF.GetFiles("*.ssif");
            if (files.Length == 0)
            {
                files = DirectorySSIF.GetFiles("*.SSIF");
            }
            foreach (var file in files)
            {
                InterleavedFiles.Add(file.Name.ToUpper(), new TSInterleavedFile(file));
            }
        }
    }

    private void ReadDiscTitle(StreamReader fileStream)
    {
        try
        {
            var xDoc = new XmlDocument();
            xDoc.Load(fileStream);
            var xNsMgr = new XmlNamespaceManager(xDoc.NameTable);
            xNsMgr.AddNamespace("di", "urn:BDA:bdmv;discinfo");
            var xNode = xDoc.DocumentElement?.SelectSingleNode("di:discinfo/di:title/di:name", xNsMgr);
            DiscTitle = xNode?.InnerText;

            if (!string.IsNullOrEmpty(DiscTitle) && string.Equals(DiscTitle, "blu-ray", StringComparison.OrdinalIgnoreCase))
                DiscTitle = null;
        }
        catch (Exception)
        {
            DiscTitle = null;
        }
        finally
        {
            fileStream.Close();
        }

    }

    public void Scan()
    {
        var errorStreamClipFiles = new List<TSStreamClipFile>();
        foreach (var streamClipFile in StreamClipFiles.Values)
        {
            try
            {
                streamClipFile.Scan();
            }
            catch (Exception ex)
            {
                errorStreamClipFiles.Add(streamClipFile);
                if (StreamClipFileScanError is not null)
                {
                    if (!StreamClipFileScanError(streamClipFile, ex))
                    {
                        break;
                    }
                }
                else throw;
            }
        }

        foreach (var streamFile in StreamFiles.Values)
        {
            var ssifName = Path.GetFileNameWithoutExtension(streamFile.Name) + ".SSIF";
            if (InterleavedFiles.ContainsKey(ssifName))
            {
                streamFile.InterleavedFile = InterleavedFiles[ssifName];
            }
        }

        var streamFiles = new TSStreamFile[StreamFiles.Count];
        StreamFiles.Values.CopyTo(streamFiles, 0);
        Array.Sort(streamFiles, CompareStreamFiles);

        var errorPlaylistFiles = new List<TSPlaylistFile>();
        foreach (var playlistFile in PlaylistFiles.Values)
        {
            try
            {
                playlistFile.Scan(StreamFiles, StreamClipFiles);
            }
            catch (Exception ex)
            {
                errorPlaylistFiles.Add(playlistFile);
                if (PlaylistFileScanError is not null)
                {
                    if (!PlaylistFileScanError(playlistFile, ex))
                    {
                        break;
                    }
                }
                else throw;
            }
        }

        var errorStreamFiles = new List<TSStreamFile>();
        foreach (var streamFile in streamFiles)
        {
            try
            {
                var playlists = PlaylistFiles.Values.Where(playlist =>
                        playlist.StreamClips.Any(streamClip => streamClip.Name == streamFile.Name))
                    .ToList();
                streamFile.Scan(playlists, false);
            }
            catch (Exception ex)
            {
                errorStreamFiles.Add(streamFile);
                if (StreamFileScanError is not null)
                {
                    if (!StreamFileScanError(streamFile, ex))
                    {
                        break;
                    }
                }
                else throw;
            }
        }

        foreach (var playlistFile in PlaylistFiles.Values)
        {
            playlistFile.Initialize();

            if (Is50Hz) continue;

            var vidStreamCount = playlistFile.VideoStreams.Count;
            foreach (var videoStream in playlistFile.VideoStreams)
            {
                if (videoStream.FrameRate is TSFrameRate.FRAMERATE_25 or TSFrameRate.FRAMERATE_50)
                {
                    Is50Hz = true;
                }

                if (vidStreamCount <= 1 || !Is3D) continue;

                switch (videoStream.StreamType)
                {
                    case TSStreamType.AVC_VIDEO when playlistFile.MVCBaseViewR:
                    case TSStreamType.MVC_VIDEO when !playlistFile.MVCBaseViewR:
                        videoStream.BaseView = true;
                        break;
                    case TSStreamType.AVC_VIDEO:
                    case TSStreamType.MVC_VIDEO:
                        videoStream.BaseView = false;
                        break;
                    default:
                        videoStream.BaseView = false;
                        break;
                }

            }
        }
    }

    private IDirectoryInfo GetDirectoryBDMV(IDirectoryInfo path)
    {
        var dir = path;

        while (dir is not null)
        {
            if (string.Equals(dir.Name, "BDMV", StringComparison.Ordinal))
            {
                return dir;
            }
            dir = dir.Parent;
        }

        return GetDirectory("BDMV", path, 0);
    }

    private static IDirectoryInfo GetDirectory(string name, IDirectoryInfo dir, int searchDepth)
    {
        if (dir is null) return null;

        var children = dir.GetDirectories();
        foreach (var child in children)
        {
            if (child.Name == name)
            {
                return child;
            }
        }

        if (searchDepth <= 0) return null;
        foreach (var child in children)
        {
            GetDirectory(
                name, child, searchDepth - 1);
        }

        return null;
    }

    private static long GetDirectorySize(IDirectoryInfo directoryInfo)
    {
        var pathFiles = directoryInfo.GetFiles();
        var size = pathFiles.Where(pathFile => !string.Equals(pathFile.Extension, ".SSIF", StringComparison.OrdinalIgnoreCase)).Sum(pathFile => pathFile.Length);

        var pathChildren = directoryInfo.GetDirectories();
        size += pathChildren.Sum(GetDirectorySize);
        return size;
    }

    public static int CompareStreamFiles(TSStreamFile x, TSStreamFile y)
    {
        // TODO: Use interleaved file sizes

        if (x.FileInfo is null && y.FileInfo is null)
        {
            return 0;
        }

        if (x.FileInfo is null && y.FileInfo is not null)
        {
            return 1;
        }

        if (y.FileInfo is null && x.FileInfo is not null)
        {
            return -1;
        }

        if (x.FileInfo.Length > y.FileInfo.Length)
        {
            return 1;
        }

        if (y.FileInfo.Length > x.FileInfo.Length)
        {
            return -1;
        }

        return 0;
    }

    private void ReadIndexVersion(Stream indexStream)
    {
        Span<byte> buffer = stackalloc byte[8];
        int count = indexStream.Read(buffer);
        int pos = 0;
        if (count > 0)
        {
            var indexVer = ToolBox.ReadString(buffer, ref pos);
            IsUHD = string.Equals(indexVer, "INDX0300", StringComparison.Ordinal);
        }
    }
}
