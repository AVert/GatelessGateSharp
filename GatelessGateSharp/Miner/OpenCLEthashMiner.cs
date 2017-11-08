﻿// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cloo;
using HashLib;



namespace GatelessGateSharp
{
    class OpenCLEthashMiner : OpenCLMiner
    {
        const int EPOCH_LENGTH = 30000;

        private ComputeProgram mProgram;
        private ComputeKernel mDAGKernel;
        private ComputeKernel mSearchKernel;
        private EthashStratum mStratum;
        private Thread mMinerThread = null;
        private long mLocalWorkSize = 192;
        private long mGlobalWorkSize;

        public OpenCLEthashMiner(Device aGatelessGateDevice, EthashStratum aStratum, int aIntensity)
            : base(aGatelessGateDevice, "Ethash")
        {
            mStratum = aStratum;
            mGlobalWorkSize = aIntensity * mLocalWorkSize * Device.GetComputeDevice().MaxComputeUnits;
            
            StartMinerThread();
        }

        unsafe override protected void MinerThread()
        {
            MarkAsAlive();

            MainForm.Logger("Miner thread for Device #" + DeviceIndex + " started.");
 
            mProgram = new ComputeProgram(this.Context, System.IO.File.ReadAllText(@"Kernels\ethash.cl"));
            MainForm.Logger("Loaded ethash program for Device #" + GatelessGateDevice.DeviceIndex + ".");
            List<ComputeDevice> deviceList = new List<ComputeDevice>();
            deviceList.Add(Device.GetComputeDevice());
            mProgram.Build(deviceList, "-DWORKSIZE=" + mLocalWorkSize, null, IntPtr.Zero);
            MainForm.Logger("Built ethash program for Device #" + GatelessGateDevice.DeviceIndex + ".");
            mDAGKernel = mProgram.CreateKernel("GenerateDAG");
            MainForm.Logger("Created DAG kernel for Device #" + GatelessGateDevice.DeviceIndex + ".");
            mSearchKernel = mProgram.CreateKernel("search");
            MainForm.Logger("Created search kernel for Device #" + GatelessGateDevice.DeviceIndex + ".");

            while (!Stopped) {

                MarkAsAlive();

                try
                {
                    // Wait for the first job to arrive.
                    int elapsedTime = 0;
                    while ((mStratum == null || mStratum.GetJob() == null) && elapsedTime < 5000)
                    {
                        Thread.Sleep(10);
                        elapsedTime += 10;
                    }
                    if (mStratum == null || mStratum.GetJob() == null)
                    {
                        MainForm.Logger("Stratum server failed to send a new job.");
                        //throw new TimeoutException("Stratum server failed to send a new job.");
                        return;
                    }

                    System.Diagnostics.Stopwatch consoleUpdateStopwatch = new System.Diagnostics.Stopwatch();
                    EthashStratum.Work work;
                    int epoch = -1;
                    long DAGSize = 0;
                    ComputeBuffer<byte> DAGBuffer = null;
                    ComputeBuffer<UInt32> outputBuffer = new ComputeBuffer<UInt32>(Context, ComputeMemoryFlags.ReadWrite, 256);
                    ComputeBuffer<byte> headerBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadOnly, 32);
                    UInt32[] output = new UInt32[256];
                    Random r = new Random();
                    while (!Stopped && (work = mStratum.GetWork()) != null)
                    {
                        String poolExtranonce = mStratum.PoolExtranonce;
                        byte[] extranonceByteArray = Utilities.StringToByteArray(poolExtranonce);
                        byte localExtranonce = work.LocalExtranonce;
                        UInt64 startNonce = (UInt64)localExtranonce << (8 * (7 - extranonceByteArray.Length));
                        for (int i = 0; i < extranonceByteArray.Length; ++i)
                            startNonce |= (UInt64)extranonceByteArray[i] << (8 * (7 - i));
                        startNonce += (ulong)r.Next(0, int.MaxValue) & (0xfffffffffffffffful >> (extranonceByteArray.Length * 8 + 8));
                        String jobID = work.GetJob().ID;
                        String headerhash = work.GetJob().Headerhash;
                        String seedhash = work.GetJob().Seedhash;
                        double difficulty = mStratum.Difficulty;
                        fixed (byte* p = Utilities.StringToByteArray(headerhash))
                            Queue.Write<byte>(headerBuffer, true, 0, 32, (IntPtr)p, null);

                        if (epoch != work.GetJob().Epoch)
                        {
                            if (DAGBuffer != null)
                            {
                                DAGBuffer.Dispose();
                                DAGBuffer = null;
                            }
                            epoch = work.GetJob().Epoch;
                            DAGCache cache = new DAGCache(epoch, work.GetJob().Seedhash);
                            DAGSize = Utilities.GetDAGSize(epoch);

                            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                            sw.Start();
                            fixed (byte* p = cache.GetData())
                            {
                                long globalWorkSize = DAGSize / 64;
                                globalWorkSize /= 8;
                                if (globalWorkSize % mLocalWorkSize > 0)
                                    globalWorkSize += mLocalWorkSize - globalWorkSize % mLocalWorkSize;

                                ComputeBuffer<byte> DAGCacheBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, cache.GetData().Length, (IntPtr)p);
                                DAGBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite, globalWorkSize * 8 * 64 /* DAGSize */); // With this, we can remove a conditional statement in the DAG kernel.

                                mDAGKernel.SetValueArgument<UInt32>(0, 0);
                                mDAGKernel.SetMemoryArgument(1, DAGCacheBuffer);
                                mDAGKernel.SetMemoryArgument(2, DAGBuffer);
                                mDAGKernel.SetValueArgument<UInt32>(3, (UInt32)cache.GetData().Length / 64);
                                //mDAGKernel.SetValueArgument<UInt32>(4, (UInt32)(DAGSize / 64));
                                mDAGKernel.SetValueArgument<UInt32>(4, 0xffffffffu);

                                for (long start = 0; start < DAGSize / 64; start += globalWorkSize)
                                {
                                    Queue.Execute(mDAGKernel, new long[] { start }, new long[] { globalWorkSize }, new long[] { mLocalWorkSize }, null);
                                    Queue.Finish();
                                    if (Stopped || !mStratum.GetJob().ID.Equals(jobID))
                                        break;
                                }
                                DAGCacheBuffer.Dispose();
                                if (Stopped || !mStratum.GetJob().ID.Equals(jobID))
                                    break;
                            }
                            sw.Stop();
                            MainForm.Logger("Generated DAG for Epoch #" + epoch + " (" + (long)sw.Elapsed.TotalMilliseconds + "ms).");
                        }

                        consoleUpdateStopwatch.Start();

                        while (!Stopped && mStratum.GetJob().ID.Equals(jobID) && mStratum.PoolExtranonce.Equals(poolExtranonce))
                        {
                            MarkAsAlive();

                            // Get a new local extranonce if necessary.
                            if ((startNonce & (0xfffffffffffffffful >> (extranonceByteArray.Length * 8 + 8)) + (ulong)mGlobalWorkSize) >= ((ulong)0x1 << (64 - (extranonceByteArray.Length * 8 + 8))))
                                break;

                            UInt64 target = (UInt64)((double)0xffff0000U / difficulty);
                            mSearchKernel.SetMemoryArgument(0, outputBuffer); // g_output
                            mSearchKernel.SetMemoryArgument(1, headerBuffer); // g_header
                            mSearchKernel.SetMemoryArgument(2, DAGBuffer); // _g_dag
                            mSearchKernel.SetValueArgument<UInt32>(3, (UInt32)(DAGSize / 128)); // DAG_SIZE
                            mSearchKernel.SetValueArgument<UInt64>(4, startNonce); // start_nonce
                            mSearchKernel.SetValueArgument<UInt64>(5, target); // target
                            mSearchKernel.SetValueArgument<UInt32>(6, 0xffffffffu); // isolate

                            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                            sw.Start();
                            fixed (UInt32* p = output)
                            {
                                output[255] = 0; // output[255] is used as an atomic counter.
                                Queue.Write<UInt32>(outputBuffer, true, 0, 256, (IntPtr)p, null);
                                Queue.Execute(mSearchKernel, new long[] { 0 }, new long[] { mGlobalWorkSize }, new long[] { mLocalWorkSize }, null);
                                Queue.Read<UInt32>(outputBuffer, true, 0, 256, (IntPtr)p, null);
                            }
                            if (Stopped || !mStratum.GetJob().ID.Equals(jobID))
                                break;
                            sw.Stop();
                            mSpeed = ((double)mGlobalWorkSize) / sw.Elapsed.TotalSeconds;
                            if (consoleUpdateStopwatch.ElapsedMilliseconds >= 10 * 1000)
                            {
                                MainForm.Logger("Device #" + DeviceIndex + ": " + String.Format("{0:N2} Mh/s", mSpeed / (1000000)));
                                consoleUpdateStopwatch.Restart();
                            }
                            for (int i = 0; i < output[255]; ++i)
                                mStratum.Submit(GatelessGateDevice, work.GetJob(), startNonce + (UInt64)output[i]);
                            startNonce += (UInt64)mGlobalWorkSize;
                        }
                    }

                    headerBuffer.Dispose();
                    outputBuffer.Dispose();
                    DAGBuffer.Dispose();
                    mSpeed = 0;
                }
                catch (Exception ex)
                {
                    MainForm.Logger("Exception in miner thread: " + ex.Message + ex.StackTrace);
                    MainForm.Logger("Restarting miner thread...");
                }
            }

            foreach (var kernel in new ComputeKernel[] { mSearchKernel, mDAGKernel })
                kernel.Dispose();
            mProgram.Dispose();
            Dispose();

            MarkAsDone();
        }
    }
}
