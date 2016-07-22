using System;
using System.Collections.Generic;
using System.IO;
using TrrntzipDN.SupportedFiles;
using TrrntzipDN.SupportedFiles.SevenZip;
using TrrntzipDN.SupportedFiles.ZipFile;
using TrrntzipDN.SupportedFiles.ZipFile.ZLib;

namespace TrrntzipDN
{
    public static class TorrentZipRebuild
    {
        public static TrrntZipStatus ReZipFiles(List<ZippedFile> zippedFiles, ICompress originalZipFile, byte[] buffer)
        {
            int bufferSize = buffer.Length;

            string filename = originalZipFile.ZipFilename;

            string tmpFilename = filename + ".tmp";

            if (IO.File.Exists(tmpFilename))
                IO.File.Delete(tmpFilename);

            ICompress zipFileOut = new SevenZ();

            zipFileOut.ZipFileCreate(tmpFilename);

            // by now the zippedFiles have been sorted so just loop over them
            for (int i = 0; i < zippedFiles.Count; i++)
            {
                ZippedFile t = zippedFiles[i];

                if (Program.VerboseLogging)
                {
                    Console.WriteLine("{0,15}  {1}   {2}", t.Size, t.StringCRC, t.Name);
                }


                Stream readStream = null;
                ulong streamSize = 0;
                ushort compMethod;

                ZipFile z = originalZipFile as ZipFile;
                ZipReturn zrInput = ZipReturn.ZipUntested;
                if (z != null)
                    zrInput = z.ZipFileOpenReadStream(t.Index, false, out readStream, out streamSize, out compMethod);
                SevenZ z7 = originalZipFile as SevenZ;
                if (z7 != null)
                    zrInput = z7.ZipFileOpenReadStream(t.Index, out readStream, out streamSize);

                Stream writeStream;
                ZipReturn zrOutput = zipFileOut.ZipFileOpenWriteStream(false, true, t.Name, streamSize, 8, out writeStream);

                if (zrInput != ZipReturn.ZipGood || zrOutput != ZipReturn.ZipGood)
                {
                    //Error writing local File.
                    zipFileOut.ZipFileClose();
                    originalZipFile.ZipFileClose();
                    IO.File.Delete(tmpFilename);
                    return TrrntZipStatus.CorruptZip;
                }

                Stream crcCs = new CrcCalculatorStream(readStream, true);

                ulong sizetogo = streamSize;
                while (sizetogo > 0)
                {
                    int sizenow = sizetogo > (ulong)bufferSize ? bufferSize : (int)sizetogo;

                    crcCs.Read(buffer, 0, sizenow);
                    writeStream.Write(buffer, 0, sizenow);
                    sizetogo = sizetogo - (ulong)sizenow;
                }
                writeStream.Flush();

                crcCs.Close();
                if (z!=null)
                originalZipFile.ZipFileCloseReadStream();

                uint crc = (uint)((CrcCalculatorStream)crcCs).Crc;

                if (crc != t.CRC)
                    return TrrntZipStatus.CorruptZip;

                zipFileOut.ZipFileCloseWriteStream(t.ByteCRC);
            }
            
            zipFileOut.ZipFileClose();
            originalZipFile.ZipFileClose();
            IO.File.Delete(filename);
            IO.File.Move(tmpFilename, filename);

            return TrrntZipStatus.ValidTrrntzip;
        }
    }
}
