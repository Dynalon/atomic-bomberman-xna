﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BomberEngine.Core.IO;
using System.IO;
using BomberEngine.Debugging;

namespace BomberEngine.Demo
{
    public class DemoPlayer
    {
        private static int BitsPerCmdType = BitUtils.BitsToHoldUInt((int)DemoCmdType.Count);

        private BitReadBuffer m_buffer;
        private IDictionary<DemoCmdType, DemoCmd> m_cmdLookup;

        public DemoPlayer(String path)
        {   
            m_cmdLookup = new Dictionary<DemoCmdType, DemoCmd>();
            m_cmdLookup[DemoCmdType.Init] = new DemoInitCmd();
            m_cmdLookup[DemoCmdType.Input] = new DemoInputCmd();
            m_cmdLookup[DemoCmdType.Tick]  = new DemoTickCmd();

            Read(path);
        }

        private void Read(String path)
        {
            using (Stream stream = FileUtils.OpenRead(path))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    int bitLength = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    byte[] data = new byte[length];
                    int bytesRead = reader.Read(data, 0, length);
                    if (bytesRead != length)
                    {
                        throw new IOException("Wrong data size: " + bytesRead + " expected: " + length);
                    }

                    m_buffer = new BitReadBuffer(data, bitLength);
                }
            }
        }

        public void ReadTick()
        {
            while (m_buffer.BitsAvailable > 0)
            {
                DemoCmd cmd = Read(m_buffer);
                bool shouldStop = cmd.Execute();
                if (shouldStop)
                {
                    break;
                }
            }
        }

        //////////////////////////////////////////////////////////////////////////////

        #region Commands

        private DemoCmd Read(BitReadBuffer buffer)
        {
            DemoCmdType cmdType = (DemoCmdType)buffer.ReadUInt32(BitsPerCmdType);
            DemoCmd cmd = FindCmd(cmdType);
            if (cmd == null)
            {
                throw new Exception("Unexpected command: " + cmdType);
            }
            cmd.Read(buffer);
            return cmd;
        }

        private DemoCmd FindCmd(DemoCmdType type)
        {
            DemoCmd cmd;
            if (m_cmdLookup.TryGetValue(type, out cmd))
            {
                return cmd;
            }

            return null;
        }

        #endregion
    }
}