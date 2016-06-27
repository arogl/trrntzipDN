﻿using System.IO;

namespace TrrntzipDN.SupportedFiles
{
    public interface ICompress
    {
        int LocalFilesCount();

        string Filename(int i);
        ulong? LocalHeader(int i);
        ulong UncompressedSize(int i);
        byte[] CRC32(int i);
        ZipReturn FileStatus(int i);
        byte[] MD5(int i);
        byte[] SHA1(int i);


        ZipOpenType ZipOpen { get; }

        ZipReturn ZipFileOpen(string newFilename, long timestamp, bool readHeaders);
        void ZipFileClose();
        ZipReturn ZipFileOpenWriteStream(bool raw, bool trrntzip, string filename, ulong uncompressedSize, ushort compressionMethod, out Stream stream);
        ZipReturn ZipFileCloseReadStream();
        void DeepScan();


        ZipStatus ZipStatus { get; }

        string ZipFilename { get; }
        long TimeStamp { get; }

        void ZipFileAddDirectory();

        ZipReturn ZipFileCreate(string newFilename);
        ZipReturn ZipFileCloseWriteStream(byte[] crc32);
        ZipReturn ZipFileRollBack();
        void ZipFileCloseFailed();

    }
}
