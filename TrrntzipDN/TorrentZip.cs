using System.Collections.Generic;
using IO;
using TrrntzipDN.SupportedFiles;
using TrrntzipDN.SupportedFiles.SevenZip;
using TrrntzipDN.SupportedFiles.ZipFile;


namespace TrrntzipDN
{
    public delegate void StatusCallback(int threadID, int precent);

    public delegate void LogCallback(int threadID, string log);

    class TorrentZip
    {
        public StatusCallback StatusCallBack;
        public LogCallback StatusLogCallBack;
        public int ThreadID;

        private readonly byte[] _buffer;

        public TorrentZip()
        {
            _buffer = new byte[1024 * 1024];
        }

        public TrrntZipStatus Process(FileInfo fi)
        {
            if (Program.VerboseLogging)
               StatusLogCallBack?.Invoke(ThreadID,"");

            StatusLogCallBack?.Invoke(ThreadID, fi.Name + " - ");
            
            // First open the zip (7z) file, and fail out if it is corrupt.

            ICompress zipFile;
            TrrntZipStatus tzs = OpenZip(fi, out zipFile);
            // this will return ValidTrrntZip or CorruptZip.

            if ((tzs & TrrntZipStatus.CorruptZip) == TrrntZipStatus.CorruptZip)
            {
                StatusLogCallBack?.Invoke(ThreadID, "Zip file is corrupt");
                return TrrntZipStatus.CorruptZip;
            }

            // the zip file may have found a valid trrntzip header, but we now check that all the file info
            // is actually valid, and may invalidate it being a valid trrntzip if any problem is found.

            List<ZippedFile> zippedFiles = ReadZipContent(zipFile);
            tzs |= TorrentZipCheck.CheckZipFiles(ref zippedFiles);

            // if tza is now just 'ValidTrrntzip' the it is fully valid, and nothing needs to be done to it.

            if (tzs == TrrntZipStatus.ValidTrrntzip && !Program.ForceReZip || Program.CheckOnly)
            {
                StatusLogCallBack?.Invoke(ThreadID, "Skipping File");
                return TrrntZipStatus.ValidTrrntzip;
            }

            StatusLogCallBack?.Invoke(ThreadID, "TorrentZipping");
            TrrntZipStatus fixedTzs = TorrentZipRebuild.ReZipFiles(zippedFiles, zipFile, _buffer,StatusCallBack,StatusLogCallBack,ThreadID);
            return fixedTzs;
        }


        private TrrntZipStatus OpenZip(IO.FileInfo fi, out ICompress zipFile)
        {
            string ext = Path.GetExtension(fi.Name);
            if (ext == ".7z")
                zipFile = new SevenZ();
            else
                zipFile = new ZipFile();

            ZipReturn zr = zipFile.ZipFileOpen(fi.FullName, fi.LastWriteTime, true);
            if (zr != ZipReturn.ZipGood)
                return TrrntZipStatus.CorruptZip;

            TrrntZipStatus tzStatus = TrrntZipStatus.Unknown;

            // first check if the file is a trrntip files
            if (zipFile.ZipStatus == ZipStatus.TrrntZip)
                tzStatus |= TrrntZipStatus.ValidTrrntzip;

            return tzStatus;
        }

        private List<ZippedFile> ReadZipContent(ICompress zipFile)
        {
            List<ZippedFile> zippedFiles = new List<ZippedFile>();
            for (int i = 0; i < zipFile.LocalFilesCount(); i++)
            {
                zippedFiles.Add(
                    new ZippedFile
                    {
                        Index = i,
                        Name = zipFile.Filename(i),
                        ByteCRC = zipFile.CRC32(i),
                        Size = zipFile.UncompressedSize(i)
                    }
                );
            }
            return zippedFiles;
        }
    }
}
