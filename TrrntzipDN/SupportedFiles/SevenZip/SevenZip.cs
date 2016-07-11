using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Compressor.BZip2;
using TrrntzipDN.SupportedFiles.SevenZip.Common;
using TrrntzipDN.SupportedFiles.SevenZip.Compress.LZMA;
using TrrntzipDN.SupportedFiles.SevenZip.Filters;
using TrrntzipDN.SupportedFiles.SevenZip.Structure;
using CRC = TrrntzipDN.SupportedFiles.SevenZip.Common.CRC;

namespace TrrntzipDN.SupportedFiles.SevenZip
{
    public class SevenZ : ICompress
    {
        public class LocalFile
        {
            public string FileName;
            public ulong UncompressedSize;
            public bool isDirectory;
            public byte[] crc;
            public byte[] sha1;
            public byte[] md5;
            public int StreamIndex;
            public ulong StreamOffset;
            public ZipReturn FileStatus = ZipReturn.ZipUntested;
        }

        private List<LocalFile> _localFiles = new List<LocalFile>();

        private IO.FileInfo _zipFileInfo;

        private FileStream _zipFs;

        private ZipOpenType _zipOpen;
        private ZipStatus _pZipStatus;

        SignatureHeader _signatureHeader;

        private bool _compressed = true;

        public string ZipFilename
        {
            get { return _zipFileInfo != null ? _zipFileInfo.FullName : ""; }
        }
        public long TimeStamp
        {
            get { return _zipFileInfo != null ? _zipFileInfo.LastWriteTime : 0; }
        }

        public ZipOpenType ZipOpen { get { return _zipOpen; } }
        public ZipStatus ZipStatus { get { return _pZipStatus; } }
        public int LocalFilesCount() { return _localFiles.Count; }
        public string Filename(int i) { return _localFiles[i].FileName; }
        public ulong? LocalHeader(int i) { return 0; }
        public ulong UncompressedSize(int i) { return _localFiles[i].UncompressedSize; }
        public ZipReturn FileStatus(int i) { return _localFiles[i].FileStatus; }
        public byte[] CRC32(int i) { return _localFiles[i].crc; }
        public byte[] MD5(int i) { return _localFiles[i].md5; }
        public byte[] SHA1(int i) { return _localFiles[i].sha1; }
        public bool IsDirectory(int i) { return _localFiles[i].isDirectory; }


        private long _baseOffset;


        #region open 7z files

        public ZipReturn ZipFileOpen(string filename, long timestamp, bool readHeaders)
        {
            Debug.WriteLine(filename);
            #region open file stream
            try
            {
                if (!IO.File.Exists(filename))
                {
                    ZipFileClose();
                    return ZipReturn.ZipErrorFileNotFound;
                }
                _zipFileInfo = new IO.FileInfo(filename);
                if (timestamp != -1 && _zipFileInfo.LastWriteTime != timestamp)
                {
                    ZipFileClose();
                    return ZipReturn.ZipErrorTimeStamp;
                }
                int errorCode = IO.FileStream.OpenFileRead(filename, out _zipFs);
                if (errorCode != 0)
                {
                    ZipFileClose();
                    return ZipReturn.ZipErrorOpeningFile;
                }
            }
            catch (PathTooLongException)
            {
                ZipFileClose();
                return ZipReturn.ZipFileNameToLong;
            }
            catch (IOException)
            {
                ZipFileClose();
                return ZipReturn.ZipErrorOpeningFile;
            }
            #endregion

            _zipOpen = ZipOpenType.OpenRead;
            _pZipStatus = ZipStatus.None;

            SignatureHeader signatureHeader = new SignatureHeader();
            if (!signatureHeader.Read(new BinaryReader(_zipFs)))
                return ZipReturn.ZipSignatureError;

            _baseOffset = _zipFs.Position; Util.log("BaseOffset : " + _baseOffset);

            Util.log("Loading Stream : " + (_baseOffset + (long)signatureHeader.NextHeaderOffset) + " , Size : " + signatureHeader.NextHeaderSize);

            //_zipFs.Seek(_baseOffset + (long)signatureHeader.NextHeaderOffset, SeekOrigin.Begin);
            //byte[] mainHeader = new byte[signatureHeader.NextHeaderSize];
            //_zipFs.Read(mainHeader, 0, (int)signatureHeader.NextHeaderSize);
            //if (!CRC.VerifyDigest(signatureHeader.NextHeaderCRC, mainHeader, 0, (uint)signatureHeader.NextHeaderSize))
            //    return ZipReturn.Zip64EndOfCentralDirError;

            _zipFs.Seek(_baseOffset + (long)signatureHeader.NextHeaderOffset, SeekOrigin.Begin);
            ZipReturn zr = Header.ReadHeaderOrPackedHeader(_zipFs, _baseOffset, out _header);
            if (zr != ZipReturn.ZipGood)
                return zr;

            _zipFs.Seek(_baseOffset + (long)(signatureHeader.NextHeaderOffset + signatureHeader.NextHeaderSize), SeekOrigin.Begin);
            _pZipStatus = Istorrent7Z() ? ZipStatus.TrrntZip : ZipStatus.None;
            PopulateLocalFiles(out _localFiles);

            return ZipReturn.ZipGood;
        }


        private void PopulateLocalFiles(out List<LocalFile> localFiles)
        {
            int emptyFileIndex = 0;
            int folderIndex = 0;
            int unpackedStreamsIndex = 0;
            ulong streamOffset = 0;
            localFiles = new List<LocalFile>();

            for (int i = 0; i < _header.FileInfo.Names.Length; i++)
            {
                LocalFile lf = new LocalFile { FileName = _header.FileInfo.Names[i] };

                if (_header.FileInfo.EmptyStreamFlags == null || !_header.FileInfo.EmptyStreamFlags[i])
                {
                    lf.StreamIndex = folderIndex;
                    lf.StreamOffset = streamOffset;
                    lf.UncompressedSize = _header.StreamsInfo.Folders[folderIndex].UnpackedStreamInfo[unpackedStreamsIndex].UnpackedSize;
                    lf.crc = Util.uinttobytes(_header.StreamsInfo.Folders[folderIndex].UnpackedStreamInfo[unpackedStreamsIndex].Crc);

                    streamOffset += lf.UncompressedSize;
                    unpackedStreamsIndex++;

                    if (unpackedStreamsIndex >= _header.StreamsInfo.Folders[folderIndex].UnpackedStreamInfo.Length)
                    {
                        folderIndex++;
                        unpackedStreamsIndex = 0;
                        streamOffset = 0;
                    }
                }
                else
                {
                    lf.UncompressedSize = 0;
                    lf.crc = new byte[] { 0, 0, 0, 0 };
                    lf.isDirectory = _header.FileInfo.EmptyFileFlags == null || !_header.FileInfo.EmptyFileFlags[emptyFileIndex++];

                    if (lf.isDirectory)
                        if (lf.FileName.Substring(lf.FileName.Length - 1, 1) != "/")
                            lf.FileName += "/";
                }

                localFiles.Add(lf);
            }
        }

        public void ZipFileClose()
        {
            ZipFileClose(null);
        }

        public void ZipFileClose(ICodeProgress p)
        {
            if (_zipOpen == ZipOpenType.Closed)
                return;

            if (_zipOpen == ZipOpenType.OpenRead)
            {
                ZipFileCloseReadStream();
                if (_zipFs != null)
                {
                    _zipFs.Close();
                    _zipFs.Dispose();
                }
                _zipOpen = ZipOpenType.Closed;
                return;
            }

            CloseWriting7Zip(p);

            _zipFileInfo = new IO.FileInfo(_zipFileInfo.FullName);
            _zipOpen = ZipOpenType.Closed;
        }

        private Header _header;



        private bool Istorrent7Z()
        {
            const int crcsz = 128;
            const int t7ZsigSize = 16 + 1 + 9 + 4 + 4;
            byte[] kSignature = { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C };
            int kSignatureSize = kSignature.Length;
            const string sig = "\xa9\x9f\xd1\x57\x08\xa9\xd7\xea\x29\x64\xb2\x36\x1b\x83\x52\x33\x01torrent7z_0.9beta";
            byte[] t7Zid = Util.Enc.GetBytes(sig);
            int t7ZidSize = t7Zid.Length;

            const int tmpbufsize = 256 + t7ZsigSize + 8 + 4;
            byte[] buffer = new byte[tmpbufsize];

            // read fist 128 bytes, pad with zeros if less bytes
            int offs = 0;
            _zipFs.Seek(0, SeekOrigin.Begin);
            int ar = _zipFs.Read(buffer, offs, crcsz);
            if (ar < crcsz)
                Util.memset(buffer, offs + ar, 0, crcsz - ar);


            offs = crcsz;
            long foffs = _zipFs.Length;
            foffs = foffs < (crcsz + t7ZsigSize + 4) ? 0 : foffs - (crcsz + t7ZsigSize + 4);
            _zipFs.Seek(foffs, SeekOrigin.Begin);

            ar = _zipFs.Read(buffer, offs, (crcsz + t7ZsigSize + 4));
            if (ar < (crcsz + t7ZsigSize + 4))
            {
                if (ar >= t7ZsigSize + 4)
                {
                    ar -= t7ZsigSize + 4;
                }
                if (ar < kSignatureSize)
                {
                    ar = kSignatureSize;
                }
                Util.memset(buffer, offs + ar, 0, crcsz - ar);
                Util.memcpyr(buffer, crcsz * 2 + 8, buffer, offs + ar, t7ZsigSize + 4);
            }
            else
                Util.memcpyr(buffer, crcsz * 2 + 8, buffer, crcsz * 2, t7ZsigSize + 4);

            foffs = _zipFs.Length;
            foffs -= t7ZsigSize + 4;

            //memcpy(buffer, crcsz * 2, &foffs, 8);
            buffer[crcsz * 2 + 0] = (byte)((foffs >> 0) & 0xff);
            buffer[crcsz * 2 + 1] = (byte)((foffs >> 8) & 0xff);
            buffer[crcsz * 2 + 2] = (byte)((foffs >> 16) & 0xff);
            buffer[crcsz * 2 + 3] = (byte)((foffs >> 24) & 0xff);
            buffer[crcsz * 2 + 4] = 0;
            buffer[crcsz * 2 + 5] = 0;
            buffer[crcsz * 2 + 6] = 0;
            buffer[crcsz * 2 + 7] = 0;

            if (Util.memcmp(buffer, 0, kSignature, kSignatureSize))
            {
                t7Zid[16] = buffer[crcsz * 2 + 4 + 8 + 16];
                if (Util.memcmp(buffer, crcsz * 2 + 4 + 8, t7Zid, t7ZidSize))
                {
                    UInt32 inCrc32 = (UInt32)((buffer[crcsz * 2 + 8 + 0]) +
                                             (buffer[crcsz * 2 + 8 + 1] << 8) +
                                             (buffer[crcsz * 2 + 8 + 2] << 16) +
                                             (buffer[crcsz * 2 + 8 + 3] << 24));

                    buffer[crcsz * 2 + 8 + 0] = 0xff;
                    buffer[crcsz * 2 + 8 + 1] = 0xff;
                    buffer[crcsz * 2 + 8 + 2] = 0xff;
                    buffer[crcsz * 2 + 8 + 3] = 0xff;

                    uint calcCrc32 = CRC.CalculateDigest(buffer, 0, crcsz * 2 + 8 + t7ZsigSize + 4);

                    if (inCrc32 == calcCrc32) return true;
                }
            }

            return false;
        }



        #endregion


        public void DeepScan()
        {
            const int bufferSize = 4096 * 128;
            byte[] buffer = new byte[bufferSize];

            for (int index = 0; index < _localFiles.Count; index++)
            {
                if (_localFiles[index].isDirectory || _localFiles[index].UncompressedSize == 0)
                {
                    _localFiles[index].md5 = new byte[] { 0xd4, 0x1d, 0x8c, 0xd9, 0x8f, 0x00, 0xb2, 0x04, 0xe9, 0x80, 0x09, 0x98, 0xec, 0xf8, 0x42, 0x7e };
                    _localFiles[index].sha1 = new byte[] { 0xda, 0x39, 0xa3, 0xee, 0x5e, 0x6b, 0x4b, 0x0d, 0x32, 0x55, 0xbf, 0xef, 0x95, 0x60, 0x18, 0x90, 0xaf, 0xd8, 0x07, 0x09 };
                    _localFiles[index].FileStatus = ZipReturn.ZipGood;

                    continue;
                }

                ulong sizetogo;
                Stream inStream;
                ZipReturn zr = ZipFileOpenReadStream(index, out inStream, out sizetogo);
                if (zr != ZipReturn.ZipGood)
                    continue;

                if (inStream == null)
                    continue;

                CRC crc32 = new CRC();
                MD5 lmd5 = System.Security.Cryptography.MD5.Create();
                SHA1 lsha1 = System.Security.Cryptography.SHA1.Create();

                while (sizetogo > 0)
                {
                    int sizenow = sizetogo > (ulong)bufferSize ? bufferSize : (int)sizetogo;
                    inStream.Read(buffer, 0, sizenow);

                    crc32.Update(buffer, 0, (uint)sizenow);
                    lmd5.TransformBlock(buffer, 0, sizenow, null, 0);
                    lsha1.TransformBlock(buffer, 0, sizenow, null, 0);

                    sizetogo = sizetogo - (ulong)sizenow;
                }

                lmd5.TransformFinalBlock(buffer, 0, 0);
                lsha1.TransformFinalBlock(buffer, 0, 0);

                byte[] testcrc = Util.uinttobytes(crc32.GetDigest());
                _localFiles[index].md5 = lmd5.Hash;
                _localFiles[index].sha1 = lsha1.Hash;

                _localFiles[index].FileStatus = Util.ByteArrCompare(_localFiles[index].crc, testcrc) ? ZipReturn.ZipGood : ZipReturn.ZipCRCDecodeError;
            }
        }



        #region read 7z file

        private int streamIndex = -1;
        private Stream _stream;

        public ZipReturn ZipFileOpenReadStream(int index, out Stream stream, out ulong unCompressedSize)
        {

            Debug.WriteLine("Opening File " + _localFiles[index].FileName);
            stream = null;
            unCompressedSize = 0;

            if (_zipOpen != ZipOpenType.OpenRead)
                return ZipReturn.ZipErrorGettingDataStream;

            if (IsDirectory(index))
                return ZipReturn.ZipTryingToAccessADirectory;

            unCompressedSize = _localFiles[index].UncompressedSize;
            int thisStreamIndex = _localFiles[index].StreamIndex;
            ulong streamOffset = _localFiles[index].StreamOffset;

            if (thisStreamIndex == streamIndex && streamOffset >= (ulong) _stream.Position)
            {
                stream = _stream;
                stream.Seek((long)_localFiles[index].StreamOffset-_stream.Position, SeekOrigin.Current);
                return ZipReturn.ZipGood;
            }

            ZipFileCloseReadStream();
            streamIndex = thisStreamIndex;


            Folder folder = _header.StreamsInfo.Folders[streamIndex];

            // first make the List of Decompressors streams
            int codersNeeded = folder.Coders.Length;

            List<InStreamSourceInfo> allInputStreams = new List<InStreamSourceInfo>();
            for (int i = 0; i < codersNeeded; i++)
            {
                folder.Coders[i].decoderStream = null;
                allInputStreams.AddRange(folder.Coders[i].InputStreamsSourceInfo);
            }

            // now use the binding pairs to links the outputs to the inputs
            int bindPairsCount = folder.BindPairs.Length;
            for (int i = 0; i < bindPairsCount; i++)
            {
                allInputStreams[(int)folder.BindPairs[i].InIndex].InStreamSource = InStreamSource.CompStreamOutput;
                allInputStreams[(int)folder.BindPairs[i].InIndex].InStreamIndex = folder.BindPairs[i].OutIndex;
                folder.Coders[(int)folder.BindPairs[i].OutIndex].OutputUsedInternally = true;
            }

            // next use the stream indises to connect the remaining input streams from the sourcefile
            int packedStreamsCount = folder.PackedStreamIndices.Length;
            for (int i = 0; i < packedStreamsCount; i++)
            {
                ulong packedStreamIndex = (ulong)i + folder.PackedStreamIndexBase;

                // create and open the source file stream if needed
                if (_header.StreamsInfo.PackedStreams[packedStreamIndex].PackedStream == null)
                {
                    int errorCode = IO.FileStream.OpenFileRead(_zipFileInfo.FullName, out _header.StreamsInfo.PackedStreams[packedStreamIndex].PackedStream);
                    if (errorCode != 0)
                        return ZipReturn.ZipErrorOpeningFile;
                }
                _header.StreamsInfo.PackedStreams[packedStreamIndex].PackedStream.Seek(
                    _baseOffset + (long)_header.StreamsInfo.PackedStreams[packedStreamIndex].StreamPosition, SeekOrigin.Begin);


                allInputStreams[(int)folder.PackedStreamIndices[i]].InStreamSource = InStreamSource.FileStream;
                allInputStreams[(int)folder.PackedStreamIndices[i]].InStreamIndex = packedStreamIndex;
            }

            List<Stream> inputCoders = new List<Stream>();

            bool allCodersComplete = false;
            while (!allCodersComplete)
            {
                allCodersComplete = true;
                for (int i = 0; i < codersNeeded; i++)
                {
                    Coder coder = folder.Coders[i];

                    // check is decoder already processed
                    if (coder.decoderStream != null)
                        continue;

                    inputCoders.Clear();
                    for (int j = 0; j < (int)coder.NumInStreams; j++)
                    {
                        if (coder.InputStreamsSourceInfo[j].InStreamSource == InStreamSource.FileStream)
                        {
                            inputCoders.Add(_header.StreamsInfo.PackedStreams[coder.InputStreamsSourceInfo[j].InStreamIndex].PackedStream);
                        }
                        else if (coder.InputStreamsSourceInfo[j].InStreamSource == InStreamSource.CompStreamOutput)
                        {
                            if (folder.Coders[coder.InputStreamsSourceInfo[j].InStreamIndex].decoderStream == null)
                                break;
                            inputCoders.Add(folder.Coders[coder.InputStreamsSourceInfo[j].InStreamIndex].decoderStream);
                        }
                        else
                        {
                            // unknown input type so error
                            return ZipReturn.ZipDecodeError;
                        }
                    }

                    if (inputCoders.Count == (int)coder.NumInStreams)
                    {
                        // all inputs streams are available to make the decoder stream
                        switch (coder.DecoderType)
                        {
                            case DecompressType.LZMA:
                                coder.decoderStream = new LzmaStream(folder.Coders[i].Properties, inputCoders[0]);
                                break;
                            case DecompressType.LZMA2:
                                coder.decoderStream = new LzmaStream(folder.Coders[i].Properties, inputCoders[0]);
                                break;
                            //case DecompressType.PPMd:
                            //    coder.decoderStream = new PpmdStream(folder.Coders[i].Properties,inputCoders[0],false);
                            //    break;
                            case DecompressType.BZip2:
                                coder.decoderStream = new BZip2Stream(inputCoders[0],CompressionMode.Decompress,true);
                                break;
                            case DecompressType.BCJ:
                                coder.decoderStream = new BCJFilter(false, inputCoders[0]);
                                break;
                            case DecompressType.BCJ2:
                                coder.decoderStream = new BCJ2Filter(inputCoders[0], inputCoders[1], inputCoders[2], inputCoders[3]);
                                break;
                            default:
                                return ZipReturn.ZipDecodeError;
                        }
                    }

                    // if skipped a coder need to loop round again
                    if (coder.decoderStream == null)
                        allCodersComplete = false;
                }


            }
            // find the final output stream and return it.
            int outputStream = -1;
            for (int i = 0; i < codersNeeded; i++)
            {
                Coder coder = folder.Coders[i];
                if (!coder.OutputUsedInternally)
                    outputStream = i;
            }

            stream = folder.Coders[outputStream].decoderStream;
            stream.Seek((long)_localFiles[index].StreamOffset, SeekOrigin.Current);

            _stream = stream;

            return ZipReturn.ZipGood;

        }


        public ZipReturn ZipFileCloseReadStream()
        {
            if (streamIndex != -1)
            {
                Folder folder = _header.StreamsInfo.Folders[streamIndex];

                foreach (Coder c in folder.Coders)
                {
                    Stream ds = c.decoderStream;
                    if (ds == null)
                        continue;
                    ds.Close();
                    ds.Dispose();
                    c.decoderStream = null;
                }
            }
            streamIndex = -1;

            foreach (PackedStreamInfo psi in _header.StreamsInfo.PackedStreams)
            {
                if (psi.PackedStream == null)
                    continue;
                psi.PackedStream.Close();
                psi.PackedStream.Dispose();
                psi.PackedStream = null;
            }
            return ZipReturn.ZipGood;
        }

        #endregion


        #region write 7z File

        private LzmaStream _lzmaStream;
        private ulong _packStreamStart;
        private ulong _packStreamSize;
        private ulong _unpackedStreamSize;
        private byte[] _codeMSbytes;


        public void ZipFileAddDirectory()
        {
            // do nothing here for 7zip
        }

        public ZipReturn ZipFileCreate(string newFilename)
        {
            return ZipFileCreate(newFilename, true);
        }


        public ZipReturn ZipFileCreate(string newFilename, bool compressOutput)
        {
            if (_zipOpen != ZipOpenType.Closed)
                return ZipReturn.ZipFileAlreadyOpen;

            DirUtil.CreateDirForFile(newFilename);
            _zipFileInfo = new IO.FileInfo(newFilename);

            int errorCode = IO.FileStream.OpenFileWrite(newFilename, out _zipFs);
            if (errorCode != 0)
            {
                ZipFileClose();
                return ZipReturn.ZipErrorOpeningFile;
            }
            _zipOpen = ZipOpenType.OpenWrite;

            _signatureHeader = new SignatureHeader();
            _header = new Header();

            BinaryWriter bw = new BinaryWriter(_zipFs);
            _signatureHeader.Write(bw);

            _compressed = compressOutput;

            _unpackedStreamSize = 0;
            if (_compressed)
            {
                LzmaEncoderProperties ep = new LzmaEncoderProperties(true, 1 << 24, 128);
                _lzmaStream = new LzmaStream(ep, false, _zipFs);
                _codeMSbytes = _lzmaStream.Properties;
                _packStreamStart = (ulong)_zipFs.Position;
            }

            return ZipReturn.ZipGood;
        }

        public ZipReturn ZipFileOpenWriteStream(bool raw, bool trrntzip, string filename, ulong uncompressedSize, ushort compressionMethod, out Stream stream)
        {
            return ZipFileOpenWriteStream(filename, uncompressedSize, out stream);
        }
        public ZipReturn ZipFileOpenWriteStream(string filename, ulong uncompressedSize, out Stream stream)
        {
            LocalFile lf = new LocalFile
            {
                FileName = filename,
                UncompressedSize = uncompressedSize,
                StreamOffset = (ulong)(_zipFs.Position - _signatureHeader.BaseOffset)
            };

            _unpackedStreamSize += uncompressedSize;

            _localFiles.Add(lf);
            stream = _compressed ? _lzmaStream : (Stream)_zipFs;
            return ZipReturn.ZipGood;
        }


        public ZipReturn ZipFileCloseWriteStream(byte[] crc32)
        {
            _localFiles[_localFiles.Count - 1].crc = new[] { crc32[3], crc32[2], crc32[1], crc32[0] };
            return ZipReturn.ZipGood;
        }


        private void CloseWriting7Zip(ICodeProgress p = null)
        {
            if (_compressed)
                _lzmaStream.Close();

            _packStreamSize = (UInt64)_zipFs.Position - _packStreamStart;

            Create7ZStructure();

            byte[] newHeaderByte;
            using (Stream headerMem = new MemoryStream())
            {
                using (BinaryWriter headerBw = new BinaryWriter(headerMem))
                {
                    _header.WriteHeader(headerBw);
                    newHeaderByte = new byte[headerMem.Length];
                    headerMem.Position = 0;
                    headerMem.Read(newHeaderByte, 0, newHeaderByte.Length);
                }
            }

            CRC mainHeadercrc = new CRC();
            mainHeadercrc.Update(newHeaderByte, 0, (uint)newHeaderByte.Length);
            UInt32 mainHeaderCRC = mainHeadercrc.GetDigest();

            UInt64 headerpos = (UInt64)_zipFs.Position;
            BinaryWriter bw = new BinaryWriter(_zipFs);
            bw.Write(newHeaderByte);

            _signatureHeader.WriteFinal(bw, headerpos, (ulong)newHeaderByte.Length, mainHeaderCRC);

            _zipFs.Flush();
            _zipFs.Close();
            _zipFs.Dispose();
        }






        private void Create7ZStructure()
        {
            int fileCount = _localFiles.Count;

            //FileInfo
            _header.FileInfo = new Structure.FileInfo
            {
                Names = new string[fileCount]
            };

            ulong emptyStreamCount = 0;
            ulong emptyFileCount = 0;
            for (int i = 0; i < fileCount; i++)
            {
                _header.FileInfo.Names[i] = _localFiles[i].FileName;

                if (_localFiles[i].UncompressedSize != 0)
                    continue;

                if (!_localFiles[i].isDirectory)
                    emptyFileCount += 1;

                emptyStreamCount += 1;
            }
            ulong outFileCount = (ulong)_localFiles.Count - emptyStreamCount;

            _header.FileInfo.EmptyStreamFlags = null;
            _header.FileInfo.EmptyFileFlags = null;
            _header.FileInfo.Attributes = null;

            if (emptyStreamCount > 0)
            {
                if (emptyStreamCount != emptyFileCount) //then we found directories and need to set the attributes
                    _header.FileInfo.Attributes = new uint[fileCount];

                if (emptyFileCount > 0)
                    _header.FileInfo.EmptyFileFlags = new bool[emptyStreamCount];

                emptyStreamCount = 0;
                _header.FileInfo.EmptyStreamFlags = new bool[fileCount];
                for (int i = 0; i < fileCount; i++)
                {
                    if (_localFiles[i].UncompressedSize != 0)
                        continue;

                    if (_localFiles[i].isDirectory)
                        _header.FileInfo.Attributes[i] = 0x10;                      // set attributes to directory
                    else
                        _header.FileInfo.EmptyFileFlags[emptyStreamCount] = true;   // set empty file flag

                    _header.FileInfo.EmptyStreamFlags[i] = true;
                    emptyStreamCount += 1;
                }
            }



            //StreamsInfo
            _header.StreamsInfo = new StreamsInfo { PackPosition = 0 };

            //StreamsInfo.PackedStreamsInfo
            if (_compressed)
            {
                _header.StreamsInfo.PackedStreams = new PackedStreamInfo[1];
                _header.StreamsInfo.PackedStreams[0] = new PackedStreamInfo { PackedSize = _packStreamSize };
            }
            else
            {
                _header.StreamsInfo.PackedStreams = new PackedStreamInfo[outFileCount];
                int fileIndex = 0;
                for (int i = 0; i < fileCount; i++)
                {
                    if (_localFiles[i].UncompressedSize == 0)
                        continue;
                    _header.StreamsInfo.PackedStreams[fileIndex++] = new PackedStreamInfo { PackedSize = _localFiles[i].UncompressedSize };
                }
            }
            //StreamsInfo.PackedStreamsInfo, no CRC or StreamPosition required

            if (_compressed)
            {
                //StreamsInfo.Folders
                _header.StreamsInfo.Folders = new Folder[1];

                Folder folder = new Folder { Coders = new Coder[1] };

                //StreamsInfo.Folders.Coder
                // flags 0x23
                folder.Coders[0] = new Coder
                {
                    Method = new byte[] { 3, 1, 1 },
                    NumInStreams = 1,
                    NumOutStreams = 1,
                    Properties = _codeMSbytes
                };
                folder.BindPairs = null;
                folder.PackedStreamIndices = new[] { (ulong)0 };
                folder.UnpackedStreamSizes = new[] { _unpackedStreamSize };
                folder.UnpackCRC = null;

                folder.UnpackedStreamInfo = new UnpackedStreamInfo[outFileCount];
                int fileIndex = 0;
                for (int i = 0; i < fileCount; i++)
                {
                    if (_localFiles[i].UncompressedSize == 0)
                        continue;
                    UnpackedStreamInfo unpackedStreamInfo = new UnpackedStreamInfo
                    {
                        UnpackedSize = _localFiles[i].UncompressedSize,
                        Crc = Util.bytestouint(_localFiles[i].crc)
                    };
                    folder.UnpackedStreamInfo[fileIndex++] = unpackedStreamInfo;
                }
                _header.StreamsInfo.Folders[0] = folder;
            }
            else
            {
                _header.StreamsInfo.Folders = new Folder[outFileCount];
                int fileIndex = 0;
                for (int i = 0; i < fileCount; i++)
                {
                    if (_localFiles[i].UncompressedSize == 0)
                        continue;
                    Folder folder = new Folder { Coders = new Coder[1] };

                    //StreamsInfo.Folders.Coder
                    // flags 0x01
                    folder.Coders[0] = new Coder
                    {
                        Method = new byte[] { 0 },
                        NumInStreams = 1,
                        NumOutStreams = 1,
                        Properties = null
                    };

                    folder.BindPairs = null;
                    folder.PackedStreamIndices = new[] { (ulong)i };
                    folder.UnpackedStreamSizes = new[] { _localFiles[i].UncompressedSize };
                    folder.UnpackCRC = null;

                    folder.UnpackedStreamInfo = new UnpackedStreamInfo[1];
                    UnpackedStreamInfo unpackedStreamInfo = new UnpackedStreamInfo
                    {
                        UnpackedSize = _localFiles[i].UncompressedSize,
                        Crc = Util.bytestouint(_localFiles[i].crc)
                    };
                    folder.UnpackedStreamInfo[0] = unpackedStreamInfo;

                    _header.StreamsInfo.Folders[fileIndex++] = folder;
                }
            }
        }

        #endregion


        public void ZipFileAddDirectory(string filename)
        {
            LocalFile lf = new LocalFile
            {
                FileName = filename,
                UncompressedSize = 0,
                isDirectory = true,
                StreamOffset = 0
            };
            _localFiles.Add(lf);
        }

        public ZipReturn ZipFileRollBack()
        {
            throw new NotImplementedException();
        }

        public void ZipFileCloseFailed()
        {
            throw new NotImplementedException();
        }



    }
}
