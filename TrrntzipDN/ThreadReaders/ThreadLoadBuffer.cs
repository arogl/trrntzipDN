﻿using System;
using System.IO;
using System.Threading;

namespace RomVaultX.SupportedFiles.Files
{
    class ThreadLoadBuffer : IDisposable
    {
        private readonly AutoResetEvent _waitEvent;
        private readonly AutoResetEvent _outEvent;
        private readonly Thread _tWorker;

        private byte[] _buffer;
        private int _size;
        private Stream _ds;
        private bool _finished;
        public bool errorState;

        public ThreadLoadBuffer(Stream ds)
        {
            _waitEvent = new AutoResetEvent(false);
            _outEvent = new AutoResetEvent(false);
            _finished = false;
            _ds = ds;
            errorState = false;

            _tWorker = new Thread(MainLoop);
            _tWorker.Start();
        }
        public void Dispose()
        {
            _waitEvent.Close();
            _outEvent.Close();
        }

        private void MainLoop()
        {
            while (true)
            {
                _waitEvent.WaitOne();
                if (_finished) break;
                try
                {
                    _ds.Read(_buffer, 0, _size);
                }
                catch (Exception)
                {
                    errorState = true;
                }
                _outEvent.Set();
            }
        }

        public void Trigger(byte[] buffer, int size)
        {
            _buffer = buffer;
            _size = size;
            _waitEvent.Set();
        }

        public void Wait()
        {
            _outEvent.WaitOne();
        }

        public void Finish()
        {
            _finished = true;
            _waitEvent.Set();
            _tWorker.Join();
        }
    }
}
