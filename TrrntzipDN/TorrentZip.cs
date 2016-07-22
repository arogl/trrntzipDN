using System;
using System.Collections.Generic;
using TrrntzipDN.SupportedFiles;
using TrrntzipDN.SupportedFiles.SevenZip;
using TrrntzipDN.SupportedFiles.ZipFile;


namespace TrrntzipDN
{
    class TorrentZip
    {
        private readonly byte[] _buffer;

        public TorrentZip()
        {
            _buffer = new byte[1024 * 1024];
        }

        public bool Process(IO.FileInfo fi)
        {

            if (Program.VerboseLogging)
                Console.WriteLine();

            Console.Write(fi.Name + " - ");
            ICompress zipFile;
            TrrntZipStatus tzs = OpenZip(fi, out zipFile);
            if ((tzs & TrrntZipStatus.CorruptZip) == TrrntZipStatus.CorruptZip)
            {
                Console.WriteLine("Zip file is corrupt");
                return false;
            }

            List<ZippedFile> zippedFiles = ReadZipContent(zipFile);

            tzs |= TorrentZipCheck.CheckZipFiles(ref zippedFiles);

            if (tzs == TrrntZipStatus.ValidTrrntzip && !Program.ForceReZip || Program.CheckOnly)
            {
                Console.WriteLine("Skipping File");
                return true;
            }
            if (tzs != TrrntZipStatus.NotTrrntzipped && tzs != TrrntZipStatus.ValidTrrntzip)
                Console.WriteLine("Original torrentzip file was invalid");

         
            Console.WriteLine("TorrentZipping");
            TrrntZipStatus fixedTzs = TorrentZipRebuild.ReZipFiles(zippedFiles, zipFile, _buffer);
            return fixedTzs == TrrntZipStatus.ValidTrrntzip;
        }


        private TrrntZipStatus OpenZip(IO.FileInfo fi, out ICompress zipFile)
        {
            zipFile = new SevenZ();
            ZipReturn zr = zipFile.ZipFileOpen(fi.FullName, fi.LastWriteTime, true);
            if (zr != ZipReturn.ZipGood)
                return TrrntZipStatus.CorruptZip;

            TrrntZipStatus tzStatus = TrrntZipStatus.Unknown;

            // first check if the file is a trrntip files
            if (zipFile.ZipStatus != ZipStatus.TrrntZip)
                tzStatus |= TrrntZipStatus.NotTrrntzipped;

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
